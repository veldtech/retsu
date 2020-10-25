using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
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
using Retsu.Models.Communication;
using Retsu.Publisher.Models;
using Retsu.Publisher.RabbitMQ;
using Sentry;
using StackExchange.Redis;

namespace Retsu.Publisher
{
    internal static class Program
    {
        private static GatewayConnectionCluster cluster;
        private static ApplicationConfig config;
        private static JsonSerializerOptions options;

        private static async Task Main()
        {
            new LogBuilder().AddLogEvent((msg, lvl) =>
                {
                    if (lvl >= config.LogLevel)
                    {
                        Console.WriteLine(msg);
                    }
                })
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
                var allShardIds = new List<int>();
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
                var publisher = new RabbitMQPublisher(
                    connection, cluster.OnPacketReceived.Select(ToSendArgs));
                publisher.OnCommandReceived.SubscribeTask(OnCommandReceivedAsync);
                await publisher.StartAsync(default);
                Log.Message("Set up RabbitMQ");

                await cluster.StartAsync();
                Log.Message("Discord gateway running");
                
                await Task.Delay(-1);
            }
        }

        private static string GetEventName(string eventName)
        {
            return "gateway:" + eventName;
        }

        private static CommandMessageSendArgs ToSendArgs(GatewayMessage message)
        {
            if(message.EventName == "READY")
            {
                var ready = JsonSerializer.Deserialize<GatewayReadyPacket>(
                    ((JsonElement)message.Data).GetRawText(), options);
                Log.Message($"Shard {ready.CurrentShard} is connected");
            }

            if (!message.OpCode.HasValue)
            {
                return null;
            }
            
            return new CommandMessageSendArgs
            {
                EventName = GetEventName(message.EventName),
                Message = new CommandMessage
                {
                    Opcode = message.OpCode.Value,
                    Data = message.Data
                }
            };
        }
        
        private static async Task OnCommandReceivedAsync(CommandMessage msg)
        {
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
                var json = await File.ReadAllTextAsync(filePath);
                config = JsonSerializer.Deserialize<ApplicationConfig>(json);
            }
        }   
    }
}
