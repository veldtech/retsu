﻿using Miki.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Sharder.App
{
    public class ApplicationConfig
    {
        public class DiscordConfig
        {
            [JsonProperty("token")]
            public string Token { get; set; } = "";

            [JsonProperty("shard_count")]
            public int ShardCount { get; set; }

            [JsonProperty("shard_start_index")]
            public int ShardIndex { get; set; }

            [JsonProperty("shard_num")]
            public int ShardAmount { get; set; }
        }

        public class MQConfig
        {
            [JsonProperty("url")]
            public string Url { get; set; } = "amqp://localhost";
        }

        [JsonProperty("discord")]
        public DiscordConfig Discord { get; set; } = new DiscordConfig();

        [JsonProperty("ignore_packets")]
        public IEnumerable<string> IgnorePackets { get; set; } = new List<string>();

        [JsonProperty("loglevel")]
        public LogLevel LogLevel { get; set; } = LogLevel.Information;

        [JsonProperty("msg_queue")]
        public MQConfig MessageQueue { get; set; } = new MQConfig();

        [JsonProperty("redis_url")]
        public string RedisUrl { get; set; } = "";

        [JsonProperty("sentry_url")]
        public string SentryUrl { get; set; }
    }
}