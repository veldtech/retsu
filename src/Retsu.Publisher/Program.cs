using Miki.Cache.StackExchange;
using Miki.Discord.Common;
using Miki.Discord.Common.Gateway.Packets;
using Miki.Discord.Gateway;
using Miki.Discord.Gateway.Connection;
using Miki.Discord.Gateway.Ratelimiting;
using Miki.Discord.Rest;
using Miki.Logging;
using Miki.Serialization.Protobuf;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sharder.App
{
    class Program
    {
        static GatewayCluster _cluster;
        static ApplicationConfig _config;
        static IModel _pusherModel;

        static void Main(string[] args)
            => MainAsync(args).GetAwaiter().GetResult();
        static async Task MainAsync(string[] args)
        {
            new LogBuilder()
                .AddLogEvent((msg, lvl) => { if (lvl >= _config.LogLevel) Console.WriteLine(msg); })
                .Apply();

            await LoadConfigAsync();

            var cache = new StackExchangeCacheClient(
                new ProtobufSerializer(), 
                await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(_config.RedisUrl));

            IApiClient api = new DiscordApiClient(_config.Discord.Token, cache);

            List<int> allShardIds = new List<int>();
            for (int i = _config.Discord.ShardIndex; i < _config.Discord.ShardIndex + _config.Discord.ShardCount; i++)
            {
                allShardIds.Add(i);
            }

            _cluster = new GatewayCluster(new GatewayProperties
            {
                Compressed = true,
                Encoding = GatewayEncoding.Json,
                Ratelimiter = new CacheBasedRatelimiter(cache),
                ShardCount = _config.Discord.ShardCount,
                ShardId = 0,
                Token = _config.Discord.Token,
                Version = GatewayConstants.DefaultVersion,
                AllowNonDispatchEvents = false
            }, allShardIds);

            ConnectionFactory conn = new ConnectionFactory();
            conn.Uri = new Uri(_config.MessageQueue.Url);
            conn.DispatchConsumersAsync = true;

            using (var connection = conn.CreateConnection())
            using (_pusherModel = connection.CreateModel())
            using (var commandModel = connection.CreateModel())
            {
                _pusherModel.ExchangeDeclare("gateway", "direct", true);
                _pusherModel.QueueDeclare("gateway", true, false, false);
                _pusherModel.QueueBind("gateway", "gateway", "");
                _cluster.OnPacketReceived += OnPacketReceivedAsync;

                commandModel.ExchangeDeclare("gateway-command", "fanout", true);

                var queue = commandModel.QueueDeclare();
                commandModel.QueueBind(queue.QueueName, "gateway-command", "");

                var consumer = new AsyncEventingBasicConsumer(commandModel);
                consumer.Received += OnCommandReceivedAsync;
                commandModel.BasicConsume(queue.QueueName, false, consumer);

                await _cluster.StartAsync();
                await Task.Delay(-1);
            }
        }

        private static Task OnPacketReceivedAsync(GatewayMessage arg, ArraySegment<byte> packet)
        {
            if(_config.IgnorePackets.Contains(arg.EventName))
            {
                return Task.CompletedTask;
            }

            Log.Message(arg.EventName);

            _pusherModel.BasicPublish("gateway", "", mandatory: true, body: packet.ToArray());
            return Task.CompletedTask;
        }

        private static async Task OnCommandReceivedAsync(object sender, BasicDeliverEventArgs e)
        {
            var json = Encoding.UTF8.GetString(e.Body);
            CommandMessage msg = JsonConvert.DeserializeObject<CommandMessage>(json);
            if (msg.Type == null)
            {
                await _cluster.SendAsync(msg.ShardId, msg.Opcode, msg.Data);
            }
            else
            {
                switch (msg.Type.ToLowerInvariant())
                {
                    case "reconnect":
                    {
                        if (_cluster.Shards.TryGetValue(msg.ShardId, out var shard))
                        {
                            await shard.RestartAsync();
                        }
                    }
                    break;
                }
            }
        }

        static async Task LoadConfigAsync()
        {
            string filePath = "./config.json";

            if (!File.Exists(filePath))
            {
                using (var x = File.CreateText(filePath))
                {
                    await x.WriteAsync(JsonConvert.SerializeObject(
                        new ApplicationConfig(), 
                        Formatting.Indented));
                    await x.FlushAsync();
                    _config = new ApplicationConfig();
                }
            }
            else
            {
                string json = await File.ReadAllTextAsync(filePath);
                _config = JsonConvert.DeserializeObject<ApplicationConfig>(json);
            }
        }   
    }
}
