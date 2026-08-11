using System;

namespace OHLCFunctionApp
{
    // Used by LiveCandlePersistenceFunction. The same logic also exists as a private copy in
    // GetOHLCByYearAndMonth/GetOHLCDataByDate (this branch was cut from main, before those got
    // consolidated on the still-unmerged 7-ohlc-historical-validation branch, which added the same
    // helper for HistoricalSufficiency) — expect a small merge conflict here when both land, worth
    // resolving by keeping this one shared version rather than re-duplicating.
    public static class BlobPathHelper
    {
        public static string GetBasePath(string exchangeName) =>
            exchangeName.ToLower() == "nfo" ? "exchanges/nfo/futures/indices/" : "exchanges/nse/indices/";

        // NFO's blob name is month-specific (a futures contract's own expiry label, e.g.
        // BANKNIFTY26AUGFUT) — always pass the date the row/check actually belongs to, not "today",
        // or a lookback/backfill spanning a month boundary will silently resolve to the wrong contract.
        public static string GetBlobName(DateTime date, string exchangeName, string instrumentName)
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

        public static string GetBlobPath(DateTime date, string exchangeName, string instrumentName) =>
            $"{GetBasePath(exchangeName)}{date.Year}/{date.Month}/{GetBlobName(date, exchangeName, instrumentName)}.csv";
    }
}
