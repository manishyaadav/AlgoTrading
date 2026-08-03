using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using MockDataStreamService.Models;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using MockDataStreamService.Events;

namespace MockDataStreamService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MockDataStreamController : Controller
    {
        private readonly ILogger<MockDataStreamController> _logger;
        private readonly HttpClient _client;
        private readonly string _bootStrapServer;
        private readonly string _ohlcTopicName;
        private readonly string _tickTopicName;
        private readonly ApiSettings _apiSettings;
        private List<Task<HistoricalDataResponse>> _historicalData = new List<Task<HistoricalDataResponse>>(); // To store the tasks
        
        private static string _connectionString = "Server=DESKTOP-F1HOE6M\\SQLEXPRESS;Database=Market_Stream_Data_India;Trusted_Connection=True;TrustServerCertificate=True;"; // **REPLACE THIS**

        public MockDataStreamController(ILogger<MockDataStreamController> logger, IOptions<ApiSettings> apiSettings )
        {
            _logger = logger;
            _apiSettings = apiSettings.Value;
            _bootStrapServer = _apiSettings.BootstrapServer;
            _client = new HttpClient();
            _client.BaseAddress = new Uri(_apiSettings.ApiUrl);
            _ohlcTopicName = _apiSettings.OhlcTopicName;
            _tickTopicName = _apiSettings.TickTopicName;
        }

        [HttpPost("ohlc")]
        public async Task<IActionResult> GenerateMockOhlcData([FromBody] MockDataStreamRequest request)
        {
            _logger.LogInformation("Inside Mock Ohlc Api...");
            foreach (var item in request.RequestData)
            {                
                _logger.LogInformation($"Exchange: {item.ExchangeName}, Instrument: {item.PartialInstrumentName}, Frequency: {item.ProducerFrequencyInSeconds}, Year: {item.Year}, Month: {item.month}");
                var response = await FetchOHLCData(_apiSettings.ApiUrl, item.Year, item.month, item.ExchangeName, item.PartialInstrumentName);
                Task<HistoricalDataResponse> task = Task.FromResult(response);
                _historicalData.Add(task);
            }

            var results = await Task.WhenAll(_historicalData);

            foreach (var task in _historicalData)
            {
                var response = await task; // Wait for each task here
                if (response.Records.Count > 0)
                {
                    // Display logic (same as before)
                    //... 
                    var first = response.Records.FirstOrDefault();
                    Console.WriteLine($"Data from Task first Record: {first.ContractName}, {first.Date}");
                }
            }

            // Find the common start date
            var commonStartDate = new DateTime(2025, 2, 7, 9, 15, 0);   //await FindCommonStartDate(_historicalData);

            // Display the common start date
            Console.WriteLine($"Common Start Date: {commonStartDate.ToString()}");
            // Inside this method:
            // 1. Validate the incoming 'request'.
            //      Validation is done by attributes on the request class and a custom validator
            // 2. Implement logic to generate mock OHLC data based on the request parameters. 
            // 3. Return the generated OHLC data in appropriate JSON format.
            // Set Color

            var tasks = _historicalData.Select(t => t.Result).ToList();

            var result = StreamOhlcDataToKafka(tasks, commonStartDate, _bootStrapServer, _ohlcTopicName);
            // Placeholder for now:
            return Ok("Mock OHLC data generation logic will go here");
        }

        static async Task StreamOhlcDataToKafka(IEnumerable<HistoricalDataResponse> responses, DateTime commonStartDate, string bootStrapServer, string topicName)
        {
            var conf = new ProducerConfig { BootstrapServers = bootStrapServer };
            using (var producer = new ProducerBuilder<string, string>(conf).Build())
            {
                var lastSentTimestamps = new Dictionary<string, DateTime>();

                Parallel.ForEach(responses, (response) =>
                {
                    if (response.Records.Count > 0)
                    {
                        var contractName = response.Records.First().ContractName;
                        if (!lastSentTimestamps.ContainsKey(contractName))
                        {
                            lastSentTimestamps[contractName] = DateTime.MinValue;
                        }

                        foreach (var ohlcRecord in response.Records.Where(r => r.Date >= commonStartDate))
                        {
                            // Ensure at least 1 second has elapsed for this contract
                            while (DateTime.Now - lastSentTimestamps[contractName] < TimeSpan.FromSeconds(1))
                            {
                                Thread.Sleep(5000);
                            }

                            var jsonRecord = JsonSerializer.Serialize(ohlcRecord);

                            
                            try
                            {

                                producer.ProduceAsync(topicName, new Message<string, string> { Key = ohlcRecord.ContractName.ToString(), Value = jsonRecord });
                                Console.WriteLine($"Topic: {topicName}, Data: {jsonRecord}, Timestamp: {DateTime.Now}");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"Error sending record to Kafka: {ex.Message}");
                            }

                            
                            lastSentTimestamps[contractName] = DateTime.Now;
                        }
                    }
                });
            }
        }

        static async Task<DateTime>? FindCommonStartDate(List<Task<HistoricalDataResponse>> tasks)
        {
            DateTime? latestStartDate = null;

            foreach (var task in tasks)
            {
                var response = await task;
                if (response.Records.Count > 0)
                {
                    var firstRecordTimestamp = response.Records.FirstOrDefault().Date; // Assuming you have a Timestamp property

                    if (latestStartDate == null || firstRecordTimestamp > latestStartDate)
                    {
                        latestStartDate = firstRecordTimestamp;
                    }
                }
            }

            return latestStartDate ?? DateTime.MinValue;
        }

        private ConsoleColor getColor(string partialInstrumentName)
        {
            ConsoleColor color = new ConsoleColor();
            if (partialInstrumentName.ToLower().Contains("bank") && partialInstrumentName.ToLower().Contains("nifty"))
            {
                if (partialInstrumentName.ToUpper().Contains("FUT"))
                    color = ConsoleColor.Magenta;
                else
                    color = ConsoleColor.Yellow;
            }
            else
            {
                if (partialInstrumentName.ToUpper().Contains("FUT"))
                    color = ConsoleColor.Cyan;
                else
                    color = ConsoleColor.Green;
            }

            return color;
        }

        

        static async Task<HistoricalDataResponse> FetchOHLCData(string apiUrl, int year, int month, string exchange, string instrument)
        {
            HttpClient client = new HttpClient();
            string urlWithParams = $"{apiUrl}?year={year}&month={month}&exchange={exchange}&instrumentName={instrument}";
            HttpResponseMessage response = await client.GetAsync(urlWithParams);
            string jsonResponse = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Json Response Length: {jsonResponse.Length}");

            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(jsonResponse))
            {
                var apiResponse = JsonSerializer.Deserialize<HistoricalDataResponse>(jsonResponse);
                Console.WriteLine("Deserialization done ...");
                if (apiResponse != null)
                {
                    Console.WriteLine($"Api Response Record Count: {apiResponse.TotalRecords}, Full Path: {apiResponse.FullPath}");
                    return apiResponse;
                }

                else
                {
                    Console.WriteLine($"Error getting data from historical api {apiUrl}");
                    return new HistoricalDataResponse()
                    {
                        FullPath = "Response didn't has any data ..."
                    };
                }
            }
            else
            {
                Console.WriteLine("API call was not successful or the response was empty");
                return new HistoricalDataResponse()
                {
                    FullPath = "Not Success ..."
                };
            }
        }       
    }
}
