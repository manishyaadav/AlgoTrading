using System.Text.Json;
using AggregatorFunctions.Common;
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
    public class Aggregation60MinutesFunction
    {
        private readonly ILogger<Aggregation60MinutesFunction> _logger;
        private static Dictionary<string, List<TimeFrameAggregationEvent>> _bufferData = new Dictionary<string, List<TimeFrameAggregationEvent>>();
        private static List<Bucket> _bucket = new List<Bucket>();
        private static int _timeframe = 60;
        private static int _base = 15;
        private readonly IProducer<string, string> _producer;        
        private static string _producerTopicName = $"live-aggregation-ohlc-{_timeframe}min-topic";

        public Aggregation60MinutesFunction(ILogger<Aggregation60MinutesFunction> logger,
                    IConfiguration configuration,
                    IProducer<string, string> producer)
        {
            _logger = logger;
            _producer = producer; // Inject the producer   
        }

        [Function("StreamAggregator60MFunction")]
        public async Task Run(
               [KafkaTrigger("%KAFKA_BROKER_URL%",
                  "live-aggregation-ohlc-15min-topic",
                  AuthenticationMode = BrokerAuthenticationMode.Plain,
                  ConsumerGroup = "live-aggregator-60min-consumer")] string eventDataJson, FunctionContext context)
        {
            var logger = context.GetLogger("KafkaFunction");
            logger.LogInformation($"Kafka Trigged on topic : live-aggregation-ohlc-15min-topic at: {DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")}");
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

        private async Task ProcessKafkaMessage(string eventData, ILogger logger)
        {
            TimeFrameAggregationEvent? eventValue;

            try
            {
                eventValue = JsonSerializer.Deserialize<TimeFrameAggregationEvent>(eventData);

                if (eventValue != null)
                {
                    logger.LogInformation($"Event Details Ticker: {eventValue.Ticker}, Timeframe: {eventValue.Timeframe}, Time: {eventValue.WindowsStartTime}");

                    if (eventValue != null)
                    {
                        if (!_bufferData.ContainsKey(eventValue.Ticker))
                        {
                            _bufferData[eventValue.Ticker] = new List<TimeFrameAggregationEvent>();
                        }

                        _bufferData[eventValue.Ticker].Add(eventValue);

                        logger.LogInformation($"bufferData Count for key: {eventValue.Ticker}:{eventValue.Timeframe} is {_bufferData[eventValue.Ticker].Count}");

                        DateTime currentTime = DateTime.Now;

                        //DateTime currentTime = DateTime.Now;
                        var offset = (currentTime.Minute % _timeframe) >= _base ? _base : -(_timeframe - _base);

                        DateTime tmpBucketStart = currentTime.AddMinutes(-(currentTime.Minute % _timeframe)).AddMinutes(offset);
                        DateTime bucketStart = new DateTime(tmpBucketStart.Year, tmpBucketStart.Month, tmpBucketStart.Day, tmpBucketStart.Hour, tmpBucketStart.Minute, 0);
                        DateTime bucketEnd = bucketStart.AddMinutes(_timeframe);
                        logger.LogInformation($"\nTIMEFRAME: {eventValue.Timeframe}, CURRENT TIME: {currentTime}, BUCKET START: {bucketStart}, BUCKET END: {bucketEnd}");

                        var bckt = _bucket.Where(x => x.InstrumentName.Equals(eventValue.Ticker)).FirstOrDefault();

                        if (bckt != null)
                        {
                            if (bckt.currentBucket.startTime != bucketStart && bckt.currentBucket.endTime != bucketEnd)
                            {

                                var data = _bufferData[eventValue.Ticker].Where(x => x.WindowsStartTime >= bckt.currentBucket.startTime && x.WindowsStartTime < bckt.currentBucket.endTime).ToList();
                                if (data.Count() >= _timeframe / _base)
                                {
                                    // Create aggregation data 
                                    var aggregatedData = CalculateMinAggregation(_timeframe, data);
                                    if (aggregatedData != null)
                                    {
                                        var ohlcvJson = JsonSerializer.Serialize(aggregatedData);
                                        string key = $"{aggregatedData.Ticker}:{_timeframe}Min";
                                        // and push that to another topic
                                        // Send the aggregated data to the Kafka topic
                                        logger.LogInformation($"Aggregated Data: {ohlcvJson}");
                                        await ProduceToKafka(_producerTopicName, ohlcvJson, key, logger);
                                        // Set the buffer for the ticker to initial state
                                        _bufferData[eventValue.Ticker] = new List<TimeFrameAggregationEvent>();
                                    }
                                    else
                                    {
                                        logger.LogError($"aggregation data is NULL");
                                    }

                                }

                                bckt.prevBucket = new BucketItem()
                                {
                                    startTime = bckt.currentBucket.startTime,
                                    endTime = bckt.currentBucket.endTime
                                };
                                bckt.currentBucket = new BucketItem()
                                {
                                    startTime = bucketStart,
                                    endTime = bucketEnd,
                                };
                            }

                        }
                        else
                        {
                            _bucket.Add(new Bucket()
                            {
                                InstrumentName = eventValue.Ticker,
                                currentBucket = new BucketItem()
                                {
                                    startTime = bucketStart,
                                    endTime = bucketEnd,
                                },
                                prevBucket = new BucketItem()
                                {
                                    startTime = bucketStart,
                                    endTime = bucketEnd,
                                }
                            });
                        }

                        var tmpBucketDisplay = _bucket.Where(x => x.InstrumentName.Equals(eventValue.Ticker)).FirstOrDefault();
                        if (tmpBucketDisplay != null)
                        {
                            logger.LogWarning($"\n\tBUCKET DATA: {tmpBucketDisplay.InstrumentName}:{_timeframe}, \n\tCURRENT: start-{tmpBucketDisplay.currentBucket.startTime} end-{tmpBucketDisplay.currentBucket.endTime}, \n\tPREVIOUS: start-{tmpBucketDisplay.prevBucket.startTime} end-{tmpBucketDisplay.prevBucket.endTime}");
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

        private TimeFrameAggregationEvent? CalculateMinAggregation(int timeframe, List<TimeFrameAggregationEvent> data)
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
                    Producer = "aggregator.service",
                    ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset)
                };
            }
            else
                return null;
        }
    }
}
