using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SharedLibrary.Enums.AlertFeed;
using SharedLibrary.Events.AlertIngestion;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using PivotMarkingAggregationFunction.SharedLibrary.Events.AlertIngestion;


namespace PivotMarkingAggregationFunction
{
    public class PivotMarkingFunction
    {
        private readonly ILogger<PivotMarkingFunction> _logger;
        private readonly IProducer<string, string> _producer;
        private static string _connectionString = "Server=DESKTOP-F1HOE6M\\SQLEXPRESS;Database=Market_Stream_Data_India;Trusted_Connection=True;TrustServerCertificate=True;"; // **REPLACE THIS**
        private readonly string _producerTopicName;
        private readonly int _level;
        private readonly int _length;

        // Use ConcurrentDictionary for thread safety in a function app environment
        private static Dictionary<string, AlertIngestionEvent> _bufferData = new Dictionary<string, AlertIngestionEvent>();

        public PivotMarkingFunction(ILogger<PivotMarkingFunction> logger, IProducer<string, string> producer)
        {
            _logger = logger;
            _producer = producer;
            _level = 0; // Make these configurable if needed
            _length = 2;
            _producerTopicName = $"live-pivot-marking-{_level}level-{_length}length-topic-4";
        }

        [Function("ZigZagAlertZeroLevelFunction")]
        public async Task Run(
        [KafkaTrigger("%KAFKA_BROKER_URL%",
          "live-tradingview-alert-topic",
          AuthenticationMode = BrokerAuthenticationMode.Plain,
          ConsumerGroup = "live-alertingestion-zero-level-aggregator-4")] string eventDataJson, FunctionContext context)
        {
            var logger = context.GetLogger("KafkaFunction");
            logger.LogInformation($"Kafka Trigged on topic : live-tradingview-alert-topic at: {DateTime.Now:yyyy-MM-ddTHH:mm:ss}");

            try
            {
                using (JsonDocument document = JsonDocument.Parse(eventDataJson)) // Correct usage
                {
                    if (document.RootElement.TryGetProperty("Value", out var valueElement))
                    {
                        await ProcessKafkaMessage(valueElement.GetString() ?? string.Empty, logger);
                    }
                    else
                    {
                        logger.LogWarning("Kafka message does not contain a 'Value' property.");
                    }
                }
            }
            catch (JsonException ex) // Catch JSON parsing exceptions
            {
                logger.LogError(ex, "Error parsing JSON: Invalid JSON format.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing Kafka message");
            }
        }



        private async Task ProcessKafkaMessage(string eventData, ILogger logger)
        {
            try
            {
                var eventValue = JsonSerializer.Deserialize<AlertIngestionEvent>(eventData);

                if (eventValue == null)
                {
                    logger.LogWarning("Failed to deserialize event data.");
                    return;
                }

                logger.LogInformation($"Event Details Ticker: {eventValue.Ticker}, Timeframe: {eventValue.Timeframe}, Time: {eventValue.WindowsStartTime}");

                var keyFromEvent = $"{eventValue.Ticker}:{eventValue.Timeframe}:{eventValue.AlertType}:{eventValue.Level}:{eventValue.Level}:{eventValue.Version}";

                if (_bufferData.ContainsKey(keyFromEvent))
                {
                    var previous = _bufferData[keyFromEvent];

                    if (GetDirection(eventValue.Direction) != GetDirection(previous.Direction) &&
                        GetDirection(eventValue.Direction) != MarkingTypeEnum.NOTAPPLICABLE &&
                        GetDirection(previous.Direction) != MarkingTypeEnum.NOTAPPLICABLE)
                    {
                        var pivotMarking = CreatePivotMarking(previous);

                        // Send the previous value to Kafka topic
                        var ohlcvJson = JsonSerializer.Serialize(pivotMarking);
                        string key = $"{eventValue.Ticker}:{eventValue.Timeframe}:{eventValue.AlertType}:{eventValue.Level}:{eventValue.Level}:{eventValue.Version}";


                        // Send the aggregated data to the Kafka topic
                        logger.LogInformation($"Marking Data: {ohlcvJson}");
                        await ProduceToKafka(_producerTopicName, ohlcvJson, key, logger);
                        logger.LogInformation("Marking Data: Sent to Kafka");
                        await SaveToDatabase(pivotMarking);
                        logger.LogInformation("Marking Data: Sent to DB");
                    }

                    _bufferData[keyFromEvent] = eventValue; // Update existing entry
                    Console.WriteLine($"Updated entry for key: {keyFromEvent}");
                }
                else
                {
                    _bufferData.Add(keyFromEvent, eventValue); // Add new entry
                    Console.WriteLine($"Added new entry for key: {keyFromEvent}");
                }
               
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Aggregation Error");
            }
        }

        private PivotMarkingEvent CreatePivotMarking(AlertIngestionEvent alertIngestionEvent)
        {
            if (alertIngestionEvent != null)
            {
                return new PivotMarkingEvent()
                {
                    SourceToken = alertIngestionEvent.SourceToken,
                    Ticker = alertIngestionEvent.SourceToken,
                    Timeframe = alertIngestionEvent.Timeframe,

                    AlertType = alertIngestionEvent.AlertType,
                    Level = alertIngestionEvent.Level,
                    Length = alertIngestionEvent.Length,
                    Direction = alertIngestionEvent.Direction,
                    PointVal = alertIngestionEvent.PointVal,
                    MarkingType = GetDirection(alertIngestionEvent.Direction).ToString(),
                    WindowsStartTime = alertIngestionEvent.WindowsStartTime,

                    Producer = "pivot.marking.service",
                    ProducedAt = alertIngestionEvent.ProducedAt,
                    DataSource = alertIngestionEvent.DataSource,
                    DataType = alertIngestionEvent.DataType,
                    Version = alertIngestionEvent.Version
                };
            }
            else
            {
                return null;
            }
        }

        private static MarkingTypeEnum GetDirection(int direction)
        {
            return direction switch
            {
                > 0 => MarkingTypeEnum.UP,
                < 1 => MarkingTypeEnum.DOWN,                
            };
        }

        private async Task ProduceToKafka(string topicName, string message, string key, ILogger logger)
        {
            try
            {
                var deliveryResult = await _producer.ProduceAsync(topicName, new Message<string, string> { Key = key, Value = message });
                logger.LogInformation($"Kafka message PRODUCED SUCCESSFULLY to partition {deliveryResult.Partition} at offset {deliveryResult.Offset}");
            }
            catch (ProduceException<string, string> e)
            {
                logger.LogError(e, $"Delivery failed: {e.Error.Reason}");
            }
        }

      
        private async Task SaveToDatabase(PivotMarkingEvent alertEvent)
        {
            try
            {
                // 1. Insert into SQL Database
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string sql = @"
                    INSERT INTO PivotMarkingEvent (SourceToken, Ticker, Timeframe, AlertType, Level, Length, Direction, PointVal, MarkingType, ProducedAt, ProducedBy, WindowsStartTime, InsertedDate, Version)
                    VALUES (@SourceToken, @Ticker, @Timeframe,  @AlertType, @Level, @Length, @Direction, @PointVal,  @MarkingType, @ProducedAt, @ProducedBy, @WindowsStartTime, @InsertedDate, @Version);
                ";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        // Add parameters to prevent SQL injection
                        command.Parameters.AddWithValue("@SourceToken", alertEvent.SourceToken);
                        command.Parameters.AddWithValue("@Ticker", alertEvent.Ticker);
                        command.Parameters.AddWithValue("@Timeframe", alertEvent.Timeframe);
                        
                        command.Parameters.AddWithValue("@AlertType", alertEvent.AlertType);
                        command.Parameters.AddWithValue("@Level", alertEvent.Level);
                        command.Parameters.AddWithValue("@Length", alertEvent.Length);
                        command.Parameters.AddWithValue("@Direction", alertEvent.Direction);
                        command.Parameters.AddWithValue("@PointVal", alertEvent.PointVal);
                        command.Parameters.AddWithValue("@MarkingType", alertEvent.MarkingType);

                        command.Parameters.AddWithValue("@ProducedAt", alertEvent.ProducedAt);
                        command.Parameters.AddWithValue("@ProducedBy", alertEvent.Producer);
                        command.Parameters.AddWithValue("@WindowsStartTime", alertEvent.WindowsStartTime);
                        command.Parameters.AddWithValue("@InsertedDate", DateTime.Now);
                        command.Parameters.AddWithValue("@Version", alertEvent.Version);

                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("Data inserted into SQL successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing data.");
            }
        }
    }
}