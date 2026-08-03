using Azure.Storage.Blobs;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace HolidayFunctionApp
{
    public class IsExchangeHoliday
    {
        private readonly ILogger<IsExchangeHoliday> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public IsExchangeHoliday(BlobServiceClient blobServiceClient, ILogger<IsExchangeHoliday> logger)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }

        [Function("IsExchangeHoliday")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req,
            string exchangename)
        {
            _logger.LogInformation("HTTP trigger function processed a request.");

            string storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";

            var blobContainerClient = _blobServiceClient.GetBlobContainerClient("exchange-holiday-container");
            var blobClient = blobContainerClient.GetBlobClient("holiday/current.csv");

            if (!blobClient.Exists())
            {
                return new OkObjectResult("No Blob found!");
            }

            var response = blobClient.DownloadContent();


            _logger.LogInformation("C# HTTP trigger function processed a request.");
            var csvContent = response.Value.Content.ToString();

            List<HolidayBlob> holidays = new List<HolidayBlob>();

            using (var reader = new StringReader(csvContent))
            {
                string line;
                bool isFirstLine = true;  // Assuming the first line is headers
                while ((line = reader.ReadLine()) != null)
                {
                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        continue;  // Skip header line
                    }
                    var columns = line.Split(',');
                    if (columns.Length >= 2)
                    {
                        DateTime parsedDate;

                        if (DateTime.TryParseExact(columns[1], "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                        {                            
                        }
                        else
                        {
                            // Parsing failed
                            Console.WriteLine("Date parsing failed.");
                        }

                        var holiday = new HolidayBlob
                        {
                            Holidays = columns[0],  // Description is the first column
                            Date = parsedDate  // Date is the second column
                        };
                        holidays.Add(holiday);
                    }
                }
            }

            var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).Date;
            var todaysHoliday = holidays.FirstOrDefault(h => h.Date.Date == today);
            var todayResponse = new HolidayResponse();

            if (todaysHoliday != null)
            {
                todayResponse.IsHoliday = true;
                todayResponse.Reason = todaysHoliday.Holidays;
            }
            else if (today.DayOfWeek == DayOfWeek.Saturday)
            {
                todayResponse.IsHoliday = true;
                todayResponse.Reason = "It's Saturday.";
            }
            else if (today.DayOfWeek == DayOfWeek.Sunday)
            {
                todayResponse.IsHoliday = true;
                todayResponse.Reason = "It's Sunday.";
            }
            else
            {
                todayResponse.IsHoliday = false;
                todayResponse.Reason = "Today is not a holiday.";
            }

            return new OkObjectResult(todayResponse);
        }
    }
}
