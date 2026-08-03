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
