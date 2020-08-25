namespace Retsu.Publisher.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Miki.Discord.Gateway;
    using Miki.Logging;

    public class ApplicationConfig
    {
        public class DiscordConfig
        {
            [JsonPropertyName("token")]
            public string Token { get; set; } = "";

            [JsonPropertyName("shard_count")]
            public int ShardCount { get; set; }

            [JsonPropertyName("shard_start_index")]
            public int ShardIndex { get; set; }

            [JsonPropertyName("shard_num")]
            public int ShardAmount { get; set; }

            [JsonPropertyName("intents")]
            public GatewayIntents Intents { get; set; }

            [JsonPropertyName("use_large_bot_sharding")]
            public bool LargeBotSharding { get; set; }
        }

        public class MQConfig
        {
            [JsonPropertyName("url")]
            public string Url { get; set; } = "amqp://localhost";
        }

        [JsonPropertyName("discord")]
        public DiscordConfig Discord { get; set; } = new DiscordConfig();

        [JsonPropertyName("ignore_packets")]
        public IEnumerable<string> IgnorePackets { get; set; } = new List<string>();

        [JsonPropertyName("loglevel")]
        public LogLevel LogLevel { get; set; } = LogLevel.Information;

        [JsonPropertyName("msg_queue")]
        public MQConfig MessageQueue { get; set; } = new MQConfig();

        [JsonPropertyName("redis_url")]
        public string RedisUrl { get; set; } = "";

        [JsonPropertyName("sentry_url")]
        public string SentryUrl { get; set; }
    }
}