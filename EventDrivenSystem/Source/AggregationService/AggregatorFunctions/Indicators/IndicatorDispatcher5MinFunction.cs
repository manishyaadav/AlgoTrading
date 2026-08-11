using System.Text.Json;
using AggregatorFunctions.RedisConfig;
using AggregatorFunctions.SharedLibrary.Events.Aggregation.TimeFrame;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AggregatorFunctions.Indicators
{
    // Thin per-timeframe wrapper — consumes the completed-bar output topic this timeframe's own
    // Aggregation5MinutesFunction already publishes to (not the raw 1-min topic), and hands each
    // completed bar to the shared IndicatorDispatcher. Own consumer group, so this doesn't interfere
    // with NotificationService's DataAggregationNotificationFunctions, which already reads the same
    // topic to update its own Redis cache.
    public class IndicatorDispatcher5MinFunction
    {
        private readonly IndicatorDispatcher _dispatcher;

        public IndicatorDispatcher5MinFunction(ILogger<IndicatorDispatcher5MinFunction> logger, IProducer<string, string> producer, RedisHelper redisHelper)
        {
            _dispatcher = new IndicatorDispatcher(logger, producer, redisHelper);
        }

        [Function("IndicatorDispatcher5MinFunction")]
        public async Task Run(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-aggregation-ohlc-5min-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "live-indicator-5min-dispatcher")] string eventDataJson,
            FunctionContext context)
        {
            var logger = context.GetLogger("IndicatorDispatcher5MinFunction");
            try
            {
                using JsonDocument document = JsonDocument.Parse(eventDataJson);
                string eventDataValue = document.RootElement.TryGetProperty("Value", out var valueElement) ? valueElement.GetString() ?? string.Empty : string.Empty;

                var bar = JsonSerializer.Deserialize<TimeFrameAggregationEvent>(eventDataValue);
                if (bar == null) return;

                await _dispatcher.DispatchAsync(bar, 5);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Indicator dispatch error (5min)");
            }
        }
    }
}
