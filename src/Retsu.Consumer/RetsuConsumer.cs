using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Miki.Discord.Common;
using Miki.Discord.Common.Gateway;
using Miki.Discord.Gateway;
using Miki.Discord.Rest.Converters;
using Miki.Logging;
using Retsu.Consumer.Models;
using Retsu.Models.Communication;

namespace Retsu.Consumer
{
    public class RetsuConsumer : IGateway
	{
        private readonly GatewayEventHandler eventHandler;
        public IGatewayEvents Events => eventHandler;

        public IObservable<GatewayMessage> PacketReceived => packageReceivedSubject;
        private readonly Subject<GatewayMessage> packageReceivedSubject;

        private readonly ConsumerConfiguration config;

        private readonly ReactiveMQConsumer consumer;
        private readonly ReactiveMQPublisher publisher;

        private readonly HashSet<string> subscribedTopics = new HashSet<string>();
        private readonly List<IDisposable> activeTopics = new List<IDisposable>();

        private bool isActive = false;

		public RetsuConsumer(
            ConsumerConfiguration config,
            QueueConfiguration publisherConfig)
        {
            this.config = config;
            consumer = new ReactiveMQConsumer(config);
            publisher = new ReactiveMQPublisher(publisherConfig);

            var jsonSerializer = new JsonSerializerOptions();
            jsonSerializer.Converters.Add(new UserAvatarConverter());
            jsonSerializer.Converters.Add(new StringToUlongConverter());
            jsonSerializer.Converters.Add(new StringToShortConverter());
            jsonSerializer.Converters.Add(new StringToEnumConverter<GuildPermission>());

            packageReceivedSubject = new Subject<GatewayMessage>();
            eventHandler = new GatewayEventHandler(packageReceivedSubject, jsonSerializer);
        }

		public async Task RestartAsync()
		{
			await StopAsync(default);
			await StartAsync(default);
		}

		public async Task StartAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await consumer.StartAsync();
            await publisher.StartAsync();
            isActive = true;

            foreach(var ev in subscribedTopics)
            {
                await SubscribeAsync(ev);
            }

        }

		public async Task StopAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await consumer.StopAsync();
            await publisher.StartAsync();
            isActive = false;

            foreach(var topic in activeTopics)
            {
                topic.Dispose();
            }
        }

        private async Task OnMessageAsync(IMQMessage<GatewayMessage> message)
        {
            if(message.Body.OpCode != GatewayOpcode.Dispatch)
            {
                message.Ack();
                Log.Trace("packet from gateway with op '" + message.Body.OpCode + "' received");
                return;
            }

            try
            {
                Log.Trace("packet with the op-code '" + message.Body.EventName + "' received.");
                packageReceivedSubject.OnNext(message.Body);

                if (!config.ConsumerAutoAck)
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

        public ValueTask SubscribeAsync(string ev)
        {
            if(isActive)
            {
                var activeTopic = consumer.CreateObservable<GatewayMessage>(ev)
                    .Subscribe(async x => await OnMessageAsync(x));
                activeTopics.Add(activeTopic);
            }

            if(!subscribedTopics.Contains(ev))
            {
                subscribedTopics.Add(ev);
            }

            return default;
        }
    }
}