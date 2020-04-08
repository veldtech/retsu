namespace Retsu.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Miki.Cache.StackExchange;
    using Miki.Discord.Common.Gateway;
    using Miki.Discord.Gateway;
    using Miki.Discord.Gateway.Connection;
    using Miki.Discord.Gateway.Ratelimiting;
    using Miki.Logging;
    using Miki.Serialization.Protobuf;
    using RabbitMQ.Client;
    using RabbitMQ.Client.Events;
    using Retsu.Models.Communication;
    using Sentry;
    using Sharder.App;
    using StackExchange.Redis;

    internal class Program
    {
        private static GatewayConnectionCluster cluster;
        private static ApplicationConfig config;
        private static IModel pusherModel;

        private static void Main()
            => MainAsync().GetAwaiter().GetResult();

        private static async Task MainAsync()
        {
            new LogBuilder()
                .AddLogEvent((msg, lvl) => { if(lvl >= config.LogLevel) Console.WriteLine(msg); })
                .AddExceptionEvent((ex, lvl) => SentrySdk.CaptureException(ex))
                .Apply();

            await LoadConfigAsync();

            using (SentrySdk.Init(config.SentryUrl))
            {
                var redis = await ConnectionMultiplexer.ConnectAsync(config.RedisUrl);
                var cache = new StackExchangeCacheClient(new ProtobufSerializer(), redis);

                List<int> allShardIds = new List<int>();
                for (var i = config.Discord.ShardIndex; i < config.Discord.ShardIndex + config.Discord.ShardAmount; i++)
                {
                    allShardIds.Add(i);
                }

                cluster = new GatewayConnectionCluster(new GatewayProperties
                {
                    Compressed = true,
                    Encoding = GatewayEncoding.Json,
                    Ratelimiter = new CacheBasedRatelimiter(cache),
                    ShardCount = config.Discord.ShardCount,
                    ShardId = 0,
                    Token = config.Discord.Token,
                    Version = GatewayConstants.DefaultVersion,
                    AllowNonDispatchEvents = false
                }, allShardIds);

                ConnectionFactory conn = new ConnectionFactory
                {
                    Uri = new Uri(config.MessageQueue.Url),
                    DispatchConsumersAsync = true
                };

                using var connection = conn.CreateConnection();
                using var model = pusherModel = connection.CreateModel();
                using var commandModel = connection.CreateModel();

                pusherModel.ExchangeDeclare("gateway", "direct", true);
                pusherModel.QueueDeclare("gateway", true, false, false);
                pusherModel.QueueBind("gateway", "gateway", "");
                cluster.OnPacketReceived += OnPacketReceivedAsync;

                commandModel.ExchangeDeclare("gateway-command", "fanout", true);

                var queue = commandModel.QueueDeclare();
                commandModel.QueueBind(queue.QueueName, "gateway-command", "");

                var consumer = new AsyncEventingBasicConsumer(commandModel);
                consumer.Received += OnCommandReceivedAsync;
                commandModel.BasicConsume(queue.QueueName, false, consumer);

                await cluster.StartAsync();
                await Task.Delay(-1);
            }
        }

        private static Task OnPacketReceivedAsync(GatewayMessage arg)
        {
            if(config.IgnorePackets.Contains(arg.EventName))
            {
                return Task.CompletedTask;
            }
            pusherModel.BasicPublish(
                "gateway", "", body: Encoding.UTF8.GetBytes(JsonSerializer.Serialize(arg)));
            return Task.CompletedTask;
        }

        private static async Task OnCommandReceivedAsync(object sender, BasicDeliverEventArgs e)
        {
            var json = Encoding.UTF8.GetString(e.Body);
            CommandMessage msg = JsonSerializer.Deserialize<CommandMessage>(json);
            if (msg.Type == null)
            {
                await cluster.SendAsync(msg.ShardId, msg.Opcode, msg.Data);
            }
            else
            {
                switch (msg.Type.ToLowerInvariant())
                {
                    case "reconnect":
                    {
                        await cluster.RestartAsync(msg.ShardId);
                    }
                    break;
                }
            }
        }

        private static async Task LoadConfigAsync()
        {
            const string filePath = "./config.json";

            if (!File.Exists(filePath))
            {
                await using var x = File.CreateText(filePath);
                await x.WriteAsync(
                    JsonSerializer.Serialize(new ApplicationConfig()));
                await x.FlushAsync();
                config = new ApplicationConfig();
            }
            else
            {
                string json = await File.ReadAllTextAsync(filePath);
                config = JsonSerializer.Deserialize<ApplicationConfig>(json);
            }
        }   
    }
}
