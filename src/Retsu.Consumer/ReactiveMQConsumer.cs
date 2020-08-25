using System;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Miki.Discord.Gateway.Converters;
using Miki.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Retsu.Consumer
{
    internal class ReactiveMQConsumer
    {
        private IModel channel;
        private IConnection connection;

        private readonly ConnectionFactory connectionFactory;
        private readonly ConcurrentDictionary<string, EventingBasicConsumer> consumers
            = new ConcurrentDictionary<string, EventingBasicConsumer>();

        private readonly ConsumerConfiguration config;
        private readonly JsonSerializerOptions serializerOptions;

        internal ReactiveMQConsumer(ConsumerConfiguration config)
        {
            this.config = config;

            serializerOptions = new JsonSerializerOptions();
            serializerOptions.Converters.Add(new StringToUlongConverter());

            connectionFactory = new ConnectionFactory
            {
                Uri = config.ConnectionString,
                DispatchConsumersAsync = false
            };
        }

        public Task StartAsync()
        {
            connection = connectionFactory.CreateConnection();
            connection.CallbackException += (s, args) =>
            {
                Log.Error(args.Exception);
            };

            channel = connection.CreateModel();
            channel.BasicQos(config.PrefetchSize, config.PrefetchCount, false);
            channel.ExchangeDeclare(config.ExchangeName, ExchangeType.Direct);
            channel.QueueDeclare(config.QueueName, config.QueueDurable, config.QueueExclusive,
                config.QueueAutoDelete, null);
            channel.QueueBind(config.QueueName, config.ExchangeName, config.ExchangeRoutingKey, null);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            channel.Close();
            channel.Dispose();
            channel = null;

            connection.Close();
            connection.Dispose();
            connection = null;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public IObservable<IMQMessage<T>> CreateObservable<T>(string ev)
        {
            if(string.IsNullOrEmpty(ev))
            {
                throw new ArgumentNullException(nameof(ev));
            }

            var key = config.QueueName + ":" + ev;

            EventingBasicConsumer consumer;
            if(consumers.ContainsKey(key))
            {
                if(!consumers.TryGetValue(key, out consumer))
                {
                    throw new InvalidOperationException(
                        "Existing consumer could not be fetched from collection");
                }
            }
            else
            {
                consumer = new EventingBasicConsumer(channel);
                channel.QueueDeclare(key, true, false, false);
                channel.QueueBind(key, config.ExchangeName, ev);
                string _ = channel.BasicConsume(key, config.ConsumerAutoAck, consumer);
                consumers.TryAdd(key, consumer);
            }

            var observable = Observable.FromEventPattern<BasicDeliverEventArgs>(
                    x => consumer.Received += x,
                    x => consumer.Received -= x)
                .Select(x => new MQMessage<T>(channel, x.EventArgs, serializerOptions));
            return observable;
        }
    }

    internal class MQMessage<T> : IMQMessage<T>
    {
        private readonly BasicDeliverEventArgs args;
        private readonly IModel channel;

        public MQMessage(
            IModel channel, BasicDeliverEventArgs args, JsonSerializerOptions serializerOptions = null)
        {
            this.channel = channel;
            this.args = args;

            Body = JsonSerializer.Deserialize<T>(
                Encoding.UTF8.GetString(args.Body.Span), serializerOptions);
        }


        /// <inheritdoc />
        public T Body { get; }

        /// <inheritdoc />
        public void Ack()
        {
            channel.BasicAck(args.DeliveryTag, false);
        }

        /// <inheritdoc />
        public void Nack()
        {
            channel.BasicNack(args.DeliveryTag, false, false);
        }
    }
}
