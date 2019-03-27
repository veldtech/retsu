using Miki.Cache;
using Miki.Discord.Gateway.Ratelimiting;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retsu.Publisher
{
    class BetterRatelimiter : IGatewayRatelimiter
    {
        const string CacheKey = "miki:gateway:identify:ratelimit";

        IConnectionMultiplexer _cache;
        IDatabaseAsync _db;

        public BetterRatelimiter(IConnectionMultiplexer cache)
        {
            _cache = cache;
            _db = cache.GetDatabase(0);
        }

        public async Task<bool> CanIdentifyAsync()
        {
            var ticks = await _db.StringIncrementAsync(CacheKey, 1);
            if (ticks != 1)
            {
                return false;
            }
            await _db.KeyExpireAsync(CacheKey, TimeSpan.FromSeconds(5));
            return true;
        }
    }
}
