using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SharedLibrary.Helpers;

namespace OHLCFunctionApp
{
    // "Does Azurite have enough history for this instrument?" — a reusable capability, not a
    // private step of any one caller. WarmUpService needs it first (WARMUP_AND_INDICATOR_PLAN.md
    // section 2d), but it's designed to be called by others later too: a StrategyService
    // deploy-time "insufficient history" warning, and Backtest's own date-range version of the
    // same question. Lives here because ohlc-live already owns the Azurite connection and
    // historical-query logic — no reason for callers to duplicate that.
    public class HistoricalSufficiency
    {
        private readonly ILogger<HistoricalSufficiency> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public HistoricalSufficiency(BlobServiceClient blobServiceClient, ILogger<HistoricalSufficiency> logger)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
        }

        /// <summary>
        /// Existence-only, checked against the monthly rollup blob (`{year}/{month}/{blobName}.csv`),
        /// not the day-level folders alongside it. Those day folders hold the current month's data
        /// broken out per day, but they're transient — purged once the month completes — while the
        /// same-named file directly under the month folder is cumulative for that whole month and
        /// is the permanent record. Checking day-level paths would silently read as "missing" for
        /// any completed month. This does not confirm the target day's data is actually *inside*
        /// the monthly file, only that the file exists — bar/date-level completeness within a
        /// month would be a separate, deeper (content-parsing) check if ever needed.
        ///
        /// Trading days = weekdays only, walking backward from the day before `asOf` (today's own
        /// data isn't warm-up history — it's what today's live session produces, so it's
        /// excluded). No holiday-calendar awareness: ohlc-live has no Redis dependency today, and
        /// adding one just for this would be new cross-service coupling. A holiday just shows up
        /// as "missing", which is technically true from Azurite's side, not a false positive.
        ///
        /// http GET http://localhost:8092/api/HistoricalSufficiency?exchange=nfo&instrumentName=BANKNIFTY&daysNeeded=20
        /// http GET http://localhost:8092/api/HistoricalSufficiency?exchange=nse&instrumentName=nifty-50&daysNeeded=20&asOf=2026-08-07
        /// </summary>
        [Function("HistoricalSufficiency")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            string? exchangeName = req.Query["exchange"];
            string? instrumentName = req.Query["instrumentName"];
            string? daysNeededRaw = req.Query["daysNeeded"];
            string? asOfRaw = req.Query["asOf"];

            if (string.IsNullOrEmpty(exchangeName) || string.IsNullOrEmpty(instrumentName) || string.IsNullOrEmpty(daysNeededRaw))
            {
                return new BadRequestObjectResult("Please provide all required query parameters: exchange, instrumentName, and daysNeeded.");
            }

            if (!int.TryParse(daysNeededRaw, out int daysNeeded) || daysNeeded <= 0)
            {
                return new BadRequestObjectResult("daysNeeded must be a positive integer.");
            }

            DateTime asOf;
            if (!string.IsNullOrEmpty(asOfRaw))
            {
                if (!DateTime.TryParse(asOfRaw, out asOf))
                {
                    return new BadRequestObjectResult("Invalid asOf date format. Use yyyy-MM-dd.");
                }
            }
            else
            {
                asOf = DateTimeHelper.GetCurrentIndianTime().Date;
            }

            string basePath = exchangeName.ToLower() == "nfo" ? "exchanges/nfo/futures/indices/" : "exchanges/nse/indices/";
            var container = _blobServiceClient.GetBlobContainerClient("exchange-ohlc-container");

            var days = new List<TradingDayAvailability>();
            var cursor = asOf.Date.AddDays(-1);
            int checkedCount = 0;

            // Safety valve — daysNeeded weekdays normally lands within ~1.4x calendar days
            // (5 weekdays per 7), so this is generous headroom against a pathological input
            // rather than a real expected bound.
            var oldestAllowed = asOf.Date.AddDays(-(daysNeeded * 3 + 30));

            // Per-month rollup, not per-day: the blob layout keeps a same-named file directly
            // under {year}/{month}/ containing every day of that month, cumulative and updated
            // live for the current month, permanent once the month completes — the day-level
            // folders alongside it are transient (current month only) and get purged after
            // month-end, so they can't be relied on for anything but the last few days. Every day
            // that falls in the same month resolves to the same blob; memoized so a lookback
            // window doesn't hit Azurite once per day for what's really one existence check.
            var monthExistsCache = new Dictionary<string, bool>();

            while (checkedCount < daysNeeded && cursor >= oldestAllowed)
            {
                if (cursor.DayOfWeek != DayOfWeek.Saturday && cursor.DayOfWeek != DayOfWeek.Sunday)
                {
                    // Recomputed per day, not once for `asOf` — an NFO futures contract name is
                    // month-specific (e.g. BANKNIFTY26AUGFUT), so a lookback window crossing a
                    // month boundary needs each day's own contract name, not one fixed name that
                    // would be silently wrong for the earlier month's days.
                    string blobName = GetBlobName(cursor, exchangeName, instrumentName);
                    string fullPath = $"{basePath}{cursor.Year}/{cursor.Month}/{blobName}.csv";

                    if (!monthExistsCache.TryGetValue(fullPath, out bool exists))
                    {
                        var blobClient = container.GetBlobClient(fullPath);
                        exists = await blobClient.ExistsAsync();
                        monthExistsCache[fullPath] = exists;
                    }

                    days.Add(new TradingDayAvailability(cursor.ToString("yyyy-MM-dd"), exists, fullPath));
                    checkedCount++;
                }

                cursor = cursor.AddDays(-1);
            }

            int availableCount = days.Count(d => d.Exists);
            int missingCount = days.Count - availableCount;

            _logger.LogInformation(
                $"HistoricalSufficiency {exchangeName}/{instrumentName}: needed {daysNeeded}, checked {days.Count}, " +
                $"available {availableCount}, missing {missingCount}, asOf {asOf:yyyy-MM-dd}");

            var response = new HistoricalSufficiencyResponse(
                Exchange: exchangeName,
                InstrumentName: instrumentName,
                DaysNeeded: daysNeeded,
                DaysChecked: days.Count,
                DaysAvailable: availableCount,
                DaysMissing: missingCount,
                Sufficient: missingCount == 0 && days.Count == daysNeeded,
                Days: days.OrderBy(d => d.Date).ToList());

            return new OkObjectResult(response);
        }

        // Same convention as GetOHLCDataByDate/GetOHLCByYearAndMonth's private copy — kept
        // separate rather than shared, matching how this project already duplicates it per file.
        private string GetBlobName(DateTime date, string exchangeName, string instrumentName)
        {
            if (string.IsNullOrEmpty(exchangeName) || string.IsNullOrEmpty(instrumentName))
            {
                return "Invalid input";
            }

            if (exchangeName.ToLower().Equals("nse"))
            {
                return instrumentName.ToLower().Contains("bank") && instrumentName.ToLower().Contains("nifty")
                    ? "bank-nifty"
                    : "nifty-50";
            }

            if (exchangeName.ToLower().Equals("nfo"))
            {
                string yearLastTwoDigits = date.ToString("yy");
                string month = date.ToString("MMM").ToUpper();

                return instrumentName.ToLower().Contains("bank") && instrumentName.ToLower().Contains("nifty")
                    ? $"BANKNIFTY{yearLastTwoDigits}{month}FUT"
                    : $"NIFTY{yearLastTwoDigits}{month}FUT";
            }

            return "Not Implemented";
        }
    }
}
