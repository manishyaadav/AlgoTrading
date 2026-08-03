using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace HolidayFunctionApp
{
    public class GetHolidays
    {
        private readonly ILogger<GetHolidays> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public GetHolidays(BlobServiceClient blobServiceClient, ILogger<GetHolidays> logger)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }

        [Function("GetHolidays")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("HTTP trigger function processed a request.");
            _logger.LogInformation("Query string: " + req.QueryString.Value);
            // Retrieve the blob name and year from query parameters
            string blobName = req.Query["blobName"];
            string year = req.Query["year"];

            if (blobName == "master" && string.IsNullOrEmpty(year))
            {
                return new BadRequestObjectResult("Year must be provided when requesting 'master' blob.");
            }

            // Construct the blob path based on the provided parameters
            string blobPath = (blobName == "master") ? $"holiday/holidaymaster.csv" : "holiday/current.csv";            

            var blobContainerClient = _blobServiceClient.GetBlobContainerClient("exchange-holiday-container");
            var blobClient = blobContainerClient.GetBlobClient(blobPath);

            if (!blobClient.Exists())
            {
                return new OkObjectResult("No Blob found!");
            }

            var response = blobClient.DownloadContent();
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

                        holidays.Add(new HolidayBlob
                        {
                            Holidays = columns[0],
                            Date = parsedDate
                        });
                    }
                }
            }

            int yearInt = int.TryParse(year, out int result) ? result : DateTime.Now.Year;            

            if (blobName.Equals("master"))
                holidays = holidays.Where(x => x.Date.Year == yearInt).ToList();

            return new OkObjectResult(holidays);
        }
    }
}
