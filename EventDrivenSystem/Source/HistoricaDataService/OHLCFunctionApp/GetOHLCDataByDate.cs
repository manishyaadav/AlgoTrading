using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CsvHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace OHLCFunctionApp
{
    public class GetOHLCDataByDate
    {
        private readonly ILogger<GetOHLCDataByDate> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public GetOHLCDataByDate(BlobServiceClient blobServiceClient, ILogger<GetOHLCDataByDate> logger)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }
        

        /// <summary>
        /// http GET http://localhost:7071/api/GetOHLCDataByDate date==2024-02-2 exchange==nse instrumentName==nifty-50
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        [Function("GetOHLCDataByDate")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            string date = req.Query["date"];
            string exchangeName = req.Query["exchange"];
            string instrumentName = req.Query["instrumentName"];

            // Check if all required parameters are provided
            if (string.IsNullOrEmpty(date) || string.IsNullOrEmpty(exchangeName) || string.IsNullOrEmpty(instrumentName))
            {
                return new BadRequestObjectResult("Please provide all required query parameters: date, exchange, and instrumentName.");
            }

            DateTime dt;
            if (!DateTime.TryParse(date, out dt))
            {
                return new BadRequestObjectResult("Invalid date format. Please use a valid date.");
            }

            string basePath = exchangeName.ToLower() == "nfo" ? "exchanges/nfo/futures/indices/" : "exchanges/nse/indices/";
            string blobName = GetBlobName(dt, exchangeName, instrumentName);
            string fullPath = $"{basePath}{dt.Year}/{dt.Month}/{blobName}.csv";


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
                        DateTime parsedDate;

                        if (DateTime.TryParseExact(columns[0], "dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                        {
                        }
                        else
                        {
                            // Parsing failed
                            Console.WriteLine("Date parsing failed.");
                        }

                        ohlcRecords.Add(new OHLCResponse
                        {
                            ContractName = blobName,
                            Timeframe = 1,
                            Date = parsedDate,
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

            var filteredRecords = ohlcRecords.Where(x => x.Date.Date == dt.Date).ToList();

            var response = new
            {
                TotalRecords = filteredRecords.Count,
                FullPath = fullPath,
                Recods = filteredRecords
            };

            return new OkObjectResult(response);
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
