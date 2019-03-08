using Newtonsoft.Json;

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

        [JsonProperty("discord")]
        public DiscordConfig Discord { get; set; } = new DiscordConfig();

        [JsonProperty("redis_url")]
        public string RedisUrl { get; set; } = "";
    }
}