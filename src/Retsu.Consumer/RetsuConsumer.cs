namespace Retsu.Consumer
{
	using Miki.Discord.Common;
	using Miki.Discord.Common.Events;
	using Miki.Discord.Common.Gateway;
	using Miki.Discord.Common.Packets;
	using Miki.Discord.Common.Packets.Events;
	using Miki.Logging;
	using RabbitMQ.Client;
	using RabbitMQ.Client.Events;
	using System;
    using System.Collections.Concurrent;
    using System.Text;
	using System.Threading.Tasks;
    using Miki.Discord.Common.Extensions;
    using Miki.Discord.Common.Packets.API;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Retsu.Models.Communication;

    public partial class RetsuConsumer : IConsumer, IGateway
	{
		public Func<DiscordChannelPacket, System.Threading.Tasks.Task> OnChannelCreate { get; set; }
		public Func<DiscordChannelPacket, System.Threading.Tasks.Task> OnChannelUpdate { get; set; }
		public Func<DiscordChannelPacket, System.Threading.Tasks.Task> OnChannelDelete { get; set; }
		public Func<DiscordGuildPacket, System.Threading.Tasks.Task> OnGuildCreate { get; set; }
		public Func<DiscordGuildPacket, System.Threading.Tasks.Task> OnGuildUpdate { get; set; }
		public Func<DiscordGuildUnavailablePacket, System.Threading.Tasks.Task> OnGuildDelete { get; set; }
		public Func<DiscordGuildMemberPacket, System.Threading.Tasks.Task> OnGuildMemberAdd { get; set; }
		public Func<ulong, DiscordUserPacket, System.Threading.Tasks.Task> OnGuildMemberRemove { get; set; }
		public Func<GuildMemberUpdateEventArgs, System.Threading.Tasks.Task> OnGuildMemberUpdate { get; set; }
		public Func<ulong, DiscordUserPacket, System.Threading.Tasks.Task> OnGuildBanAdd { get; set; }
		public Func<ulong, DiscordUserPacket, System.Threading.Tasks.Task> OnGuildBanRemove { get; set; }
		public Func<ulong, DiscordEmoji[], System.Threading.Tasks.Task> OnGuildEmojiUpdate { get; set; }
		public Func<ulong, DiscordRolePacket, System.Threading.Tasks.Task> OnGuildRoleCreate { get; set; }
		public Func<ulong, DiscordRolePacket, System.Threading.Tasks.Task> OnGuildRoleUpdate { get; set; }
		public Func<ulong, ulong, System.Threading.Tasks.Task> OnGuildRoleDelete { get; set; }
		public Func<DiscordMessagePacket, System.Threading.Tasks.Task> OnMessageCreate { get; set; }
		public Func<DiscordMessagePacket, System.Threading.Tasks.Task> OnMessageUpdate { get; set; }
		public Func<MessageDeleteArgs, System.Threading.Tasks.Task> OnMessageDelete { get; set; }
		public Func<MessageBulkDeleteEventArgs, System.Threading.Tasks.Task> OnMessageDeleteBulk { get; set; }
		public Func<DiscordPresencePacket, System.Threading.Tasks.Task> OnPresenceUpdate { get; set; }
		public Func<Miki.Discord.Common.Gateway.Packets.GatewayReadyPacket, System.Threading.Tasks.Task> OnReady { get; set; }
		public Func<TypingStartEventArgs, System.Threading.Tasks.Task> OnTypingStart { get; set; }
		public Func<DiscordPresencePacket, System.Threading.Tasks.Task> OnUserUpdate { get; set; }
        public event Func<GatewayMessage, System.Threading.Tasks.Task> OnPacketSent;
        public event Func<GatewayMessage, System.Threading.Tasks.Task> OnPacketReceived;

        private readonly IModel channel;

        private readonly ConcurrentDictionary<string, EventingBasicConsumer> consumers
            = new ConcurrentDictionary<string, EventingBasicConsumer>();

		private readonly ConsumerConfiguration config;

		public RetsuConsumer(ConsumerConfiguration config)
		{
            this.config = config;

            ConnectionFactory connectionFactory = new ConnectionFactory
            {
                Uri = config.ConnectionString,
                DispatchConsumersAsync = false
            };

            var connection = connectionFactory.CreateConnection();

			connection.CallbackException += (s, args) =>
			{
				Log.Error(args.Exception);
			};

			connection.ConnectionRecoveryError += (s, args) =>
			{
				Log.Error(args.Exception);
			};

			connection.RecoverySucceeded += (s, args) =>
			{
				Log.Debug("Rabbit Connection Recovered!");
			};

			channel = connection.CreateModel();
			channel.BasicQos(config.PrefetchSize, config.PrefetchCount, false);
			channel.ExchangeDeclare(config.ExchangeName, ExchangeType.Direct);
			channel.QueueDeclare(config.QueueName, config.QueueDurable, config.QueueExclusive, config.QueueAutoDelete, null);
			channel.QueueBind(config.QueueName, config.ExchangeName, config.ExchangeRoutingKey, null);

			var commandChannel = connectionFactory.CreateConnection().CreateModel();
			commandChannel.ExchangeDeclare(
                config.QueueName + "-command", ExchangeType.Fanout, true);
			commandChannel.QueueDeclare(
                config.QueueName + "-command", false, false, false);
			commandChannel.QueueBind(
                config.QueueName + "-command", 
                config.QueueName + "-command", 
                config.ExchangeRoutingKey, null);
		}

		public async System.Threading.Tasks.Task RestartAsync()
		{
			await StopAsync();
			await StartAsync();
		}

		public System.Threading.Tasks.Task StartAsync()
		{
			var consumer = new EventingBasicConsumer(channel);
			consumer.Received += async (ch, ea) => await OnMessageAsync(ch, ea);

			// TODO: remove once transition is complete.
			string _ = channel.BasicConsume(
                config.QueueName, config.ConsumerAutoAck, consumer);
            consumers.TryAdd("", consumer);

			return System.Threading.Tasks.Task.CompletedTask;
		}

		public System.Threading.Tasks.Task StopAsync()
		{
			return System.Threading.Tasks.Task.CompletedTask;
		}

		private async System.Threading.Tasks.Task OnMessageAsync(object ch, BasicDeliverEventArgs ea)
		{
			var payload = Encoding.UTF8.GetString(ea.Body);
			var body = JsonConvert.DeserializeObject<GatewayMessage>(payload);

			if(body.OpCode != GatewayOpcode.Dispatch)
			{
				channel.BasicAck(ea.DeliveryTag, false);
				Log.Trace("packet from gateway with op '" + body.OpCode + "' received");
				return;
			}

			try
			{
				Log.Trace("packet with the op-code '" + body.EventName + "' received.");
				switch(Enum.Parse(typeof(Miki.Discord.Rest.GatewayEventType), body.EventName.Replace("_", ""), true))
				{
					case Miki.Discord.Rest.GatewayEventType.MessageCreate:
					{
						if(OnMessageCreate != null)
						{
							await OnMessageCreate(
                                (body.Data as JToken).ToObject<DiscordMessagePacket>());
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildCreate:
					{
						if(OnGuildCreate != null)
						{
							var guild = (body.Data as JToken).ToObject<DiscordGuildPacket>();

							await OnGuildCreate(
								guild
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.ChannelCreate:
					{
						if(OnGuildCreate != null)
						{
							var discordChannel = (body.Data as JToken).ToObject<DiscordChannelPacket>();

							await OnChannelCreate(discordChannel);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildMemberRemove:
					{
						if(OnGuildMemberRemove != null)
                        {
                            var packet = (body.Data as JToken).ToObject<GuildIdUserArgs>();

							await OnGuildMemberRemove(
								packet.guildId,
								packet.user
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildMemberAdd:
					{
						DiscordGuildMemberPacket guildMember 
                            = (body.Data as JToken).ToObject<DiscordGuildMemberPacket>();

						if(OnGuildMemberAdd != null)
						{
							await OnGuildMemberAdd(guildMember);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildMemberUpdate:
					{
						GuildMemberUpdateEventArgs guildMember =
                            (body.Data as JToken).ToObject<GuildMemberUpdateEventArgs>();

						if(OnGuildMemberUpdate != null)
						{
							await OnGuildMemberUpdate(
								guildMember
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildRoleCreate:
					{
						RoleEventArgs role = (body.Data as JToken).ToObject<RoleEventArgs>();

						if(OnGuildRoleCreate != null)
						{
							await OnGuildRoleCreate(
								role.GuildId,
								role.Role
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildRoleDelete:
					{
						if(OnGuildRoleDelete != null)
						{
							RoleDeleteEventArgs role = (body.Data as JToken)
                                .ToObject<RoleDeleteEventArgs>();

							await OnGuildRoleDelete(
								role.GuildId,
								role.RoleId
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildRoleUpdate:
					{
						RoleEventArgs role = (body.Data as JToken).ToObject<RoleEventArgs>();

						if(OnGuildRoleUpdate != null)
						{
							await OnGuildRoleUpdate(
								role.GuildId,
								role.Role
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.ChannelDelete:
					{
						if(OnChannelDelete != null)
						{
							await OnChannelDelete(
                                (body.Data as JToken).ToObject<DiscordChannelPacket>());
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.ChannelUpdate:
					{
						if(OnChannelUpdate != null)
						{
							await OnChannelUpdate(
                                (body.Data as JToken).ToObject<DiscordChannelPacket>());
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildBanAdd:
					{
						if(OnGuildBanAdd != null)
						{
							var packet = (body.Data as JToken).ToObject<GuildIdUserArgs>();

							await OnGuildBanAdd(
								packet.guildId,
								packet.user
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildBanRemove:
					{
						if(OnGuildBanRemove != null)
						{
							var packet = (body.Data as JToken).ToObject<GuildIdUserArgs>();

							await OnGuildBanRemove(
								packet.guildId,
								packet.user
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildDelete:
					{
						if(OnGuildDelete != null)
						{
							var packet = (body.Data as JToken)
                                .ToObject<DiscordGuildUnavailablePacket>();

							await OnGuildDelete(
								packet
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildEmojisUpdate:
					{
						if(OnGuildEmojiUpdate != null)
                        {
                            var packet = (body.Data as JToken).ToObject<GuildEmojisUpdateEventArgs>();

							await OnGuildEmojiUpdate(
								packet.guildId,
								packet.emojis
							);
						}
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildIntegrationsUpdate:
					{
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildMembersChunk:
					{
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.GuildUpdate:
					{
                        if(OnGuildUpdate != null)
                        {
                            await OnGuildUpdate(
                                (body.Data as JToken).ToObject<DiscordGuildPacket>());
                        }
                    }
					break;

					case Miki.Discord.Rest.GatewayEventType.MessageDelete:
					{
						if(OnMessageDelete != null)
                        {
                            await OnMessageDelete(
                                (body.Data as JToken).ToObject<MessageDeleteArgs>());
                        }
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.MessageDeleteBulk:
					{
						if(OnMessageDeleteBulk != null)
                        {
                            await OnMessageDeleteBulk(
                                (body.Data as JToken).ToObject<MessageBulkDeleteEventArgs>());
                        }
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.MessageUpdate:
					{
						if(OnMessageUpdate != null)
                        {
                            await OnMessageUpdate(
                                (body.Data as JToken).ToObject<DiscordMessagePacket>());
                        }
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.PresenceUpdate:
					{
						if(OnPresenceUpdate != null)
                        {
                            await OnPresenceUpdate(
                                (body.Data as JToken).ToObject<DiscordPresencePacket>());
                        }
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.Ready:
					{
							OnReady.InvokeAsync(
								(body.Data as Newtonsoft.Json.Linq.JToken).ToObject<Miki.Discord.Common.Gateway.Packets.GatewayReadyPacket>()
							).Wait();
					}

					break;

					case Miki.Discord.Rest.GatewayEventType.Resumed:
					{

					}
					break;

					case Miki.Discord.Rest.GatewayEventType.TypingStart:
					{
						if(OnTypingStart != null)
                        {
                            await OnTypingStart(
                                (body.Data as JToken).ToObject<TypingStartEventArgs>());
                        }
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.UserUpdate:
					{
						if(OnUserUpdate != null)
                        {
                            await OnUserUpdate(
                                (body.Data as JToken).ToObject<DiscordPresencePacket>());
                        }
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.VoiceServerUpdate:
					{
					}
					break;

					case Miki.Discord.Rest.GatewayEventType.VoiceStateUpdate:
					{
					}
					break;
				}

				if(!config.ConsumerAutoAck)
				{
					channel.BasicAck(ea.DeliveryTag, false);
				}
			}
			catch(Exception e)
			{
				Log.Error(e);

				if(!config.ConsumerAutoAck)
				{
					channel.BasicNack(ea.DeliveryTag, false, false);
				}
			}
		}

		public System.Threading.Tasks.Task SendAsync(int shardId, GatewayOpcode opcode, object payload)
		{
            CommandMessage msg = new CommandMessage
            {
                Opcode = opcode,
                ShardId = shardId,
                Data = payload
            };

            channel.BasicPublish(
                "gateway-command", "", body: Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(msg)));
			return System.Threading.Tasks.Task.CompletedTask;
		}

        /// <inheritdoc />
        public System.Threading.Tasks.ValueTask SubscribeAsync(string ev)
        {
            var key = config.QueueName + ":" + ev;
            if(consumers.ContainsKey(key))
            {
                throw new InvalidOperationException("Queue already subscribed");
            }

			var consumer = new EventingBasicConsumer(channel);
            consumer.Received += async (ch, ea) => await OnMessageAsync(ch, ea);

			string _ = channel.BasicConsume(
                key, config.ConsumerAutoAck, consumer);
            consumers.TryAdd("", consumer);

			return default;
        }

        /// <inheritdoc />
        public System.Threading.Tasks.ValueTask UnsubscribeAsync(string ev)
        {
			return default;
        }
    }
}