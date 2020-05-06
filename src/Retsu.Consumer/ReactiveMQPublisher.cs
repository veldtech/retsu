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
        private IConnection connection;
        private IModel channel;

        private readonly QueueConfiguration config;
        private readonly ConnectionFactory connectionFactory;

        internal ReactiveMQPublisher(QueueConfiguration config)
        {
            this.config = config;
            connectionFactory = new ConnectionFactory
            {
                Uri = config.ConnectionString
            };
        }

        public Task StartAsync()
        {
            connection = connectionFactory.CreateConnection();
            channel = connection.CreateModel();
            channel.ExchangeDeclare(
                config.QueueName, ExchangeType.Fanout, true);
            channel.QueueDeclare(
                config.QueueName, false, false, false);
            channel.QueueBind(
                config.QueueName, config.QueueName, config.ExchangeRoutingKey, null);
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
