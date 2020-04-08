namespace Retsu.Publisher
{
    using Miki.Discord.Common;
    using Miki.Discord.Common.Gateway;
    using Miki.Discord.Gateway;
    using Miki.Discord.Gateway.Connection;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Miki.Logging;

    /// <summary>
    /// Like Miki.Discord.Gateway.GatewayCluster, but only for raw connections.
    /// </summary>
    public class GatewayConnectionCluster
    {
        private readonly List<GatewayConnection> connections = new List<GatewayConnection>();
        public event Func<GatewayMessage, Task> OnPacketReceived;

        public GatewayConnectionCluster(GatewayProperties properties, IEnumerable<int> allShardIds)
        {
            // Spawn connection shards
            foreach (var i in allShardIds)
            {
                connections.Add(new GatewayConnection(new GatewayProperties
                {
                    AllowNonDispatchEvents = properties.AllowNonDispatchEvents,
                    Compressed = properties.Compressed,
                    Encoding = properties.Encoding,
                    Ratelimiter = properties.Ratelimiter,
                    ShardCount = properties.ShardCount,
                    ShardId = i,
                    Token = properties.Token,
                    Version = properties.Version,
                    WebSocketClientFactory = properties.WebSocketClientFactory
                }));
            }
        }

        public async Task StartAsync()
        {
            foreach(var s in connections)
            {
                Log.Trace("Spawning shard #" + s.ShardId);

                s.OnPacketReceived += OnPacketReceived;
                await s.StartAsync();
                
            }
        }

        public async Task StopAsync()
        {
            foreach(var s in connections)
            {
                s.OnPacketReceived -= OnPacketReceived;
                await s.StopAsync();
            }
        }

        public GatewayConnection GetConnection(int shardId)
        {
            return connections.FirstOrDefault(x => x.ShardId == shardId);
        }

        public async ValueTask RestartAsync(int shardId)
        {
            var shard = GetConnection(shardId);
            if(shard == null)
            {
                return;
            }
            await shard.ReconnectAsync();
        }

        public async ValueTask SendAsync(int shardId, GatewayOpcode opcode, object data)
        {
            var shard = GetConnection(shardId);
            if(shard == null)
            {
                return;
            }
            await shard.SendCommandAsync(opcode, data);
        }
    }
}
