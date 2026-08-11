using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DataIngestionFunctions.RedisConfig
{
    // DataIngestionService had no Redis dependency at all before SessionCloseGapFillFunction —
    // it was a pure stateless webhook-in/Kafka-out transform. This is the minimal set of
    // operations that function needs: read/write the "last known real candle" cache, and check
    // whether a given minute already landed in the per-bucket SET NotificationService maintains.
    //
    // Registered as a singleton (see Program.cs) rather than the transient registration
    // NotificationService/AggregationService use for their own copies of this class — a
    // ConnectionMultiplexer is meant to be created once and reused, and SessionCloseGapFillFunction
    // calls this every minute all day, so a transient registration would open a fresh Redis
    // connection on every tick for no reason.
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

        public async Task<bool> AddToRedis(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return false;
            IDatabase db = _redis.GetDatabase();
            return await db.StringSetAsync(key, value);
        }

        public async Task<bool> AddToRedisWithExpiry(string key, string value, TimeSpan expiry)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return false;
            IDatabase db = _redis.GetDatabase();
            return await db.StringSetAsync(key, value, expiry);
        }

        public async Task<string> GetKeyValueFromRedis(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            IDatabase db = _redis.GetDatabase();
            string? value = await db.StringGetAsync(key);
            return value ?? string.Empty;
        }

        // SISMEMBER — matches NotificationService's AddToCountSetAsync member format
        // (yyyy-MM-ddTHH:mm:ss), so this reads the exact same per-bucket ground truth the
        // dashboard's rail already relies on, rather than inventing a second notion of "arrived".
        public async Task<bool> SetContainsAsync(string key, string member)
        {
            IDatabase db = _redis.GetDatabase();
            return await db.SetContainsAsync(key, member);
        }

        public async Task<long> GetSetLengthAsync(string key)
        {
            IDatabase db = _redis.GetDatabase();
            return await db.SetLengthAsync(key);
        }

        // Same discovery idiom DashboardService's DiscoverTickers/DiscoverProviderTickers use —
        // whatever's actually in Redis today drives what this scans, no hardcoded ticker list.
        public async Task<List<string>> GetKeysAsync(string pattern)
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = new List<string>();
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                keys.Add(key.ToString());
            }
            return keys;
        }
    }
}
