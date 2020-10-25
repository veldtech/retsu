using System;
using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Miki.Discord.Common;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Retsu.Models;
using Retsu.Models.Communication;

namespace Retsu.Publisher.RabbitMQ
{
    public class RabbitMQPublisher : IRetsuPublisher, IDisposable
    {
        public IObservable<CommandMessage> OnCommandReceived => onCommandReceived;
        private readonly Subject<CommandMessage> onCommandReceived = new Subject<CommandMessage>();
        
        private readonly IObservable<CommandMessageSendArgs> messageObservable;
 
        private readonly IModel publishModel;
        private readonly IModel commandModel;
        
        private readonly ConcurrentDictionary<string, object> queueSet = new ConcurrentDictionary<string, object>();
        
        private IDisposable subscription;
        
        public RabbitMQPublisher(IConnection connection, IObservable<CommandMessageSendArgs> messageObservable)
        {
            this.messageObservable = messageObservable;
            
            publishModel = connection.CreateModel();
            publishModel.ExchangeDeclare("gateway", "direct", true);
            
            commandModel = connection.CreateModel();
            var queue = commandModel.QueueDeclare();
            commandModel.ExchangeDeclare("gateway-command", "fanout", true);
            commandModel.QueueBind(queue.QueueName, "gateway-command", "");
            
            var consumer = new AsyncEventingBasicConsumer(commandModel);
            commandModel.BasicConsume(queue.QueueName, false, consumer);
        }
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            subscription = messageObservable.SubscribeTask(PublishPayloadAsync);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            subscription.Dispose();
            return Task.CompletedTask;
        }

        public Task PublishPayloadAsync(CommandMessageSendArgs args)
        {
            if (args == null)
            {
                return default;
            }
            
            if (!queueSet.ContainsKey(args.EventName))
            {
                var queue = publishModel.QueueDeclare();
                publishModel.QueueDeclare(queue, true, false, false);
                publishModel.QueueBind(queue, "gateway", args.EventName);
                queueSet.TryAdd(args.EventName, null);
            }
            
            publishModel.BasicPublish(
                "gateway", 
                args.EventName, 
                body: Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args.Message)));
            return default;
        }

        public void Dispose()
        {
            publishModel.Dispose();
            commandModel.Dispose();
            subscription.Dispose();
            onCommandReceived.Dispose();
        }
    }
}