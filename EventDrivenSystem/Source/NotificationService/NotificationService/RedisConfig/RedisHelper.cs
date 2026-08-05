using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NotificationService.RedisConfig
{
    public class RedisHelper
    {
        private readonly ILogger<RedisHelper> _logger;
        private readonly IConfiguration _configuration;
        private readonly ConnectionMultiplexer _redis;

        public RedisHelper(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<RedisHelper>();
            _configuration = configuration;

            string? redisConnectionString = _configuration["RedisConnectionString"];
            _logger.LogInformation($"redisConnectionString: {redisConnectionString}");
            if (string.IsNullOrEmpty(redisConnectionString))
            {
                throw new InvalidOperationException("Redis connection string is missing in configuration.");
            }
            _redis = ConnectionMultiplexer.Connect(redisConnectionString);
        }

        public async Task<bool> AddToRedis(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
            {
                _logger.LogInformation($"key or value is null or blank Key {key}, Value: {value}");
                return false;
            }

            // Add key-value pair to Redis
            IDatabase db = _redis.GetDatabase();
            bool result = await db.StringSetAsync(key, value);

            if (result)
            {
                _logger.LogInformation($"{DateTime.Now} - Added/Updated to REDIS KEY: {key}, Value: {value}");
                return true;
            }
            else
            {
                _logger.LogError($"{DateTime.Now} - Error Adding/Updating to REDIS KEY: {key}, Value: {value}");
            }

            return true;
        }

        /// <summary>
        /// Adds `member` to the Redis SET at `key` (SADD — automatically de-duplicates, so the same
        /// candle re-arriving twice doesn't inflate the count) and refreshes the key's TTL. Returns
        /// the set's new cardinality, i.e. the current count. Used for "how many candles landed
        /// today" tracking — the single-value cache keys elsewhere only hold the latest snapshot,
        /// not a running count, so this is a separate structure.
        /// </summary>
        public async Task<long> AddToCountSetAsync(string key, string member, TimeSpan expiry)
        {
            IDatabase db = _redis.GetDatabase();
            await db.SetAddAsync(key, member);
            await db.KeyExpireAsync(key, expiry);
            return await db.SetLengthAsync(key);
        }

        public async Task<string> GetKeyValueFromRedis(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                // Log error or handle validation failure
                return string.Empty;
            }

            // Get value from Redis
            IDatabase db = _redis.GetDatabase();
            string? value = await db.StringGetAsync(key);

            _logger.LogInformation($"{DateTime.Now} - Retrieved from REDIS KEY: {key}, Value: {value}");

            return value ?? string.Empty;
        }
    }
}
