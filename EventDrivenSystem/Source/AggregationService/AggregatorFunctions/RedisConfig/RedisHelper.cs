using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AggregatorFunctions.RedisConfig
{
    // Thin Redis Hash wrapper — used to persist each ticker/timeframe's in-progress aggregation
    // bucket (see RunningBucket) so a restart or crash mid-bucket doesn't lose the candles already
    // accumulated for it. Mirrors NotificationService/RedisConfig/RedisHelper.cs's connection setup.
    public class RedisHelper
    {
        private readonly ILogger<RedisHelper> _logger;
        private readonly ConnectionMultiplexer _redis;

        public RedisHelper(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<RedisHelper>();

            string? redisConnectionString = configuration["RedisConnectionString"];
            _logger.LogInformation($"redisConnectionString: {redisConnectionString}");
            if (string.IsNullOrEmpty(redisConnectionString))
            {
                throw new InvalidOperationException("Redis connection string is missing in configuration.");
            }
            _redis = ConnectionMultiplexer.Connect(redisConnectionString);
        }

        public async Task<HashEntry[]> GetHashAsync(string key)
        {
            IDatabase db = _redis.GetDatabase();
            return await db.HashGetAllAsync(key);
        }

        public async Task SetHashAsync(string key, HashEntry[] entries, TimeSpan expiry)
        {
            IDatabase db = _redis.GetDatabase();
            await db.HashSetAsync(key, entries);
            await db.KeyExpireAsync(key, expiry);
        }

        public async Task DeleteKeyAsync(string key)
        {
            IDatabase db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }

        /// <summary>
        /// Pushes `value` onto the right end of the List at `key`, trims it down to the last
        /// `maxLength` entries, and refreshes its TTL — used for rolling-window state (e.g. the
        /// trailing N candles for box-plot stats) so a restart doesn't lose the window like a
        /// static in-memory List used to. RPUSH+LTRIM keeps this a fixed-size ring without ever
        /// needing to know or track the window's current length separately.
        /// </summary>
        public async Task PushToListAsync(string key, string value, int maxLength, TimeSpan expiry)
        {
            IDatabase db = _redis.GetDatabase();
            await db.ListRightPushAsync(key, value);
            await db.ListTrimAsync(key, -maxLength, -1);
            await db.KeyExpireAsync(key, expiry);
        }

        public async Task<RedisValue[]> GetListAsync(string key)
        {
            IDatabase db = _redis.GetDatabase();
            return await db.ListRangeAsync(key, 0, -1);
        }
    }
}
