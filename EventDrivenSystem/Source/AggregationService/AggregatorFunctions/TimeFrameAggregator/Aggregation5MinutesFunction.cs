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
    public class Aggregation5MinutesFunction
    {
        private readonly ILogger<Aggregation5MinutesFunction> _logger;
        private static int _timeframe = 5;
        private readonly IProducer<string, string> _producer;
        private readonly RedisHelper _redisHelper;
        private static string _producerTopicName = $"live-aggregation-ohlc-{_timeframe}min-topic";

        public Aggregation5MinutesFunction(ILogger<Aggregation5MinutesFunction> logger,
                    IConfiguration configuration,
                    IProducer<string, string> producer,
                    RedisHelper redisHelper)
        {
            _logger = logger;
            _producer = producer; // Inject the producer
            _redisHelper = redisHelper;
        }

        [Function("StreamAggregator5MFunction")]
        public async Task Run(
               [KafkaTrigger("%KAFKA_BROKER_URL%",
                  "live-dataingestion-ohlc-topic",
                  AuthenticationMode = BrokerAuthenticationMode.Plain,
                  ConsumerGroup = "live-dataingestion-5min-aggregator")] string eventDataJson, FunctionContext context)
        {
            var logger = context.GetLogger("KafkaFunction");
            logger.LogInformation($"Kafka Trigged on topic : live-dataingestion-ohlc-topic at: {DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")}");
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

        // Was: append eventValue to a static in-memory List<DataIngestionMinDataEvent>, then on the
        // next wall-clock bucket boundary crossing, re-scan the whole list for candles falling in the
        // just-closed window and fold them into an OHLCV. Both the list and the wall-clock bucket
        // tracker (_bufferData/_bucket) lived only in process memory — an aggregation-live restart
        // mid-window silently dropped whatever had been buffered so far, and since the Kafka consumer
        // group had already committed those offsets, that data was gone for good, not just delayed.
        //
        // Now: the running OHLCV aggregate for the ticker's in-progress bucket is persisted to a
        // Redis Hash after every candle (RunningBucket, via RedisHelper), keyed off how many source
        // candles have landed so far rather than wall-clock time. A restart just re-reads the hash
        // and resumes exactly where it left off instead of losing the partial bucket.
        private async Task ProcessKafkaMessage(string eventData, ILogger logger)
        {
            DataIngestionMinDataEvent? eventValue;

            try
            {
                eventValue = JsonSerializer.Deserialize<DataIngestionMinDataEvent>(eventData);

                if (eventValue == null)
                {
                    return;
                }

                logger.LogInformation($"Event Details Ticker: {eventValue.Ticker}, Timeframe: {eventValue.Timeframe}, Time: {eventValue.WindowsStartTime}");

                string bucketKey = $"Aggregation:Bucket:{eventValue.Ticker}:{_timeframe}min";
                var existingHash = await _redisHelper.GetHashAsync(bucketKey);
                var bucket = RunningBucket.FromHash(existingHash);

                // Buckets are aligned to the timeframe's standard clock marks (:00/:05/:10/... for a
                // 5-min bucket), derived from the candle's own event time — not wall-clock processing
                // time (the old bug), and not just "whatever the first arriving candle's timestamp
                // happens to be" (what this file did until now, which only looked aligned because the
                // pipeline has run continuously since market open; a restart landing on an off-mark
                // candle, e.g. 11:47, would have started a 11:47-11:51 bucket instead of resuming
                // 11:45-11:49). Comparing against the floored value also subsumes the old "different
                // day" check — a new day trivially floors to a different bucket start too.
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

                logger.LogInformation($"Running bucket {eventValue.Ticker}:{_timeframe}min — Count: {bucket.Count}/{_timeframe}, O:{bucket.Open} H:{bucket.High} L:{bucket.Low} C:{bucket.Close} V:{bucket.VolumeSum}, Start:{bucket.BucketStart}");

                if (bucket.Count >= _timeframe)
                {
                    var aggregatedData = BuildAggregationEvent(eventValue.Ticker, bucket);
                    var ohlcvJson = JsonSerializer.Serialize(aggregatedData);
                    string key = $"{aggregatedData.Ticker}:{_timeframe}Min";

                    logger.LogInformation($"Aggregated Data: {ohlcvJson}");
                    await ProduceToKafka(_producerTopicName, ohlcvJson, key, logger);

                    // Bucket complete — clear the persisted state so the next candle starts a fresh one.
                    await _redisHelper.DeleteKeyAsync(bucketKey);
                }
                else
                {
                    // 24h TTL is just a safety net against an abandoned key for a ticker that stops
                    // flowing — the day-check above is what actually prevents a stale bucket from
                    // being reused, regardless of whether the TTL has expired yet.
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
