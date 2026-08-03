using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
using TopicToCSVConverter.Events;
using TopicToCSVConverter.Models;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace TopicToCSVConverter
{
    public class TopicToJsonConvertorFunction
    {
        private readonly BlobServiceClient _blobServiceClient;
        private static DateTime _date = DateTime.MinValue;

        public TopicToJsonConvertorFunction(BlobServiceClient blobServiceClient)
        {            
            _blobServiceClient = blobServiceClient;
        }

        [Function("MinDataJsonConverter")]
        public async Task Run(
               [KafkaTrigger("%KAFKA_BROKER_URL%",
                  "live-ohlc-5min-aggregation-topic",                 // live-ohlc-5min-aggregation-topic,     live-tradingview-min-topic  
                  //Protocol = BrokerProtocol.SaslSsl,
                  AuthenticationMode = BrokerAuthenticationMode.Plain,
                  ConsumerGroup = "live-ohlc-min-json-converter1")] string eventDataJson,
                FunctionContext context)
        {
            var logger = context.GetLogger("MinDataJsonConverter");

            var eventDataValue = string.Empty;
            var JsonObj = JObject.Parse(eventDataJson);

            if (JsonObj != null && JsonObj["Value"] != null)
            {
                eventDataValue = JsonObj["Value"].ToString();
            }
            
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

                string tickerName = eventData.Ticker.ToLower();
                string derivedTickerName = string.Empty;

                string blobPath = "exchanges/";
                string fileName = "candles";

                if (tickerName.Contains("nifty") && tickerName.Contains("bank"))
                {
                    if (tickerName.Contains("!"))
                    {
                        // bank nifty futures data                        
                        derivedTickerName = GetFutureFileName("BANKNIFTY", _date);
                    }
                    else
                    {
                        // bank nifty index data
                        derivedTickerName = "NIFTYBANK";
                    }
                }
                else if (tickerName.Contains("nifty") && !(tickerName.Contains("bank")))
                {
                    if (tickerName.Contains("!"))
                    {
                        // nifty futures data                        
                        derivedTickerName = GetFutureFileName("NIFTY", _date);
                    }
                    else
                    {
                        // nifty index data
                        derivedTickerName = "NIFTY50";
                    }
                }

                string completePathAndName = $"{blobPath}{fileName}.json";
                eventData.Ticker = derivedTickerName;
                SaveJsonToBlobAsync(eventData, completePathAndName);

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


        private void SaveJsonToBlobAsync(TradingViewMinDataEvent newData, string pathAndName)
        {
            var blobContainerClient = _blobServiceClient.GetBlobContainerClient("exchange-ohlc-container");
            BlobClient blobClient = blobContainerClient.GetBlobClient(pathAndName);

            blobContainerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);
            
            // JSON Appending Logic
            bool isNewFile = !blobClient.ExistsAsync().Result; // Check if blob exists
            string existingContent = isNewFile ? "" : blobClient.DownloadContent().Value.Content.ToString();//.Result;

            JArray jsonArray = new JArray();

            if (isNewFile)
            {
                // New file, no existing content
                jsonArray = new JArray();
            }
            else
            {
                // Download the existing blob content                
                jsonArray = JArray.Parse(existingContent);
            }

            // Add new data to the JSON array
            jsonArray.Add(JObject.FromObject(newData));

            // Upload the updated content to the blob
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonArray.ToString())))
            {
                blobClient.UploadAsync(ms, overwrite: true);
            }

            Console.WriteLine("Data has been written to the blob");
        }
    }
}
