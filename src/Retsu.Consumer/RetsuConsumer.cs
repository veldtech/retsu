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
	using System.Diagnostics;
	using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
	using Miki.Discord.Common.Packets.API;
    using Retsu.Models.Communication;

    public class RetsuConsumer : IGateway
	{
		public Func<DiscordChannelPacket, Task> OnChannelCreate { get; set; }
		public Func<DiscordChannelPacket, Task> OnChannelUpdate { get; set; }
		public Func<DiscordChannelPacket, Task> OnChannelDelete { get; set; }
		public Func<DiscordGuildPacket, Task> OnGuildCreate { get; set; }
		public Func<DiscordGuildPacket, Task> OnGuildUpdate { get; set; }
		public Func<DiscordGuildUnavailablePacket, Task> OnGuildDelete { get; set; }
		public Func<DiscordGuildMemberPacket, Task> OnGuildMemberAdd { get; set; }
		public Func<ulong, DiscordUserPacket, Task> OnGuildMemberRemove { get; set; }
		public Func<GuildMemberUpdateEventArgs, Task> OnGuildMemberUpdate { get; set; }
		public Func<ulong, DiscordUserPacket, Task> OnGuildBanAdd { get; set; }
		public Func<ulong, DiscordUserPacket, Task> OnGuildBanRemove { get; set; }
		public Func<ulong, DiscordEmoji[], Task> OnGuildEmojiUpdate { get; set; }
		public Func<ulong, DiscordRolePacket, Task> OnGuildRoleCreate { get; set; }
		public Func<ulong, DiscordRolePacket, Task> OnGuildRoleUpdate { get; set; }
		public Func<ulong, ulong, Task> OnGuildRoleDelete { get; set; }
		public Func<DiscordMessagePacket, Task> OnMessageCreate { get; set; }
		public Func<DiscordMessagePacket, Task> OnMessageUpdate { get; set; }
		public Func<MessageDeleteArgs, Task> OnMessageDelete { get; set; }
		public Func<MessageBulkDeleteEventArgs, Task> OnMessageDeleteBulk { get; set; }
		public Func<DiscordPresencePacket, Task> OnPresenceUpdate { get; set; }
		public Func<GatewayReadyPacket, Task> OnReady { get; set; }
		public Func<TypingStartEventArgs, Task> OnTypingStart { get; set; }
		public Func<DiscordPresencePacket, Task> OnUserUpdate { get; set; }
        public event Func<GatewayMessage, Task> OnPacketSent;
        public event Func<GatewayMessage, Task> OnPacketReceived;

        private readonly IModel channel;

        private EventingBasicConsumer consumer;

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
			commandChannel.ExchangeDeclare(config.QueueName + "-command", ExchangeType.Fanout, true);
			commandChannel.QueueDeclare(config.QueueName + "-command", false, false, false);
			commandChannel.QueueBind(config.QueueName + "-command", config.QueueName + "-command", config.ExchangeRoutingKey, null);
		}

		public async Task RestartAsync()
		{
			await StopAsync();
			await StartAsync();
		}

		public Task StartAsync()
		{
			consumer = new EventingBasicConsumer(channel);
			consumer.Received += async (ch, ea) => await OnMessageAsync(ch, ea);

			string consumerTag = channel.BasicConsume(config.QueueName, config.ConsumerAutoAck, consumer);
			return Task.CompletedTask;
		}

		public Task StopAsync()
		{
			consumer = null;
			return Task.CompletedTask;
		}

		private async Task OnMessageAsync(object ch, BasicDeliverEventArgs ea)
		{
			var payload = Encoding.UTF8.GetString(ea.Body);
			var sw = Stopwatch.StartNew();
			var body = JsonSerializer.Deserialize<GatewayMessage>(payload);
			if(body.OpCode != GatewayOpcode.Dispatch)
			{
				channel.BasicAck(ea.DeliveryTag, false);
				Log.Trace("packet from gateway with op '" + body.OpCode + "' received");
				return;
			}

			try
			{
				Log.Trace("packet with the op-code '" + body.EventName + "' received.");
				switch(Enum.Parse(typeof(GatewayEventType), body.EventName.Replace("_", ""), true))
				{
					case GatewayEventType.MessageCreate:
					{
						if(OnMessageCreate != null)
						{
							await OnMessageCreate(
								JsonSerializer.Deserialize<DiscordMessagePacket>(
                                    ((JsonElement)body.Data).GetRawText()));
						}
					}
					break;

					case GatewayEventType.GuildCreate:
					{
						if(OnGuildCreate != null)
						{
							var guild = JsonSerializer.Deserialize<DiscordGuildPacket>(
                                ((JsonElement)body.Data).GetRawText());

							await OnGuildCreate(
								guild
							);
						}
					}
					break;

					case GatewayEventType.ChannelCreate:
					{
						if(OnGuildCreate != null)
						{
							var discordChannel = JsonSerializer.Deserialize<DiscordChannelPacket>(
                                ((JsonElement)body.Data).GetRawText());

							await OnChannelCreate(discordChannel);
						}
					}
					break;

					case GatewayEventType.GuildMemberRemove:
					{
						if(OnGuildMemberRemove != null)
                        {
                            var packet = JsonSerializer.Deserialize<GuildIdUserArgs>(
                                ((JsonElement) body.Data).GetRawText());

							await OnGuildMemberRemove(
								packet.guildId,
								packet.user
							);
						}
					}
					break;

					case GatewayEventType.GuildMemberAdd:
					{
						DiscordGuildMemberPacket guildMember 
                            = JsonSerializer.Deserialize<DiscordGuildMemberPacket>(
                                ((JsonElement)body.Data).GetRawText());

						if(OnGuildMemberAdd != null)
						{
							await OnGuildMemberAdd(guildMember);
						}
					}
					break;

					case GatewayEventType.GuildMemberUpdate:
					{
						GuildMemberUpdateEventArgs guildMember = 
                            JsonSerializer.Deserialize<GuildMemberUpdateEventArgs>(
                                ((JsonElement)body.Data).GetRawText());

						if(OnGuildMemberUpdate != null)
						{
							await OnGuildMemberUpdate(
								guildMember
							);
						}
					}
					break;

					case GatewayEventType.GuildRoleCreate:
					{
						RoleEventArgs role = JsonSerializer.Deserialize<RoleEventArgs>(
                            ((JsonElement)body.Data).GetRawText());

						if(OnGuildRoleCreate != null)
						{
							await OnGuildRoleCreate(
								role.GuildId,
								role.Role
							);
						}
					}
					break;

					case GatewayEventType.GuildRoleDelete:
					{
						if(OnGuildRoleDelete != null)
						{
							RoleDeleteEventArgs role = JsonSerializer.Deserialize<RoleDeleteEventArgs>(
                                ((JsonElement)body.Data).GetRawText());

							await OnGuildRoleDelete(
								role.GuildId,
								role.RoleId
							);
						}
					}
					break;

					case GatewayEventType.GuildRoleUpdate:
					{
						RoleEventArgs role = JsonSerializer.Deserialize<RoleEventArgs>(
                            ((JsonElement)body.Data).GetRawText());

						if(OnGuildRoleUpdate != null)
						{
							await OnGuildRoleUpdate(
								role.GuildId,
								role.Role
							);
						}
					}
					break;

					case GatewayEventType.ChannelDelete:
					{
						if(OnChannelDelete != null)
						{
							await OnChannelDelete(
                                    JsonSerializer.Deserialize<DiscordChannelPacket>(
                                        ((JsonElement)body.Data).GetRawText()));
						}
					}
					break;

					case GatewayEventType.ChannelUpdate:
					{
						if(OnChannelUpdate != null)
						{
							await OnChannelUpdate(
                                    JsonSerializer.Deserialize<DiscordChannelPacket>(
                                        ((JsonElement)body.Data).GetRawText()));
						}
					}
					break;

					case GatewayEventType.GuildBanAdd:
					{
						if(OnGuildBanAdd != null)
						{
							var packet = JsonSerializer.Deserialize<GuildIdUserArgs>(
                                ((JsonElement)body.Data).GetRawText());

							await OnGuildBanAdd(
								packet.guildId,
								packet.user
							);
						}
					}
					break;

					case GatewayEventType.GuildBanRemove:
					{
						if(OnGuildBanRemove != null)
						{
							var packet = JsonSerializer.Deserialize<GuildIdUserArgs>(
                                ((JsonElement)body.Data).GetRawText());

							await OnGuildBanRemove(
								packet.guildId,
								packet.user
							);
						}
					}
					break;

					case GatewayEventType.GuildDelete:
					{
						if(OnGuildDelete != null)
						{
							var packet = JsonSerializer.Deserialize<DiscordGuildUnavailablePacket>(
                                ((JsonElement)body.Data).GetRawText());

							await OnGuildDelete(
								packet
							);
						}
					}
					break;

					case GatewayEventType.GuildEmojisUpdate:
					{
						if(OnGuildEmojiUpdate != null)
						{
							var packet = JsonSerializer.Deserialize<GuildEmojisUpdateEventArgs>(
                                ((JsonElement)body.Data).GetRawText());

							await OnGuildEmojiUpdate(
								packet.guildId,
								packet.emojis
							);
						}
					}
					break;

					case GatewayEventType.GuildIntegrationsUpdate:
					{
					}
					break;

					case GatewayEventType.GuildMembersChunk:
					{
					}
					break;

					case GatewayEventType.GuildUpdate:
					{
                        if(OnGuildUpdate != null)
                        {
                            await OnGuildUpdate(
                                JsonSerializer.Deserialize<DiscordGuildPacket>(
                                    ((JsonElement) body.Data).GetRawText()));
                        }
                    }
					break;

					case GatewayEventType.MessageDelete:
					{
						if(OnMessageDelete != null)
                        {
                            await OnMessageDelete(
                                JsonSerializer.Deserialize<MessageDeleteArgs>(
                                    ((JsonElement) body.Data).GetRawText()));
                        }
					}
					break;

					case GatewayEventType.MessageDeleteBulk:
					{
						if(OnMessageDeleteBulk != null)
                        {
                            await OnMessageDeleteBulk(
                                JsonSerializer.Deserialize<MessageBulkDeleteEventArgs>(
                                    ((JsonElement) body.Data).GetRawText()));
                        }
					}
					break;

					case GatewayEventType.MessageUpdate:
					{
						if(OnMessageUpdate != null)
                        {
                            await OnMessageUpdate(
                                JsonSerializer.Deserialize<DiscordMessagePacket>(
                                    ((JsonElement) body.Data).GetRawText()));
                        }
					}
					break;

					case GatewayEventType.PresenceUpdate:
					{
						if(OnPresenceUpdate != null)
                        {
                            await OnPresenceUpdate(
                                JsonSerializer.Deserialize<DiscordPresencePacket>(
                                    ((JsonElement) body.Data).GetRawText()));
                        }
					}
					break;

					case GatewayEventType.Ready:
                    {
                        OnReady?.Invoke(
                                JsonSerializer.Deserialize<GatewayReadyPacket>(
                                    ((JsonElement) body.Data).GetRawText()))
                            .Wait();
                    }
					break;

					case GatewayEventType.Resumed:
					{

					}
					break;

					case GatewayEventType.TypingStart:
					{
						if(OnTypingStart != null)
                        {
                            await OnTypingStart(
                                JsonSerializer.Deserialize<TypingStartEventArgs>(
                                    ((JsonElement) body.Data).GetRawText()));
                        }
					}
					break;

					case GatewayEventType.UserUpdate:
					{
						if(OnUserUpdate != null)
                        {
                            await OnUserUpdate(
                                JsonSerializer.Deserialize<DiscordPresencePacket>(
                                    ((JsonElement) body.Data).GetRawText()));
                        }
					}
					break;

					case GatewayEventType.VoiceServerUpdate:
					{
					}
					break;

					case GatewayEventType.VoiceStateUpdate:
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
			Log.Debug($"{body.EventName}: {sw.ElapsedMilliseconds}ms");
		}

		public Task SendAsync(int shardId, GatewayOpcode opcode, object payload)
		{
            CommandMessage msg = new CommandMessage
            {
                Opcode = opcode,
                ShardId = shardId,
                Data = payload
            };

            channel.BasicPublish(
                "gateway-command", "", body: Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg)));
			return Task.CompletedTask;
		}
	}
}