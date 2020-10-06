using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Miki.Cache.StackExchange;
using Miki.Discord.Common;
using Miki.Discord.Common.Gateway;
using Miki.Discord.Gateway;
using Miki.Discord.Gateway.Connection;
using Miki.Discord.Gateway.Ratelimiting;
using Miki.Discord.Rest.Converters;
using Miki.Logging;
using Miki.Serialization.Protobuf;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Retsu.Models.Communication;
using Retsu.Publisher.Models;
using Sentry;
using StackExchange.Redis;

namespace Retsu.Publisher
{
    internal class Program
    {
        private static GatewayConnectionCluster cluster;
        private static ApplicationConfig config;
        private static IModel pusherModel;
        private static JsonSerializerOptions options;

        private static ConcurrentDictionary<string, object> queueSet =
            new ConcurrentDictionary<string, object>();

        private static void Main()
            => MainAsync().GetAwaiter().GetResult();

        private static async Task MainAsync()
        {
            new LogBuilder()
                .AddLogEvent((msg, lvl) => { if(lvl >= config.LogLevel) Console.WriteLine(msg); })
                .AddExceptionEvent((ex, lvl) => SentrySdk.CaptureException(ex))
                .Apply();

            await LoadConfigAsync();

            Log.Message("Config loaded.");

            using (SentrySdk.Init(config.SentryUrl))
            {
                Log.Message("Error handler setup.");

                var redis = await ConnectionMultiplexer.ConnectAsync(config.RedisUrl);
                var cache = new StackExchangeCacheClient(new ProtobufSerializer(), redis);

                Log.Message("Cache connected");
                List<int> allShardIds = new List<int>();
                for (var i = config.Discord.ShardIndex; 
                    i < config.Discord.ShardIndex + config.Discord.ShardAmount; 
                    i++)
                {
                    allShardIds.Add(i);
                }
                
                options = new JsonSerializerOptions
                {
                    Converters =
                    {
                        new StringToUlongConverter(),
                        new StringToShortConverter(),
                        new StringToEnumConverter<GuildPermission>(),
                        new UserAvatarConverter()
                    }
                };

                cluster = new GatewayConnectionCluster(new GatewayProperties
                {
                    Encoding = GatewayEncoding.Json,
                    Ratelimiter = new CacheBasedRatelimiter(cache),
                    SerializerOptions = options,
                    ShardCount = config.Discord.ShardCount,
                    ShardId = 0,
                    Token = config.Discord.Token,
                    Version = GatewayConstants.DefaultVersion,
                    AllowNonDispatchEvents = false,
                    Compressed = false,
                    Intents = config.Discord.Intents,
                }, allShardIds);

                var conn = new ConnectionFactory
                {
                    Uri = new Uri(config.MessageQueue.Url),
                };

                using var connection = conn.CreateConnection();
                using var model = pusherModel = connection.CreateModel();
                using var commandModel = connection.CreateModel();

                pusherModel.ExchangeDeclare("gateway", "direct", true);
                cluster.OnPacketReceived.SubscribeTask(OnPacketReceivedAsync);

                commandModel.ExchangeDeclare("gateway-command", "fanout", true);

                var queue = commandModel.QueueDeclare();
                commandModel.QueueBind(queue.QueueName, "gateway-command", "");

                var consumer = new AsyncEventingBasicConsumer(commandModel);
                consumer.Received += OnCommandReceivedAsync;
                commandModel.BasicConsume(queue.QueueName, false, consumer);
                Log.Message("Set up RabbitMQ");

                await cluster.StartAsync();
                Log.Message("Discord gateway running");
                
                await Task.Delay(-1);
            }
        }

        private static Task OnPacketReceivedAsync(GatewayMessage arg)
        {
            if(arg.EventName == "READY")
            {
                var ready = JsonSerializer.Deserialize<GatewayReadyPacket>(
                    ((JsonElement)arg.Data).GetRawText(), options);
                Log.Message($"Shard {ready.CurrentShard} is connected");
            }

            if(config.IgnorePackets.Contains(arg.EventName))
            {
                return Task.CompletedTask;
            }

            if(!queueSet.ContainsKey(arg.EventName))
            {
                var queue = GetQueueNameFromEventName(arg.EventName);
                pusherModel.QueueDeclare(queue, true, false, false);
                pusherModel.QueueBind(queue, "gateway", arg.EventName);
                queueSet.TryAdd(arg.EventName, null);
            }

            try
            {
                pusherModel.BasicPublish(
                    "gateway", arg.EventName, body: Encoding.UTF8.GetBytes(JsonSerializer.Serialize(arg)));
            }
            catch(AlreadyClosedException)
            {
                Log.Warning($"Event '{arg.EventName}' missed due to AMQP client closed.");
            }

            return Task.CompletedTask;
        }

        private static string GetQueueNameFromEventName(string eventName)
        {
            return "gateway:" + eventName;
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
