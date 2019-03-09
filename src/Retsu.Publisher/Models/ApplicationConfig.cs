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

        public class MQConfig
        {
            [JsonProperty("hostname")]
            public string HostName { get; set; } = "localhost";
        }

        [JsonProperty("discord")]
        public DiscordConfig Discord { get; set; } = new DiscordConfig();

        [JsonProperty("msg_queue")]
        public MQConfig MessageQueue { get; set; } = new MQConfig();

        [JsonProperty("redis_url")]
        public string RedisUrl { get; set; } = "";
    }
}