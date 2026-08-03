using AggregatorFunctions.Common;
using AggregatorFunctions.SharedLibrary.Enums.Candle;
using AggregatorFunctions.SharedLibrary.Events.Aggregation;
using AggregatorFunctions.SharedLibrary.Events.Aggregation.Candle;
using AggregatorFunctions.SharedLibrary.Events.Aggregation.TimeFrame;
using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Configuration;
using System.Text.Json;
using System.Xml.Linq;
using static Confluent.Kafka.ConfigPropertyNames;

namespace AggregatorFunctions.Marking
{
    public class PriceObservsorForMarkingFunction
    {
        private readonly ILogger<PriceObservsorForMarkingFunction> _logger;
        
        private static int _timeframe = 5;
        private static int _preiod = 20;
        private static int _lookBackPeriod = 2;
        private static int _cnt = 0;

        private static Dictionary<string, MarkingAggregationEvent>_currentPotentialSupport;
        private static Dictionary<string, MarkingAggregationEvent> _currentPotentialResistance;

        private static Dictionary<string, MarkingAggregationEvent> _previousActualSupport;
        private static Dictionary<string, MarkingAggregationEvent> _previousActualResistance;

        private static Dictionary<string, MarkingAggregationEvent> _currentPrice;

        private readonly IProducer<string, string> _producer;
        private static string _producerTopicName = $"mock-marking-level-1-aggregation-{_timeframe}min-topic";

        public PriceObservsorForMarkingFunction(ILogger<PriceObservsorForMarkingFunction> logger,
                                Microsoft.Extensions.Configuration.IConfiguration configuration,
                                 IProducer<string, string> producer)
        {
            _logger = logger;
            _producer = producer; // Inject the producer              
        }        

        [Function("PriceObservsor5MinForMarkingFunction")]
        public async Task Run(
               [KafkaTrigger("%KAFKA_BROKER_URL%",
                  "mock--candle-aggregation-5min-20period-topic",
                  AuthenticationMode = BrokerAuthenticationMode.Plain,
                  ConsumerGroup = "mock-candle-5min-price-for-swing-observor")] string eventDataJson, FunctionContext context)
        {
            _cnt++;
            var logger = context.GetLogger("KafkaFunction");
            logger.LogInformation($"Kafka Trigged on topic : mock--candle-aggregation-5min-20period-topic at: {DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")}");
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

        private async Task ProcessKafkaMessage(string eventData, ILogger logger)
        {
            CandleAggregationEvent? eventValue;

            try
            {
                eventValue = JsonSerializer.Deserialize<CandleAggregationEvent>(eventData);

                if (eventValue != null)
                {
                    logger.LogInformation($"Mock Event Details Ticker: {eventValue.Ticker}, Timeframe: {eventValue.Timeframe}, Time: {eventValue.WindowsStartTime}");

                    if (eventValue != null)
                    {
                        if (_cnt == 1)
                        {
                            if (eventValue.Color.Equals("Green"))
                            {
                                _currentPotentialResistance = new Dictionary<string, MarkingAggregationEvent>();
                                _currentPotentialResistance.Add(eventValue.Ticker, new MarkingAggregationEvent()
                                {
                                    Ticker = eventValue.Ticker,
                                    Timeframe = eventValue.Timeframe,
                                    MarkingTime = eventValue.WindowsStartTime,
                                    MarkingPointVal = eventValue.Low,
                                    MarkingType = SharedLibrary.Enums.Swing.MarkingTypeEnum.Up
                                });

                                _currentPotentialSupport = new Dictionary<string, MarkingAggregationEvent>();
                                _currentPotentialSupport.Add(eventValue.Ticker, new MarkingAggregationEvent()
                                {
                                    Ticker = eventValue.Ticker,
                                    Timeframe = eventValue.Timeframe,
                                    MarkingTime = eventValue.WindowsStartTime,
                                    MarkingPointVal = eventValue.High,
                                    MarkingType = SharedLibrary.Enums.Swing.MarkingTypeEnum.Down
                                });

                                _currentPrice = new Dictionary<string, MarkingAggregationEvent>();
                                _currentPotentialSupport.Add(eventValue.Ticker, new MarkingAggregationEvent()
                                {
                                    Ticker = eventValue.Ticker,
                                    Timeframe = eventValue.Timeframe,
                                    MarkingTime = eventValue.WindowsStartTime,
                                    MarkingPointVal = eventValue.Close
                                });
                            }
                            else if (eventValue.Color.Equals("Red"))
                            {
                                _currentPotentialSupport = new Dictionary<string, MarkingAggregationEvent>();
                                _currentPotentialSupport.Add(eventValue.Ticker, new MarkingAggregationEvent()
                                {
                                    Ticker = eventValue.Ticker,
                                    Timeframe = eventValue.Timeframe,
                                    MarkingTime = eventValue.WindowsStartTime,
                                    MarkingPointVal = eventValue.High,
                                    MarkingType = SharedLibrary.Enums.Swing.MarkingTypeEnum.Down
                                });

                                _currentPotentialResistance = new Dictionary<string, MarkingAggregationEvent>();
                                _currentPotentialResistance.Add(eventValue.Ticker, new MarkingAggregationEvent()
                                {
                                    Ticker = eventValue.Ticker,
                                    Timeframe = eventValue.Timeframe,
                                    MarkingTime = eventValue.WindowsStartTime,
                                    MarkingPointVal = eventValue.Low,
                                    MarkingType = SharedLibrary.Enums.Swing.MarkingTypeEnum.Up
                                });                                

                                _currentPrice = new Dictionary<string, MarkingAggregationEvent>();
                                _currentPotentialSupport.Add(eventValue.Ticker, new MarkingAggregationEvent()
                                {
                                    Ticker = eventValue.Ticker,
                                    Timeframe = eventValue.Timeframe,
                                    MarkingTime = eventValue.WindowsStartTime,
                                    MarkingPointVal = eventValue.Close
                                });
                            }
                            else
                            {
                                logger.LogInformation($"Mock Event Details Ticker: {eventValue.Ticker}, Timeframe: {eventValue.Timeframe}, Time: {eventValue.WindowsStartTime} has not defined Color");
                            }
                        }
                        else
                        {
                            ProcessCurrentPrice(eventValue, eventValue.Open);
                            ProcessCurrentPrice(eventValue, eventValue.Color.Equals("Green") ? eventValue.Low : eventValue.High);
                            ProcessCurrentPrice(eventValue, eventValue.Color.Equals("Green") ? eventValue.High : eventValue.Low);
                            ProcessCurrentPrice(eventValue, eventValue.Close);
                        }    
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Aggregation Error: " + ex.Message);
            }
        }

        private void ProcessCurrentPrice(CandleAggregationEvent eventValue, decimal value)
        {
            
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
