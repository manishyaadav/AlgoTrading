using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace StrategyService.RedisConfig
{
    // StrategyService's first Redis dependency — it was a pure CRUD-over-files service until the
    // Rule Engine page needed to read live indicator/session state to evaluate rules against.
    // Read-only: this service still never writes to Redis, it only reads what WarmUpService and
    // AggregationService already maintain. Mirrors every other service's RedisHelper connection
    // setup in this repo (NotificationService/AggregationService/WarmUpService).
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

        // Finds the first key matching `pattern` and returns its Hash — used where the caller
        // knows the instrument but not the exact Timeframe segment of the key (see
        // LiveDataSnapshot.GetPriorCloseAsync). Same discovery idiom DashboardService's
        // DiscoverTickers uses: whatever's actually in Redis drives the lookup, not a guess.
        public async Task<Dictionary<string, string>?> ScanIndicatorHashAsync(string pattern)
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                return await GetHashAsync(key.ToString());
            }
            return null;
        }
    }
}
