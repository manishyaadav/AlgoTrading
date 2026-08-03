using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CsvHelper;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using System.Globalization;
using System.Text.Json;

namespace OHLCFunctionApp
{
    public class GetOHLCByYearAndMonth
    {
        private readonly ILogger<GetOHLCByYearAndMonth> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public GetOHLCByYearAndMonth(BlobServiceClient blobServiceClient, ILogger<GetOHLCByYearAndMonth> logger)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }

        /// <summary>
        /// Retrieves Open, High, Low, Close (OHLC) data for a specified financial instrument, exchange, month, and year from a CSV file stored in an Azure Blob container.
        /// </summary>
        /// <param name="req">
        /// year (Required): The year of the OHLC data (e.g., "2024").
        /// month(Required) : The month of the OHLC data(e.g., "04" for April).
        /// exchange(Required) : The name of the exchange(e.g., "nse", "nfo").
        /// instrumentName(Required) : The name of the financial instrument(e.g., "nifty-50").
        /// </param>
        /// <returns></returns>
        [Function("GetOHLCByYearAndMonth")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            string year = req.Query["year"];
            string month = req.Query["month"];
            string exchangeName = req.Query["exchange"];
            string instrumentName = req.Query["instrumentName"];

            DateTime dt = new DateTime(int.Parse(year), int.Parse(month), 1);
            

            string basePath = exchangeName.ToLower() == "nfo" ? "exchanges/nfo/futures/indices/" : "exchanges/nse/indices/";
            string blobName = GetBlobName(dt, exchangeName, instrumentName);
            string fullPath = $"{basePath}{dt.Year}/{dt.Month}/{blobName}.csv";


            _logger.LogInformation($"Full Path: {fullPath}");
            var container = _blobServiceClient.GetBlobContainerClient("exchange-ohlc-container");
            var blobClient = container.GetBlobClient(fullPath);

            if (!await blobClient.ExistsAsync())
            {
                return new NotFoundObjectResult("Blob not found.");
            }

            var download = blobClient.DownloadContent();
            var csvContent = download.Value.Content.ToString();

            List<OHLCResponse> ohlcRecords = new List<OHLCResponse>();
            using (var reader = new StringReader(csvContent))
            {
                string line;
                bool isFirstLine = true;        // Assuming thie first line is header

                while ((line = reader.ReadLine()) != null)
                {
                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        continue;  // Skip header line
                    }
                    var columns = line.Split(',');
                    if (columns.Length == 6)
                    {
                        _logger.LogInformation($"{blobName}, {columns[0]}");
                        ohlcRecords.Add(new OHLCResponse
                        {                            
                            ContractName = blobName,
                            Timeframe = 1,
                            Date = DateTime.ParseExact(columns[0], "dd-MM-yyyy HH:mm:ss", new CultureInfo("en-IN")),
                            Open = Double.Parse(columns[1]),
                            Low = Double.Parse(columns[2]),
                            High = Double.Parse(columns[3]),
                            Close = Double.Parse(columns[4]),
                            Volume = Int32.Parse(columns[5]),
                        });
                    }
                }
            }

            //string responseMessage = $"Received Query Parameters:\nDate: {dt}\nExchange Name: {exchangeName}\nBlobName: {blobName}\nFullPath: {fullPath}";

            var filteredRecords = ohlcRecords.ToList();

            var response = new
            {
                TotalRecords = filteredRecords.Count,
                FullPath = fullPath,
                Recods = filteredRecords
            };

            return new OkObjectResult(response);
        }

        private static DateTime ParseDate(string dateInput)
        {
            string[] formats = { "dd-MM-yyyy HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "MM/dd/yyyy HH:mm:ss" }; // Add more formats if needed
            if (DateTime.TryParseExact(dateInput, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate;
            }

            Console.WriteLine($"Failed to parse date: {dateInput}");
            // Handle the error: You can throw an exception or return a default date
            return DateTime.MinValue; // Return a default value or throw exception if necessary
        }

        private string GetBlobName(DateTime date, string exchangeName, string instrumentName)
        {
            if (string.IsNullOrEmpty(exchangeName) || string.IsNullOrEmpty(instrumentName))
            {
                return "Invalid input";
            }

            string blobName = string.Empty;

            if (exchangeName.ToLower().Equals("nse"))
            {
                blobName = instrumentName.ToLower().Contains("bank") && instrumentName.ToLower().Contains("nifty") ? "bank-nifty" : "nifty-50";
            }
            else if (exchangeName.ToLower().Equals("nfo"))
            {
                string yearLastTwoDigits = date.ToString("yy"); // Gets the last two digits of the year
                string month = date.ToString("MMM").ToUpper(); // Gets the abbreviated month name in uppercase

                blobName = instrumentName.ToLower().Contains("bank") && instrumentName.ToLower().Contains("nifty") ? $"BANKNIFTY{yearLastTwoDigits}{month}FUT" : $"NIFTY{yearLastTwoDigits}{month}FUT";
            }
            else
            {
                blobName = "Not Implemented";
            }



            return blobName;
        }


    }
}
