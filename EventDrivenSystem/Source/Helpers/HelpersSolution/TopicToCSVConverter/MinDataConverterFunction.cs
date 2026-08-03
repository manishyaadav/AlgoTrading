using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Json;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TopicToCSVConverter.Events;
using TopicToCSVConverter.Models;

namespace TopicToCSVConverter
{
    public class MinDataConverterFunction
    {
        private readonly ILogger<MinDataConverterFunction> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private static DateTime _date = DateTime.MinValue;

        public MinDataConverterFunction(BlobServiceClient blobServiceClient, ILogger<MinDataConverterFunction> logger)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }

        [Function("MinDataConverter")]
        public async Task Run(
               [KafkaTrigger("%KAFKA_BROKER_URL%",
                  "live-tradingview-ohlc-topic",                 
                  //Protocol = BrokerProtocol.SaslSsl,
                  AuthenticationMode = BrokerAuthenticationMode.Plain,
                  ConsumerGroup = "live-ohlc-min-csv-converter")] string eventDataJson,
                FunctionContext context)
        {
            var logger = context.GetLogger("MinDataConverter");
            
            var eventDataValue = string.Empty;
            var JsonObj = JObject.Parse(eventDataJson);

            if (JsonObj != null && JsonObj["Value"] != null)
            {
                eventDataValue = JsonObj["Value"].ToString();
            }
            //var eventData = Json.Deserialize<TradingViewMinDataEvent>(eventDataValue);
            var eventData = JsonConvert.DeserializeObject<TradingViewMinDataEvent>(eventDataValue);

            if (eventData != null)
            {
                logger.LogInformation($"C# Kafka trigger function processing a message: {eventDataJson}");

                if (_date.Equals(DateTime.MinValue))
                {
                    _date = eventData != null ? eventData.Time : DateTime.MinValue;
                }

                // Blob Storage interaction
                var blobContainerClient = _blobServiceClient.GetBlobContainerClient("exchange-ohlc-container");

                // CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);
                // CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                // CloudBlobContainer container = blobClient.GetContainerReference("exchange-ohlc-container");

                string tickerName = eventData.Ticker.ToLower();
                string blobPath = string.Empty;
                string fileName = string.Empty;

                if (tickerName.Contains("nifty") && tickerName.Contains("bank"))
                {
                    if (tickerName.Contains("!"))
                    {
                        // bank nifty futures data
                        blobPath = "exchanges/nfo/futures/indices/";
                        fileName = GetFutureFileName("BANKNIFTY", _date);
                    }
                    else
                    {
                        // bank nifty index data
                        blobPath = "exchanges/nse/indices/";
                        fileName = "bank-nifty-live";
                    }
                }
                else if (tickerName.Contains("nifty") && !(tickerName.Contains("bank")))
                {
                    if (tickerName.Contains("!"))
                    {
                        // nifty futures data
                        blobPath = "exchanges/nfo/futures/indices/";
                        fileName = GetFutureFileName("NIFTY", _date);
                    }
                    else
                    {
                        // nifty index data
                        blobPath = "exchanges/nse/indices/";
                        fileName = "nifty-50-live";
                    }
                }
                string formattedDate = _date.ToString("yyyy/M/d");
                string completePathAndName = $"{blobPath}{formattedDate}/{fileName}.csv";
                var blobClient = blobContainerClient.GetBlobClient(completePathAndName);
                //CloudBlockBlob blob = container.GetBlockBlobReference(completePathAndName);

                // CSV Appending Logic
                bool isNewFile = !blobClient.ExistsAsync().Result; // Check if blob exists
                string existingContent = isNewFile ? "" : blobClient.DownloadContent().Value.Content.ToString();//.Result;

                using (var stream = new MemoryStream())
                using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true)) // 'true' for append
                {
                    if (isNewFile)
                    {
                        // Add CSV header if it's a new file 
                        writer.WriteLine("Date,Open,Low,High,Close,Volume");
                    }
                    else
                    {
                        writer.Write(existingContent);
                    }

                    // Assuming your modifiedData represents a single CSV row
                    var modifiedData = GetCSVCompatibleData(eventData);
                    writer.WriteLine(modifiedData);
                    writer.Flush();
                    stream.Position = 0; // Reset stream position
                    blobClient.UploadAsync(stream, overwrite: true).Wait();  // .UploadFromStreamAsync(stream).Wait();
                }

                logger.LogInformation("Processed Kafka event and appended to CSV in Blob.");   
            }
            
        }

        private string GetFutureFileName(string symbol, DateTime _date)
        {
            // Find the last Thursday of the _date's month 
            DateTime lastThursday = new DateTime(_date.Year, _date.Month, 1);
            while (lastThursday.DayOfWeek != DayOfWeek.Thursday)
            {
                lastThursday = lastThursday.AddDays(1);
            }
            lastThursday = lastThursday.AddDays(7 * (Math.Floor((DateTime.DaysInMonth(_date.Year, _date.Month) - lastThursday.Day) / 7.0)));

            if (_date > lastThursday)
            {
                // Date is after the last Thursday of the month
                int nextMonth = _date.Month % 12 + 1;
                int nextYear = nextMonth == 1 ? _date.Year + 1 : _date.Year;
                return symbol + nextYear.ToString().Substring(2) + _date.ToString("MMM").ToUpper() + "FUT";
            }
            else
            {
                // Date is before or on the last Thursday of the month
                return symbol + _date.ToString("yy") + _date.ToString("MMM").ToUpper() + "FUT";
            }
        }

        private string GetCSVCompatibleData(TradingViewMinDataEvent? eventData)
        {
            string returnData = string.Empty;

            if (eventData != null)
            {
                OHLCBlob ohlc = new OHLCBlob()
                {
                    Date = eventData.Time,
                    Open = eventData.Open,
                    High = eventData.High,
                    Low = eventData.Low,
                    Close = eventData.Close,
                    Volume = (int)eventData.Volume                  // setting it to 0 for now, until have volume available from trading view
                };

                returnData = string.Join(",",
                   ohlc.Date.ToString(),
                   ohlc.Open,
                   ohlc.High,
                   ohlc.Low,
                   ohlc.Close,
                   ohlc.Volume);
            }

            

            return returnData;
        }
    }
}
