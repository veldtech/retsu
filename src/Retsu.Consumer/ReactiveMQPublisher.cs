namespace Retsu.Consumer
{
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using RabbitMQ.Client;
    using Retsu.Consumer.Models;
    using Retsu.Models.Communication;

    internal class ReactiveMQPublisher
    {
        private readonly IModel channel;
        private readonly QueueConfiguration config;

        internal ReactiveMQPublisher(QueueConfiguration config)
        {
            this.config = config;
            var connectionFactory = new ConnectionFactory
            {
                Uri = config.ConnectionString
            };
            channel = connectionFactory.CreateConnection().CreateModel();
            channel.ExchangeDeclare(
                config.QueueName, ExchangeType.Fanout, true);
            channel.QueueDeclare(
                config.QueueName, false, false, false);
            channel.QueueBind(
                config.QueueName, config.QueueName, config.ExchangeRoutingKey, null);
        }

        internal ValueTask PublishAsync(CommandMessage payload)
        {
            channel.BasicPublish(
                "gateway-command", 
                config.ExchangeRoutingKey,
                body: Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
            return default;
        }
    }
}
