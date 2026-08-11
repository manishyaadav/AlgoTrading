using System.Text.Json;
using AggregatorFunctions.RedisConfig;
using AggregatorFunctions.SharedLibrary.Events.Aggregation.TimeFrame;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AggregatorFunctions.Indicators
{
    // Thin per-timeframe wrapper — consumes the completed-bar output topic this timeframe's own
    // Aggregation15MinutesFunction already publishes to (not the raw 1-min topic), and hands each
    // completed bar to the shared IndicatorDispatcher. Own consumer group, so this doesn't interfere
    // with NotificationService's DataAggregationNotificationFunctions, which already reads the same
    // topic to update its own Redis cache.
    public class IndicatorDispatcher15MinFunction
    {
        private readonly IndicatorDispatcher _dispatcher;

        public IndicatorDispatcher15MinFunction(ILogger<IndicatorDispatcher15MinFunction> logger, IProducer<string, string> producer, RedisHelper redisHelper)
        {
            _dispatcher = new IndicatorDispatcher(logger, producer, redisHelper);
        }

        [Function("IndicatorDispatcher15MinFunction")]
        public async Task Run(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-aggregation-ohlc-15min-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "live-indicator-15min-dispatcher")] string eventDataJson,
            FunctionContext context)
        {
            var logger = context.GetLogger("IndicatorDispatcher15MinFunction");
            try
            {
                using JsonDocument document = JsonDocument.Parse(eventDataJson);
                string eventDataValue = document.RootElement.TryGetProperty("Value", out var valueElement) ? valueElement.GetString() ?? string.Empty : string.Empty;

                var bar = JsonSerializer.Deserialize<TimeFrameAggregationEvent>(eventDataValue);
                if (bar == null) return;

                await _dispatcher.DispatchAsync(bar, 15);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Indicator dispatch error (15min)");
            }
        }
    }
}
