using Miki.Discord.Common.Gateway;
using Miki.Discord.Gateway;
using Miki.Discord.Gateway.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Miki.Logging;
using System.Reactive.Subjects;
using System.Threading;

namespace Retsu.Publisher
{
    /// <summary>
    /// Like <see cref="GatewayCluster"/>, but only for raw connections.
    /// </summary>
    public class GatewayConnectionCluster
    {
        private readonly List<GatewayConnection> connections = new List<GatewayConnection>();
        public IObservable<GatewayMessage> OnPacketReceived => packetReceived;
        private readonly Subject<GatewayMessage> packetReceived;

        private readonly List<IDisposable> packetEventSubscription;

        public GatewayConnectionCluster(GatewayProperties properties, IEnumerable<int> allShardIds)
        {
            packetReceived = new Subject<GatewayMessage>();
            packetEventSubscription = new List<IDisposable>();
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
                    Intents = properties.Intents
                }));
            }
        }

        public async Task StartAsync(CancellationToken token = default)
        {
            foreach(var s in connections)
            {
                Log.Debug("Spawning shard #" + s.ShardId);

                packetEventSubscription.Add(
                    s.OnPacketReceived.Subscribe(packetReceived.OnNext));

                await s.StartAsync(token);
            }
        }

        public async Task StopAsync(CancellationToken token = default)
        {
            foreach(var sub in packetEventSubscription)
            {
                sub.Dispose();
            }

            foreach(var s in connections)
            {
                await s.StopAsync(token);
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
                Log.Debug("Shard not initialized in this cluster, ignoring payload.");
                return;
            }
            await shard.SendCommandAsync(opcode, data);
        }
    }
}
