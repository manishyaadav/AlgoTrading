using System.Globalization;
using System.Text.Json;
using AggregatorFunctions.RedisConfig;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SharedLibrary.Events.DataIngestion;
using StackExchange.Redis;

namespace AggregatorFunctions.Candles
{
    // Caches "the latest completed 1-min candle per ticker" as a plain, directly-queryable Redis
    // Hash — nothing else in this pipeline persists this today (the 5/10/15/30/60/75-min aggregators
    // only keep a transient in-progress bucket, deleted once it completes). Needed by StrategyService's
    // Alerts feature: an ExitRule comparing "Candle Low/High (1 Minute)" against the current
    // Supertrend value needs the most recently completed 1-min bar's own High/Low, not a 5-min one.
    //
    // Own consumer group on the same live-dataingestion-ohlc-topic the 5/10/15/30/60/75-min
    // aggregators already independently consume — Kafka fans out to each group separately, same
    // established pattern.
    public class LatestMinuteCandleFunction
    {
        private readonly ILogger<LatestMinuteCandleFunction> _logger;
        private readonly RedisHelper _redisHelper;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

        public LatestMinuteCandleFunction(ILogger<LatestMinuteCandleFunction> logger, RedisHelper redisHelper)
        {
            _logger = logger;
            _redisHelper = redisHelper;
        }

        [Function("LatestMinuteCandleFunction")]
        public async Task Run(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-dataingestion-ohlc-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "live-dataingestion-1min-cache-writer")] string eventDataJson, FunctionContext context)
        {
            var eventDataValue = string.Empty;

            using JsonDocument document = JsonDocument.Parse(eventDataJson);
            if (document.RootElement.TryGetProperty("Value", out JsonElement valueElement))
            {
                eventDataValue = valueElement.GetString() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(eventDataValue))
            {
                _logger.LogWarning("LatestMinuteCandleFunction: received an empty/unparseable Kafka message, skipping.");
                return;
            }

            DataIngestionMinDataEvent? candle;
            try
            {
                candle = JsonSerializer.Deserialize<DataIngestionMinDataEvent>(eventDataValue);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "LatestMinuteCandleFunction: failed to deserialize candle event: {Raw}", eventDataValue);
                return;
            }

            if (candle == null || string.IsNullOrEmpty(candle.Ticker))
            {
                _logger.LogWarning("LatestMinuteCandleFunction: deserialized candle had no ticker, skipping.");
                return;
            }

            string key = $"Candle:Last1Min:{candle.Ticker}";
            await _redisHelper.SetHashAsync(key, new HashEntry[]
            {
                new("Open", candle.Open.ToString(CultureInfo.InvariantCulture)),
                new("High", candle.High.ToString(CultureInfo.InvariantCulture)),
                new("Low", candle.Low.ToString(CultureInfo.InvariantCulture)),
                new("Close", candle.Close.ToString(CultureInfo.InvariantCulture)),
                new("Volume", candle.Volume.ToString(CultureInfo.InvariantCulture)),
                new("WindowsStartTime", candle.WindowsStartTime.ToString("yyyy-MM-ddTHH:mm:ss")),
            }, CacheTtl);
        }
    }
}
