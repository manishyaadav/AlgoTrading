using DataIngestionFunctions.SharedLibrary.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SharedLibrary.Events.AlertIngestion;
using SharedLibrary.Helpers;
using SharedLibrary.Enums.AlertFeed;
using SharedLibrary.Events;
using Confluent.Kafka;

namespace DataIngestionFunctions
{
    public class AlertToSQLFunction
    {
        private readonly ILogger<AlertToSQLFunction> _logger;
        private readonly IProducer<string, string> _producer;
        private static string _producerAlertTopicName = $"live-tradingview-alert-topic";
        private static string _connectionString = "Server=DESKTOP-F1HOE6M\\SQLEXPRESS;Database=Market_Stream_Data_India;Trusted_Connection=True;TrustServerCertificate=True;"; // **REPLACE THIS**
        //private static string _connectionString = "Server=192.168.1.4\\SQLEXPRESS;Database=Market_Stream_Data_India;Trusted_Connection=True;TrustServerCertificate=True;"; // **REPLACE THIS**

        private bool alertIngestion = true;


        public AlertToSQLFunction(ILogger<AlertToSQLFunction> logger, IProducer<string, string> producer)
        {
            _logger = logger;
            _producer = producer;
        }

        [Function(nameof(TradingViewAlertToSQLFunction))]
        public async Task TradingViewAlertToSQLFunction(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dataingestion/tradingview/funcTradingViewAlertTestSQL")] HttpRequestData req)
        {
            if (alertIngestion)
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                var options = new JsonSerializerOptions
                {
                    NumberHandling = JsonNumberHandling.AllowReadingFromString // Allow numeric strings
                };

                TradingViewAlertEvent? alertFromTradingView = JsonSerializer.Deserialize<TradingViewAlertEvent>(requestBody, options);

                if (alertFromTradingView != null)
                {
                    AlertIngestionEvent alertEvent = CreateAlertEvent(alertFromTradingView);
                    var key = alertEvent.SourceToken;
                    var message = JsonSerializer.Serialize(alertEvent);

                    _logger.LogInformation($"{DateTime.Now} - Processing TradingView Alert: {alertFromTradingView?.SourceToken}, PointValue: {alertFromTradingView?.PointVal}, Direction: {alertFromTradingView?.Direction}, EventTime: {alertFromTradingView?.EventTime}, Time: {alertFromTradingView?.WindowsStartTime}");

                    if (alertEvent != null)
                    {  
                        await ProduceToKafka(_producerAlertTopicName, key, message, _logger);
                    }
                    try
                    {
                        // 1. Insert into SQL Database
                        using (SqlConnection connection = new SqlConnection(_connectionString))
                        {
                            await connection.OpenAsync();

                            string sql = @"
                    INSERT INTO TradingViewAlertEvent (SourceToken, Ticker, Timeframe, LookBackPeriod, MinBodyStrength, Q3Multiplier, AlertType, Level, Length, Direction, PointVal, BodyStrength, Q3Value, ProducedAt, ProducedBy, WindowsStartTime, InsertedDate, Version)
                    VALUES (@SourceToken, @Ticker, @Timeframe, @LookBackPeriod, @MinBodyStrength, @Q3Multiplier, @AlertType, @Level, @Length, @Direction, @PointVal, @BodyStrength, @Q3Value, @ProducedAt, @ProducedBy, @WindowsStartTime, @InsertedDate, @Version);
                ";

                            using (SqlCommand command = new SqlCommand(sql, connection))
                            {
                                // Add parameters to prevent SQL injection
                                command.Parameters.AddWithValue("@SourceToken", alertEvent.SourceToken);
                                command.Parameters.AddWithValue("@Ticker", alertEvent.Ticker);
                                command.Parameters.AddWithValue("@Timeframe", alertEvent.Timeframe);

                                command.Parameters.AddWithValue("@LookBackPeriod", alertEvent.LookBackPeriod); 
                                command.Parameters.AddWithValue("@MinBodyStrength", alertEvent.MinBodyStrength); 
                                command.Parameters.AddWithValue("@Q3Multiplier", alertEvent.Q3Multiplier); 

                                command.Parameters.AddWithValue("@AlertType", alertEvent.AlertType);
                                command.Parameters.AddWithValue("@Level", alertEvent.Level); 
                                command.Parameters.AddWithValue("@Length", alertEvent.Length);
                                command.Parameters.AddWithValue("@Direction", alertEvent.Direction);
                                command.Parameters.AddWithValue("@PointVal", alertEvent.PointVal);
                                
                                command.Parameters.AddWithValue("@BodyStrength", alertEvent.BodyStrength);
                                command.Parameters.AddWithValue("@Q3Value", alertEvent.Q3Value);
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


                    _logger.LogInformation($"{DateTime.Now} - Alert pushed to SQL: {alertFromTradingView?.SourceToken}, PointValue: {alertFromTradingView?.PointVal}, Direction: {alertFromTradingView?.Direction}, EventTime: {alertFromTradingView?.EventTime}, Time: {alertFromTradingView?.WindowsStartTime}");
                }
            }
            
        }

        private async Task ProduceToKafka(string topicName, string key, string value, ILogger logger)
        {
            try
            {
                var deliveryReport = await _producer.ProduceAsync
                        (
                            topicName,
                            new Message<string, string>
                            {
                                Key = key,
                                Value = value
                            }
                        );
                logger.LogInformation($"Delivered message to: {deliveryReport.TopicPartitionOffset}");
            }
            catch (ProduceException<string, string> e)
            {
                logger.LogError($"Delivery failed: {e.Error.Reason}");
            }
        }

        private AlertIngestionEvent CreateAlertEvent(TradingViewAlertEvent alertFromTradingView)
        {
            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

            return new AlertIngestionEvent()
            {
                SourceToken = alertFromTradingView.SourceToken,
                Ticker = alertFromTradingView.SourceToken,
                Timeframe = int.Parse(alertFromTradingView.Timeframe),

                LookBackPeriod = int.Parse(alertFromTradingView.CandleLookback),
                MinBodyStrength = int.Parse(alertFromTradingView.MinBodyStrength),
                Q3Multiplier = decimal.Parse(alertFromTradingView.Q3Multiplier),

                AlertType = alertFromTradingView.Type,
                Level = int.Parse(alertFromTradingView.Level),                
                Length = int.Parse(alertFromTradingView.Length),
                Direction = int.Parse(alertFromTradingView.Direction),
                PointVal = decimal.Parse(alertFromTradingView.PointVal),

                Q3Value = decimal.Parse(alertFromTradingView.Q3Value),
                BodyStrength = decimal.Parse(alertFromTradingView.BodyStrength),    
                WindowsStartTime = alertFromTradingView.WindowsStartTime,

                Producer = "alertingestion.service",
                ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset),
                DataSource = AlertFeedSourceEnum.TradingView,
                DataType = AlertFeedTypeEnum.IndicatorAlert,
                Version = alertFromTradingView.Version
            };
        }

        public static DateTime ConvertToLocalTime(DateTime utcTime)
        {
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); // For example, "GMT Standard Time" represents GMT+0
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone);
        }
    }
}
