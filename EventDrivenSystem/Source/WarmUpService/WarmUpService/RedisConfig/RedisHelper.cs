using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace WarmUpService.RedisConfig
{
    // Mirrors NotificationService/AggregationService's RedisHelper connection setup. WarmUpService's
    // job is mostly *reading* what AggregationService's indicator calculators have (or haven't) put
    // in Redis, plus writing seeds when the cold-start fallback runs — so this stays intentionally
    // small; it grows as the seeding path (ohlc-live integration) gets built.
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
    }
}
