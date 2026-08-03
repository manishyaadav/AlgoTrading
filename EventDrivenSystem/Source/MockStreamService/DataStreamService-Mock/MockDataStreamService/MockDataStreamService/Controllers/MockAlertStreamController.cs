using Microsoft.AspNetCore.Mvc;
using MockDataStreamService.Models;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using MockDataStreamService.Events;
using Confluent.Kafka;
using System.Text.Json;
using System.Data;

namespace MockDataStreamService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MockAlertStreamController : Controller
    {
        private readonly ILogger<MockAlertStreamController> _logger;
        private readonly HttpClient _client;
        private readonly string _bootStrapServer;
        private readonly string _ohlcTopicName;
        private readonly string _tickTopicName;
        private readonly ApiSettings _apiSettings;
        private readonly string _connectionString = ""; // **REPLACE THIS**
        private List<TradingViewAlertEvent> responseData = new List<TradingViewAlertEvent>();
        private readonly DateTime startTime;

        public MockAlertStreamController(ILogger<MockAlertStreamController> logger, IOptions<ApiSettings> apiSettings)
        {
            _logger = logger;
            _apiSettings = apiSettings.Value;
            _bootStrapServer = _apiSettings.BootstrapServer;
            _client = new HttpClient();
            _client.BaseAddress = new Uri(_apiSettings.ApiUrl);
            _ohlcTopicName = _apiSettings.OhlcTopicName;
            _tickTopicName = _apiSettings.TickTopicName;
            _connectionString = _apiSettings.SQLServerConnectionString;
            startTime = new DateTime(2025, 2, 7, 9, 15, 0); // Year, Month, Day, Hour, Minute, Second
        }

        [HttpPost("alerts/zigzag")]
        public async Task<IActionResult> GenerateMockAlertZigZag([FromBody] MockZigZagAlertStreamRequest request)
        {
            _logger.LogInformation("Inside Mock Alert ZizZag Api...");
            foreach (var item in request.RequestData)
            {
                _logger.LogInformation($"Instrument: {item.PartialInstrumentName}, Frequency: {item.ProducerFrequencyInSeconds}, Year: {item.Year}, Month: {item.month}, Timeframe: {item.Timeframe}, Length: {item.Length}, Level: {item.Level}, Version: {item.Version} ");
                responseData = await FetchZizZagDataFromSQL(item);
            }

            await StreamOhlcDataToKafka(responseData, responseData.FirstOrDefault().WindowsStartTime, _bootStrapServer, _tickTopicName);

            return Ok("Mock ZigZag Alert generation logic will go here");
        }

        static async Task StreamOhlcDataToKafka(List<TradingViewAlertEvent> responses, DateTime commonStartDate, string bootStrapServer, string topicName)
        {
            int index = 1;
            var conf = new ProducerConfig { BootstrapServers = bootStrapServer };
            using (var producer = new ProducerBuilder<string, string>(conf).Build())
            {
                var lastSentTimestamps = new Dictionary<string, DateTime>();

                    foreach (var zigZagAlert in responses.Skip(1))
                    {
                        var previous = responses[index-1];
                        
                        var timeToSleep = getTimeToSleep(zigZagAlert, previous);
                        Thread.Sleep(5000 * timeToSleep);

                        var jsonRecord = JsonSerializer.Serialize(zigZagAlert);

                                try
                                {

                                    producer.ProduceAsync(topicName, new Message<string, string> { Key = zigZagAlert.Ticker.ToString(), Value = jsonRecord });
                                    Console.WriteLine($"Topic: {topicName}, Data: {jsonRecord}, Timestamp: {DateTime.Now}");
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine($"Error sending record to Kafka: {ex.Message}");
                                }
                    index++;
                    }
            }                
    }

        private static int getTimeToSleep(TradingViewAlertEvent zigZagAlert, TradingViewAlertEvent previous)
        {
            TimeSpan timeDifference = zigZagAlert.WindowsStartTime - previous.WindowsStartTime;

           if (zigZagAlert.WindowsStartTime.Date != previous.WindowsStartTime.Date)
            {
                DateTime tmp = new DateTime(zigZagAlert.WindowsStartTime.Year, zigZagAlert.WindowsStartTime.Month, zigZagAlert.WindowsStartTime.Day, 9, 15, 0);
                timeDifference = zigZagAlert.WindowsStartTime - tmp;
            }

            // Handle potential negative time differences (if needed)
            if (timeDifference < TimeSpan.Zero)
            {
                timeDifference = -timeDifference; // Or handle it differently, like logging an error
                Console.WriteLine("Warning: Time difference is negative.");
            }

            return( int)(timeDifference.TotalMinutes);
        }

        [NonAction]
        private async Task<List<TradingViewAlertEvent>> FetchZizZagDataFromSQL(MockZigZagAlertStreamItem zigZagAlert)
        {
            List<TradingViewAlertEvent> results = new List<TradingViewAlertEvent>(); // Store results

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
            SELECT [Id], [SourceToken], [Ticker], [Timeframe], [LookBackPeriod],
                   [MinBodyStrength], [Q3Multiplier], [AlertType], [Level], [Length],
                   [Direction], [PointVal], [BodyStrength], [Q3Value], [ProducedAt],
                   [ProducedBy], [WindowsStartTime], [InsertedDate], [Version]
            FROM [dbo].[TradingViewAlertEvent]
            WHERE Ticker = @Ticker AND
               Timeframe = @Timeframe
              AND LookBackPeriod = @LookBackPeriod
              AND MinBodyStrength = @MinBodyStrength
              AND Q3Multiplier = @Q3Multiplier
              AND Level = @Level
              AND Length = @Length
              AND Version = @Version
                AND WindowsStartTime >= @WindowsStartTime";
                //
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    // Add parameters to prevent SQL injection (COMPLETE)
                    command.Parameters.AddWithValue("@Ticker", zigZagAlert.TickerName);
                    command.Parameters.AddWithValue("@Timeframe", zigZagAlert.Timeframe);
                    command.Parameters.AddWithValue("@LookBackPeriod", zigZagAlert.LookBackPeriod);
                    command.Parameters.AddWithValue("@MinBodyStrength", zigZagAlert.MinBodyStrength);
                    command.Parameters.AddWithValue("@Q3Multiplier", zigZagAlert.Q3Multiplier);
                    command.Parameters.AddWithValue("@Level", zigZagAlert.Level);
                    command.Parameters.AddWithValue("@Length", zigZagAlert.Length);
                    command.Parameters.AddWithValue("@Version", zigZagAlert.Version);
                    // ... add other parameters as needed ...
                    // For the date/time parameter, use a DateTime object:
                    
                    command.Parameters.Add("@WindowsStartTime", SqlDbType.DateTime).Value = startTime; // Explicitly set type


                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            TradingViewAlertEvent alert = new TradingViewAlertEvent(); // Create object
                            alert.Id = reader.GetInt32(0); // Assuming Id is an int
                            alert.SourceToken = reader.GetString(1);
                            alert.Ticker = reader.GetString(2);
                            alert.Timeframe = reader.GetInt32(3);
                            alert.LookBackPeriod = reader.GetInt32(4);
                            alert.MinBodyStrength = reader.GetInt32(5); // Example: adjust as needed
                            alert.Q3Multiplier = reader.GetInt32(6); // Example: adjust as needed
                            alert.AlertType = reader.IsDBNull(7) ? null : reader.GetString(7); // Handle nullable
                            alert.Level = reader.GetInt32(8);
                            alert.Length = reader.GetInt32(9);
                            alert.Direction = reader.GetInt32(10); // Handle nullable
                            alert.PointVal = reader.GetDecimal(11); // Example: adjust as needed
                            alert.BodyStrength = reader.GetDecimal(12); // Example: adjust as needed
                            alert.Q3Value = reader.GetDecimal(13); // Example: adjust as needed
                            alert.ProducedAt = reader.GetDateTime(14);
                            alert.ProducedBy = reader.GetString(15);
                            alert.WindowsStartTime = reader.GetDateTime(16);
                            alert.InsertedDate = reader.GetDateTime(17);
                            alert.Version = reader.GetString(18);
                            // ... map other properties ...

                            results.Add(alert); // Add to the list
                        }
                    }
                }
            }

            return results; // Return the list of objects
        }
    }
}
