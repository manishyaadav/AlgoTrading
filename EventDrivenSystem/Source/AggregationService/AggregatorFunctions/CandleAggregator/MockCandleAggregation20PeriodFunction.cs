using AggregatorFunctions.Common;
using AggregatorFunctions.RedisConfig;
using AggregatorFunctions.SharedLibrary;
using AggregatorFunctions.SharedLibrary.Enums.Candle;
using AggregatorFunctions.SharedLibrary.Events.Aggregation.Candle;
using AggregatorFunctions.SharedLibrary.Events.Aggregation.TimeFrame;
using AggregatorFunctions.TimeFrameAggregator.MockTimeframeAggregator;
using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Events.DataIngestion;
using SharedLibrary.Helpers;
using System.Text.Json;

namespace AggregatorFunctions.CandleAggregator
{
    // Despite the "Mock" name, this is wired into the live pipeline (consumes
    // live-dataingestion-ohlc-topic, produces live-candle-stats-1min-20period-topic) — a rolling
    // box-plot/candle-classification stats calculator over the trailing 20 1-min candles.
    public class MockCandleAggregation20PeriodFunction
    {
        private readonly ILogger<MockCandleAggregation20PeriodFunction> _logger;
        private static int _timeframe = 1;
        private static int _preiod = 20;
        private readonly IProducer<string, string> _producer;
        private readonly RedisHelper _redisHelper;
        private static string _producerTopicName = $"live-candle-stats-{_timeframe}min-{_preiod}period-topic";

        public MockCandleAggregation20PeriodFunction(
                                ILogger<MockCandleAggregation20PeriodFunction> logger,
                                IConfiguration configuration,
                                 IProducer<string, string> producer,
                                 RedisHelper redisHelper)
        {
            _logger = logger;
            _producer = producer; // Inject the producer
            _redisHelper = redisHelper;
        }

        [Function("MockCandleAggregator20PeriodFunction")]
        public async Task Run(
               [KafkaTrigger("%KAFKA_BROKER_URL%",
                  "live-dataingestion-ohlc-topic",
                  AuthenticationMode = BrokerAuthenticationMode.Plain,
                  ConsumerGroup = "live-candle-1min-20period-aggregator")] string eventDataJson, FunctionContext context)
        {
            var logger = context.GetLogger("KafkaFunction");
            logger.LogInformation($"Kafka Trigged on topic : live-dataingestion-ohlc-topic at: {DateTimeHelper.ToIsoStringWithTime(DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow))}");
            var eventDataValue = string.Empty;

            using JsonDocument document = JsonDocument.Parse(eventDataJson);
            JsonElement root = document.RootElement;

            // Access the "Value" property
            if (document.RootElement.TryGetProperty("Value", out JsonElement valueElement))
            {
                eventDataValue = valueElement.GetString() ?? string.Empty;
            }

            try
            {
                await ProcessKafkaMessage(eventDataValue, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing Mock Kafka message");
            }
        }

        // Was: append to a static in-memory List<TimeFrameAggregationEvent> capped at 20 by manually
        // RemoveAt(0)-ing the oldest after each computation — lost on every restart, same failure
        // mode as the fixed-size buckets elsewhere in this service. Now: the trailing-20 window is
        // persisted to a Redis List (RPUSH + LTRIM to the last 20, via RedisHelper.PushToListAsync)
        // so a restart just re-reads whatever candles already made it into the window instead of
        // starting from empty. Unlike the fixed buckets, this window doesn't reset on completion —
        // it's a true rolling window, so once it reaches 20 it recomputes stats on every subsequent
        // candle, same as the original in-memory version did.
        private async Task ProcessKafkaMessage(string eventData, ILogger logger)
        {
            TimeFrameAggregationEvent? eventValue;

            try
            {
                eventValue = JsonSerializer.Deserialize<TimeFrameAggregationEvent>(eventData);

                if (eventValue != null)
                {
                    logger.LogInformation($"Mock Event Details Ticker: {eventValue.Ticker}, Timeframe: {eventValue.Timeframe}, Time: {eventValue.WindowsStartTime}");

                    if (eventValue != null)
                    {
                        string windowKey = $"Aggregation:Window:{eventValue.Ticker}:{_timeframe}min:{_preiod}period";
                        await _redisHelper.PushToListAsync(windowKey, JsonSerializer.Serialize(eventValue), _preiod, TimeSpan.FromHours(24));

                        var windowValues = await _redisHelper.GetListAsync(windowKey);
                        var data = windowValues
                            .Select(v => JsonSerializer.Deserialize<TimeFrameAggregationEvent>(v!))
                            .Where(e => e != null)
                            .Select(e => e!)
                            .ToList();

                        logger.LogInformation($"Rolling window Count for key: {eventValue.Ticker}:{eventValue.Timeframe} is {data.Count}/{_preiod}");

                        if (data.Count == _preiod)
                        {
                            // High - Low - Total Candle Size
                            var boxPlotCandleSize = BoxPlot.GetBoxPlot(data.Select(x => x.High - x.Low).ToList());
                            var boxPlotBodySize = BoxPlot.GetBoxPlot(data.Select(x => Math.Abs(x.Open - x.Close)).ToList());

                            var candleSizeCategory = CandleCategories.GetCandleCategories(boxPlotCandleSize, _timeframe, CandlePartEnum.Size, 3, eventValue.WindowsStartTime);

                            decimal size = eventValue.High - eventValue.Low;
                            decimal body = Math.Abs(eventValue.Open - eventValue.Close);

                            var candleBodyCategory = CandleCategories.GetCandleCategories(boxPlotBodySize, _timeframe, CandlePartEnum.Body, 3, eventValue.WindowsStartTime);
                            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

                            var candleAggregationEvent = new CandleAggregationEvent()
                            {
                                Ticker = eventValue.Ticker,
                                WindowsStartTime = eventValue.WindowsStartTime,
                                Timeframe = eventValue.Timeframe,
                                Open = eventValue.Open,
                                Close = eventValue.Close,
                                High = eventValue.High,
                                Low = eventValue.Low,
                                Volume = eventValue.Volume,
                                Color = eventValue.Open > eventValue.Close ? "Red" : "Green",

                                Size = size,
                                IsSizeOutlier = size > boxPlotCandleSize.OutlierHighBoundary,
                                IsSizeRelevant = size > boxPlotCandleSize.ThirdQuartile,
                                SizeCategory = candleSizeCategory.Where(x => size >= x.Start && size <= x.End).FirstOrDefault().Classification.ToString(),

                                Body = body,
                                IsBodyOutlier = body > boxPlotBodySize.OutlierHighBoundary,
                                IsBodyRelevant = body > boxPlotBodySize.ThirdQuartile,
                                BodyCategory = candleBodyCategory.Where(x => body >= x.Start && body <= x.End).FirstOrDefault().Classification.ToString(),

                                BodyPerc = Math.Round(body / size * 100, 0),
                                IsStrong = eventValue.Open > eventValue.Close ? Math.Abs(eventValue.Close - eventValue.Low) <= ((eventValue.High - eventValue.Low) * 0.1m) : Math.Abs(eventValue.Close - eventValue.High) <= ((eventValue.High - eventValue.Low) * 0.1m),

                                Producer = "mock.candle.aggregator.service",
                                ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset)
                            };

                            var candleEventJson = JsonSerializer.Serialize(candleAggregationEvent);

                            string key = $"{candleAggregationEvent.Ticker}:{_preiod}:{_timeframe}Min:Candle";
                            // and push that to another topic
                            // Send the aggregated data to the Kafka topic
                            logger.LogInformation($"Candle Aggregated Data: {candleEventJson}");
                            await ProduceToKafka(_producerTopicName, candleEventJson, key, logger);
                            logger.LogInformation($"Candle Aggregated Data: {candleEventJson} Sent");

                            // No manual eviction needed — PushToListAsync's LTRIM already keeps the
                            // Redis List capped at the last 20 entries on every push.
                        }
                    }
                }



            }
            catch (Exception ex)
            {
                logger.LogError("Aggregation Error: " + ex.Message);
            }
        }

        private async Task ProduceToKafka(string topicName, string message, string key, ILogger logger)
        {
            try
            {
                var kafkaTask = await _producer.ProduceAsync(topicName, new Message<string, string>
                {
                    Key = key,
                    Value = message
                });

                _logger.LogInformation($"Kafka message PRODUCED SUCCESSFULLY to partition {kafkaTask.Partition} at offset {kafkaTask.Offset}");
            }
            catch (ProduceException<string, string> e)
            {
                logger.LogError($"Delivery failed: {e.Error.Reason}");
            }
        }

        private TimeFrameAggregationEvent? CalculateMinAggregation(int timeframe, List<DataIngestionMinDataEvent> data)
        {
            var first = data.FirstOrDefault();
            var last = data.LastOrDefault();

            if (first != null && last != null)
            {
                var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

                return new TimeFrameAggregationEvent()
                {
                    Ticker = first?.Ticker ?? string.Empty,
                    Timeframe = timeframe,
                    WindowsStartTime = first?.WindowsStartTime ?? DateTime.MinValue,
                    Open = first?.Open ?? 0,
                    High = data.Max(x => x.High),
                    Low = data.Min(x => x.Low),
                    Close = last?.Close ?? 0,
                    Volume = data.Sum(x => x.Volume),
                    Producer = "live.aggregator.service",
                    ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset)
                };
            }
            else
                return null;
        }
    }
}
