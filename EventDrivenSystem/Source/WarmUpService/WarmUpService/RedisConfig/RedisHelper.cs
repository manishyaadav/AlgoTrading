using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace WarmUpService.RedisConfig
{
    // Mirrors NotificationService/AggregationService's RedisHelper connection setup. Started
    // read-only (status-check first cut); now also writes — the cold-start seeding path (plan
    // section 2b step 3) needs to persist indicator state and the rolling True-Range window, using
    // the same Hash/List primitives AggregationService's RedisHelper already established for
    // RunningBucket and the 20-period window, so a value written here reads back identically however
    // it's later updated live.
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

        public async Task<bool> KeyExistsAsync(string key)
        {
            IDatabase db = _redis.GetDatabase();
            return await db.KeyExistsAsync(key);
        }

        public async Task<string?> GetStringAsync(string key)
        {
            IDatabase db = _redis.GetDatabase();
            RedisValue value = await db.StringGetAsync(key);
            return value.IsNullOrEmpty ? null : (string)value!;
        }

        public async Task SetStringAsync(string key, string value, TimeSpan expiry)
        {
            IDatabase db = _redis.GetDatabase();
            await db.StringSetAsync(key, value, expiry);
        }

        public async Task SetHashAsync(string key, HashEntry[] entries, TimeSpan expiry)
        {
            IDatabase db = _redis.GetDatabase();
            await db.HashSetAsync(key, entries);
            await db.KeyExpireAsync(key, expiry);
        }

        // RPUSH + LTRIM(-maxLength,-1) + refresh TTL — identical to AggregationService's
        // PushToListAsync (used there for the 20-period candle-stats window); reused verbatim here
        // for Supertrend's rolling True-Range window rather than inventing a second list-persistence
        // pattern for the same job.
        public async Task PushToListAsync(string key, string value, int maxLength, TimeSpan expiry)
        {
            IDatabase db = _redis.GetDatabase();
            await db.ListRightPushAsync(key, value);
            await db.ListTrimAsync(key, -maxLength, -1);
            await db.KeyExpireAsync(key, expiry);
        }

        public async Task DeleteKeyAsync(string key)
        {
            IDatabase db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
    }
}
