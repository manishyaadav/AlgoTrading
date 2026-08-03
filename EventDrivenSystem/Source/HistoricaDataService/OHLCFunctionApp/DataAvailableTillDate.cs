using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace OHLCFunctionApp
{
    public class DataAvailableTill
    {
        private readonly ILogger<DataAvailableTill> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public DataAvailableTill(BlobServiceClient blobServiceClient, ILogger<DataAvailableTill> logger)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }

        [Function("DataAvailableTill")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {            
            string[] paths = { "exchanges/nfo/futures/indices/", "exchanges/nse/indices/" };
            var latestDates = new Dictionary<string, (DateTime Date, string Path)>();

            foreach (var path in paths)
            {
                var container = _blobServiceClient.GetBlobContainerClient("exchange-ohlc-container");
                var (latestDate, latestPath) = await FindLatestDate(container, path, _logger);
                latestDates[path] = (latestDate, latestPath);
            }

            var results = new List<object>();
            foreach (var entry in latestDates)
            {
                string exchangeName = GetExchangeNameFromPath(entry.Key);
                DateTime maxDate = entry.Value.Date;
                string maxDatePath = entry.Value.Path;

                _logger.LogWarning($"Max Date Path: {maxDatePath}");

                var container = _blobServiceClient.GetBlobContainerClient("exchange-ohlc-container");
                List<string> blobNames = new List<string>();
                await foreach (var blob in container.GetBlobsAsync(prefix: maxDatePath))
                {
                    blobNames.Add(blob.Name);
                }

                results.Add(new
                {
                    ExchangeName = exchangeName,
                    MaxAvailableDate = maxDate.ToString("yyyy-MM-dd"),
                    BlobNames = blobNames
                });
            }

            return new OkObjectResult(results);
        }

        private static async Task<(DateTime, string)> FindLatestDate(BlobContainerClient container, string basePath, ILogger log, string currentPath = "")
        {
            DateTime maxDate = DateTime.MinValue;
            string maxDatePath = null;

            // Construct the full path for the current recursion level correctly
            string fullPath = string.IsNullOrEmpty(currentPath) ? basePath : basePath.TrimEnd('/') + '/' + currentPath.TrimStart('/');

            //log.LogInformation($"Exploring: {fullPath}");  // Debugging output

            await foreach (var blobItem in container.GetBlobsByHierarchyAsync(prefix: fullPath, delimiter: "/"))
            {
                if (blobItem.IsPrefix)
                {
                    // Extract the relative path from the current prefix to avoid reappending base path segments
                    string relativePath = blobItem.Prefix.Substring(fullPath.Length).Trim('/');

                    // Recursive call
                    var (foundDate, foundPath) = await FindLatestDate(container, basePath, log, fullPath.Substring(basePath.Length).Trim('/') + "/" + relativePath);

                    if (foundDate > maxDate)
                    {
                        maxDate = foundDate;
                        maxDatePath = foundPath;  // Directly use the returned path
                    }
                }
                else
                {
                    // Parse the date from the current path segment
                    string dateSegment = fullPath.Substring(basePath.Length).Trim('/');
                    if (DateTime.TryParseExact(dateSegment, "yyyy/M/d", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                    {
                        if (parsedDate > maxDate)
                        {
                            maxDate = parsedDate;
                            maxDatePath = fullPath;  // Set maxDatePath to the current full path
                        }
                    }
                }
            }

            return (maxDate, maxDatePath);
        }

        static string GetExchangeNameFromPath(string path)
        {
            var segments = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 1 && segments[0].Equals("exchanges", StringComparison.OrdinalIgnoreCase))
            {
                return segments[1];
            }
            return "Unknown";
        }
    }
}
