using Azure.Storage.Blobs;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OHLCFunctionApp.Persistence;
using SharedLibrary.Enums.Exchange;
using SharedLibrary.Events.Exchange;
using SharedLibrary.Helpers;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace OHLCFunctionApp
{
    // Reacts to NSE's Close (15:30 IST) — deliberately NSE only, not also NFO, even though NFO's
    // own Close event fires on the same topic seconds later, because this function's body already
    // merges both nse and nfo daily files in one pass per invocation. Reacting to both ExchangeName
    // values would double-merge every contract.
    //
    // For each of the same 4 (exchange, ticker) combos DailyFileWarmUpFunction pre-creates, reads
    // today's daily file's data rows and merges them into the monthly file in one round trip via
    // MergeReplacingDateAsync — not one AppendAsync call per row, which would mean ~375 sequential
    // whole-file re-uploads against the (already large, cumulative) monthly blob.
    //
    // The merge is idempotent by date: MergeReplacingDateAsync drops any existing monthly rows for
    // the date being merged before appending the fresh ones at the end, so a redelivered Close event
    // (or a manual re-trigger) re-merging the same day just replaces that day's rows in place instead
    // of doubling them. The daily file itself is left in place after a successful merge — purging it
    // is a separate, not-yet-implemented concern (see this project's README).
    public class DailyToMonthlyMergeFunction
    {
        private readonly ILogger<DailyToMonthlyMergeFunction> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IBlobAppendStrategy _appendStrategy;

        private static readonly string[] Exchanges = { "nse", "nfo" };
        private static readonly string[] Tickers = { "NIFTY", "BANKNIFTY" };

        public DailyToMonthlyMergeFunction(BlobServiceClient blobServiceClient, IBlobAppendStrategy appendStrategy, ILogger<DailyToMonthlyMergeFunction> logger)
        {
            _blobServiceClient = blobServiceClient;
            _appendStrategy = appendStrategy;
            _logger = logger;
        }

        [Function("DailyToMonthlyMergeFunction")]
        public async Task Run(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-exchange-workflow-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "live-ohlc-daily-monthly-merge-consumer")] string eventDataJson,
            FunctionContext context)
        {
            var eventDataValue = string.Empty;
            var jsonObj = JObject.Parse(eventDataJson);
            if (jsonObj?["Value"] != null)
                eventDataValue = jsonObj["Value"]?.ToString() ?? string.Empty;

            var exchangeEvent = JsonConvert.DeserializeObject<ExchangeEvent>(eventDataValue);
            if (exchangeEvent == null)
            {
                _logger.LogWarning($"DailyToMonthlyMergeFunction: could not deserialize exchange event: {eventDataValue}");
                return;
            }

            if (!string.Equals(exchangeEvent.ExchangeName, "NSE", StringComparison.OrdinalIgnoreCase) ||
                exchangeEvent.ExchangeTimerAction != ExchangeActionEnum.Close)
            {
                _logger.LogInformation($"DailyToMonthlyMergeFunction: ignoring {exchangeEvent.ExchangeName} {exchangeEvent.ExchangeTimerAction}");
                return;
            }

            DateTime date = ResolveDate(exchangeEvent.Date);
            var container = _blobServiceClient.GetBlobContainerClient("exchange-ohlc-container");

            foreach (var exchange in Exchanges)
            {
                foreach (var ticker in Tickers)
                {
                    string dailyPath = BlobPathHelper.GetDailyBlobPath(date, exchange, ticker);
                    string monthlyPath = BlobPathHelper.GetBlobPath(date, exchange, ticker);
                    try
                    {
                        var dailyBlobClient = container.GetBlobClient(dailyPath);
                        if (!await dailyBlobClient.ExistsAsync())
                        {
                            _logger.LogWarning($"DailyToMonthlyMergeFunction: no daily file at {dailyPath} for {date:yyyy-MM-dd}, nothing to merge.");
                            continue;
                        }

                        var download = await dailyBlobClient.DownloadContentAsync();
                        string content = download.Value.Content.ToString();
                        var dataRows = content
                            .Split('\n')
                            .Select(l => l.TrimEnd('\r'))
                            .Where(l => l.Length > 0)
                            .Skip(1) // header row
                            .ToList();

                        if (dataRows.Count == 0)
                        {
                            // Deliberately don't call MergeReplacingDateAsync here — an empty daily
                            // file means something upstream went wrong, not "this date has zero
                            // rows"; wiping monthly's existing rows for the date on the strength of
                            // an empty daily file would be a real data-loss risk, not a safety net.
                            _logger.LogInformation($"DailyToMonthlyMergeFunction: {dailyPath} has no data rows, skipping merge.");
                            continue;
                        }

                        string datePrefix = date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
                        await _appendStrategy.MergeReplacingDateAsync(container, monthlyPath, BlobPathHelper.HeaderLine, datePrefix, dataRows, _logger);
                        _logger.LogInformation($"DailyToMonthlyMergeFunction: merged {dataRows.Count} row(s) from {dailyPath} into {monthlyPath} (date {datePrefix})");
                    }
                    catch (Exception ex)
                    {
                        // One combo failing shouldn't block the other 3.
                        _logger.LogError(ex, $"DailyToMonthlyMergeFunction: failed to merge {dailyPath} into {monthlyPath}");
                    }
                }
            }
        }

        private static DateTime ResolveDate(string rawDate)
        {
            if (DateTime.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
            return DateTimeHelper.GetCurrentIndianTime().Date;
        }
    }
}
