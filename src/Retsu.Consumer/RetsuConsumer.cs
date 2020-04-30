namespace Retsu.Consumer
{
	using Miki.Discord.Common;
	using Miki.Discord.Common.Events;
	using Miki.Discord.Common.Gateway;
	using Miki.Discord.Common.Packets;
	using Miki.Discord.Common.Packets.Events;
	using Miki.Logging;
	using System;
    using System.Threading.Tasks;
    using Miki.Discord.Common.Extensions;
    using Miki.Discord.Common.Packets.API;
    using Newtonsoft.Json.Linq;
    using Retsu.Models.Communication;
    using Retsu.Consumer.Models;

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
        public Func<GatewayMessage, Task> OnPacketSent { get; set; }
        public Func<GatewayMessage, Task> OnPacketReceived { get; set; }

        private readonly ConsumerConfiguration config;

        private readonly ReactiveMQConsumer consumer;
        private readonly ReactiveMQPublisher publisher;

		public RetsuConsumer(
            ConsumerConfiguration config,
            QueueConfiguration publisherConfig)
        {
            this.config = config;
            consumer = new ReactiveMQConsumer(config);
            publisher = new ReactiveMQPublisher(publisherConfig);
        }

		public async Task RestartAsync()
		{
			await StopAsync();
			await StartAsync();
		}

		public Task StartAsync()
		{
			return Task.CompletedTask;
		}

		public Task StopAsync()
		{
			return Task.CompletedTask;
		}

        private async Task OnMessageAsync(IMQMessage<GatewayMessage> message)
        {
            if(message.Body.OpCode != GatewayOpcode.Dispatch)
            {
                message.Ack();
                Log.Trace("packet from gateway with op '" + message.Body.OpCode + "' received");
                return;
            }

            if(!(message.Body.Data is JToken token))
            {
                message.Ack();
                Log.Trace("Invalid data payload.");
                return;
            }

            try
            {
                Log.Trace("packet with the op-code '" + message.Body.EventName + "' received.");
                switch(Enum.Parse(
                    typeof(GatewayEventType), message.Body.EventName.Replace("_", ""), true))
                {
                    case GatewayEventType.MessageCreate:
                    {
                        await OnMessageCreate.InvokeAsync(
                            token.ToObject<DiscordMessagePacket>());
                        break;
                    }

                    case GatewayEventType.GuildCreate:
                    {
                        var guild = token.ToObject<DiscordGuildPacket>();
                        await OnGuildCreate.InvokeAsync(guild);
                        break;
                    }

                    case GatewayEventType.ChannelCreate:
                    {
                        var discordChannel = token.ToObject<DiscordChannelPacket>();
                        await OnChannelCreate.InvokeAsync(discordChannel);
                        break;
                    }

                    case GatewayEventType.GuildMemberRemove:
                    {
                        var packet = token.ToObject<GuildIdUserArgs>();
                        await OnGuildMemberRemove.InvokeAsync(packet.guildId, packet.user);
                        break;
                    }

                    case GatewayEventType.GuildMemberAdd:
                    {
                        var guildMember = token.ToObject<DiscordGuildMemberPacket>();
                        await OnGuildMemberAdd.InvokeAsync(guildMember);
                        break;
                    }

                    case GatewayEventType.GuildMemberUpdate:
                    {
                        var guildMember = token.ToObject<GuildMemberUpdateEventArgs>();
                        await OnGuildMemberUpdate.InvokeAsync(guildMember);
                        break;
                    }

                    case GatewayEventType.GuildRoleCreate:
                    {
                        var role = token.ToObject<RoleEventArgs>();
                        await OnGuildRoleCreate.InvokeAsync(role.GuildId, role.Role);
                        break;
                    }

                    case GatewayEventType.GuildRoleDelete:
                    {
                        var role = token.ToObject<RoleDeleteEventArgs>();
                        await OnGuildRoleDelete.InvokeAsync(role.GuildId, role.RoleId);
                        break;
                    }

                    case GatewayEventType.GuildRoleUpdate:
                    {
                        var role = token.ToObject<RoleEventArgs>();
                        await OnGuildRoleUpdate.InvokeAsync(role.GuildId, role.Role);
                        break;
                    }

                    case GatewayEventType.ChannelDelete:
                    {
                        await OnChannelDelete.InvokeAsync(token.ToObject<DiscordChannelPacket>());
                        break;
                    }

                    case GatewayEventType.ChannelUpdate:
                    {
                        await OnChannelUpdate.InvokeAsync(token.ToObject<DiscordChannelPacket>());
                        break;
                    }

                    case GatewayEventType.GuildBanAdd:
                    {
                        var packet = token.ToObject<GuildIdUserArgs>();
                        await OnGuildBanAdd.InvokeAsync(packet.guildId, packet.user);
                        break;
                    }

                    case GatewayEventType.GuildBanRemove:
                    {
                        var packet = token.ToObject<GuildIdUserArgs>();
                        await OnGuildBanRemove.InvokeAsync(packet.guildId, packet.user);
                        break;
                    }

                    case GatewayEventType.GuildDelete:
                    {
                        var packet = token.ToObject<DiscordGuildUnavailablePacket>();
                        await OnGuildDelete.InvokeAsync(packet);
                        break;
                    }

                    case GatewayEventType.GuildEmojisUpdate:
                    {
                        var packet = token.ToObject<GuildEmojisUpdateEventArgs>();
                        await OnGuildEmojiUpdate.InvokeAsync(packet.guildId, packet.emojis);
                        break;
                    }

                    case GatewayEventType.GuildUpdate:
                    {
                        await OnGuildUpdate.InvokeAsync(token.ToObject<DiscordGuildPacket>());
                        break;
                    }

                    case GatewayEventType.MessageDelete:
                    {
                        await OnMessageDelete.InvokeAsync(token.ToObject<MessageDeleteArgs>());
                        break;
                    }

                    case GatewayEventType.MessageDeleteBulk:
                    {
                        await OnMessageDeleteBulk.InvokeAsync(
                            token.ToObject<MessageBulkDeleteEventArgs>());
                        break;
                    }

                    case GatewayEventType.MessageUpdate:
                    {
                        if(OnMessageUpdate != null)
                        {
                            //await OnMessageUpdate.Invoke(token.ToObject<DiscordMessagePacket>());
                        }
                        break;
                    }

                    case GatewayEventType.PresenceUpdate:
                    {
                        await OnPresenceUpdate.InvokeAsync(token.ToObject<DiscordPresencePacket>());
                        break;
                    }

                    case GatewayEventType.Ready:
                    {
                        await OnReady.InvokeAsync(token.ToObject<GatewayReadyPacket>());
                        break;
                    }

                    case GatewayEventType.TypingStart:
                    {
                        await OnTypingStart.InvokeAsync(token.ToObject<TypingStartEventArgs>());
                        break;
                    }

                    case GatewayEventType.UserUpdate:
                    {
                        await OnUserUpdate.InvokeAsync(token.ToObject<DiscordPresencePacket>());
                        break;
                    }
                }

                if(!config.ConsumerAutoAck)
                {
                    message.Ack();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
                if(!config.ConsumerAutoAck)
                {
                    message.Nack();
                }
            }
        }

        public async Task SendAsync(int shardId, GatewayOpcode opcode, object payload)
        {
            await publisher.PublishAsync(
                new CommandMessage
                {
                    Opcode = opcode,
                    ShardId = shardId,
                    Data = payload
                });
        }

        /// <inheritdoc />
        public async ValueTask SubscribeAsync(string ev)
        {
            consumer.CreateObservable<GatewayMessage>(ev)
                .Subscribe(async x => await OnMessageAsync(x));
        }
    }
}