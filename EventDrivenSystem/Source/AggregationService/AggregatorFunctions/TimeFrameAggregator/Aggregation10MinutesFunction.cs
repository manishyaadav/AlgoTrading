using System.Text.Json;
using AggregatorFunctions.Common;
using AggregatorFunctions.RedisConfig;
using AggregatorFunctions.SharedLibrary.Events.Aggregation.TimeFrame;
using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Events;
using SharedLibrary.Events.DataIngestion;
using SharedLibrary.Helpers;

namespace AggregatorFunctions.TimeFrameAggregator
{
    public class Aggregation10MinutesFunction
    {
        private readonly ILogger<Aggregation10MinutesFunction> _logger;
        private static int _timeframe = 10;
        private static int _base = 5;
        private static int _candlesNeeded = _timeframe / _base; // 2 source (5-min) candles per bucket
        private readonly IProducer<string, string> _producer;
        private readonly RedisHelper _redisHelper;
        private static string _producerTopicName = $"live-aggregation-ohlc-{_timeframe}min-topic";

        public Aggregation10MinutesFunction(ILogger<Aggregation10MinutesFunction> logger,
                    IConfiguration configuration,
                    IProducer<string, string> producer,
                    RedisHelper redisHelper)
        {
            _logger = logger;
            _producer = producer; // Inject the producer
            _redisHelper = redisHelper;
        }

        [Function("StreamAggregator10MFunction")]
        public async Task Run(
               [KafkaTrigger("%KAFKA_BROKER_URL%",
                  "live-aggregation-ohlc-5min-topic",
                  AuthenticationMode = BrokerAuthenticationMode.Plain,
                  ConsumerGroup = "live-aggregator-10min-consumer")] string eventDataJson, FunctionContext context)
        {
            var logger = context.GetLogger("KafkaFunction");
            logger.LogInformation($"Kafka Trigged on topic : live-aggregation-ohlc-5min-topic at: {DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")}");
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
                logger.LogError(ex, "Error processing Kafka message");
            }
        }

        // See Aggregation5MinutesFunction.cs for the full design rationale (restart-safety via Redis,
        // count-based completion, session-open-anchored bucket alignment). Same pattern here, except
        // the source candles are already-aggregated 5-min events (2 of them make a 10-min candle),
        // not raw 1-min ticks.
        private async Task ProcessKafkaMessage(string eventData, ILogger logger)
        {
            TimeFrameAggregationEvent? eventValue;

            try
            {
                eventValue = JsonSerializer.Deserialize<TimeFrameAggregationEvent>(eventData);

                if (eventValue == null)
                {
                    return;
                }

                logger.LogInformation($"Event Details Ticker: {eventValue.Ticker}, Timeframe: {eventValue.Timeframe}, Time: {eventValue.WindowsStartTime}");

                string bucketKey = $"Aggregation:Bucket:{eventValue.Ticker}:{_timeframe}min";
                var existingHash = await _redisHelper.GetHashAsync(bucketKey);
                var bucket = RunningBucket.FromHash(existingHash);

                DateTime alignedBucketStart = RunningBucket.FloorToBucketStart(eventValue.WindowsStartTime, _timeframe);
                bool isFirstCandleInBucket = bucket == null || bucket.BucketStart != alignedBucketStart;

                if (isFirstCandleInBucket)
                {
                    bucket = new RunningBucket
                    {
                        Open = eventValue.Open,
                        High = eventValue.High,
                        Low = eventValue.Low,
                        Close = eventValue.Close,
                        VolumeSum = eventValue.Volume,
                        Count = 1,
                        BucketStart = alignedBucketStart,
                        BucketEnd = alignedBucketStart.AddMinutes(_timeframe)
                    };
                }
                else
                {
                    bucket!.High = Math.Max(bucket.High, eventValue.High);
                    bucket.Low = Math.Min(bucket.Low, eventValue.Low);
                    bucket.Close = eventValue.Close;
                    bucket.VolumeSum += eventValue.Volume;
                    bucket.Count += 1;
                }

                logger.LogInformation($"Running bucket {eventValue.Ticker}:{_timeframe}min — Count: {bucket.Count}/{_candlesNeeded}, O:{bucket.Open} H:{bucket.High} L:{bucket.Low} C:{bucket.Close} V:{bucket.VolumeSum}, Start:{bucket.BucketStart}");

                if (bucket.Count >= _candlesNeeded)
                {
                    var aggregatedData = BuildAggregationEvent(eventValue.Ticker, bucket);
                    var ohlcvJson = JsonSerializer.Serialize(aggregatedData);
                    string key = $"{aggregatedData.Ticker}:{_timeframe}Min";

                    logger.LogInformation($"Aggregated Data: {ohlcvJson}");
                    await ProduceToKafka(_producerTopicName, ohlcvJson, key, logger);

                    await _redisHelper.DeleteKeyAsync(bucketKey);
                }
                else
                {
                    await _redisHelper.SetHashAsync(bucketKey, bucket.ToHash(), TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Aggregation Error: " + ex.Message);
            }
        }

        private TimeFrameAggregationEvent BuildAggregationEvent(string ticker, RunningBucket bucket)
        {
            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

            return new TimeFrameAggregationEvent()
            {
                Ticker = ticker,
                Timeframe = _timeframe,
                WindowsStartTime = bucket.BucketStart,
                Open = bucket.Open,
                High = bucket.High,
                Low = bucket.Low,
                Close = bucket.Close,
                Volume = bucket.VolumeSum,
                Producer = "aggregator.service",
                ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset)
            };
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
    }
}
