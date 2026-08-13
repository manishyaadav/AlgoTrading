using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace StrategyService.RedisConfig
{
    // StrategyService's first Redis dependency — it was a pure CRUD-over-files service until the
    // Rule Engine page needed to read live indicator/session state to evaluate rules against.
    // Mirrors every other service's RedisHelper connection setup in this repo
    // (NotificationService/AggregationService/WarmUpService).
    //
    // Was read-only until the Alerts feature: PositionEntryFunction/PositionExitFunction now write
    // Position:Strategy:{id} hashes and push alert records onto Alert:Feed:{date} — the writer
    // methods below are ported verbatim from AggregationService's own RedisHelper.
    public class RedisHelper
    {
        private readonly ILogger<RedisHelper> _logger;
        private readonly ConnectionMultiplexer _redis;

        public RedisHelper(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<RedisHelper>();

            string? redisConnectionString = configuration["RedisConnectionString"];
            if (string.IsNullOrEmpty(redisConnectionString))
            {
                throw new InvalidOperationException("Redis connection string is missing in configuration.");
            }
            _redis = ConnectionMultiplexer.Connect(redisConnectionString);
        }

        public async Task<string?> GetStringAsync(string key)
        {
            IDatabase db = _redis.GetDatabase();
            RedisValue value = await db.StringGetAsync(key);
            return value.IsNullOrEmpty ? null : (string)value!;
        }

        public async Task<Dictionary<string, string>> GetHashAsync(string key)
        {
            IDatabase db = _redis.GetDatabase();
            var hash = await db.HashGetAllAsync(key);
            return hash.ToDictionary(h => h.Name.ToString(), h => h.Value.ToString());
        }

        // Finds the first key matching `pattern` and returns it along with its Hash — used where
        // the caller knows the instrument but not the exact Timeframe segment of the key (see
        // LiveDataSnapshot.GetPriorCloseAsync). Same discovery idiom DashboardService's
        // DiscoverTickers uses: whatever's actually in Redis drives the lookup, not a guess. The
        // key comes back with the hash because the caller can't otherwise know which one matched,
        // and the Rule Engine page cites it as the value's source.
        public async Task<(string Key, Dictionary<string, string> Hash)?> ScanIndicatorHashWithKeyAsync(string pattern)
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                return (key.ToString(), await GetHashAsync(key.ToString()));
            }
            return null;
        }

        public async Task SetHashAsync(string key, HashEntry[] entries, TimeSpan expiry)
        {
            IDatabase db = _redis.GetDatabase();
            await db.HashSetAsync(key, entries);
            await db.KeyExpireAsync(key, expiry);
        }

        /// <summary>
        /// Pushes `value` onto the right end of the List at `key`, trims it down to the last
        /// `maxLength` entries, and refreshes its TTL — same RPUSH+LTRIM+expire shape
        /// AggregationService's RedisHelper already uses for its own rolling-window state, reused
        /// here for the Alert:Feed:{date} daily list.
        /// </summary>
        public async Task PushToListAsync(string key, string value, int maxLength, TimeSpan expiry)
        {
            IDatabase db = _redis.GetDatabase();
            await db.ListRightPushAsync(key, value);
            await db.ListTrimAsync(key, -maxLength, -1);
            await db.KeyExpireAsync(key, expiry);
        }

        public async Task<RedisValue[]> GetListRangeAsync(string key, long start = 0, long stop = -1)
        {
            IDatabase db = _redis.GetDatabase();
            return await db.ListRangeAsync(key, start, stop);
        }
    }
}
