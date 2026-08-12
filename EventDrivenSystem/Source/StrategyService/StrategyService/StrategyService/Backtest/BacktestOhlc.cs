using System.Text.Json.Serialization;

namespace StrategyService.Backtest
{
    // Mirrors WarmUpService/Ohlc/OhlcServiceClient.cs's shapes field-for-field, including the
    // "Recods" typo in ohlc-live's real response — no shared project reference exists between
    // WarmUpService and StrategyService, same established duplication as everywhere else two
    // services both need to talk to ohlc-live.
    public record RawCandle(
        [property: JsonPropertyName("ContractName")] string ContractName,
        [property: JsonPropertyName("Timeframe")] int Timeframe,
        [property: JsonPropertyName("Date")] DateTime Date,
        [property: JsonPropertyName("Open")] double Open,
        [property: JsonPropertyName("Low")] double Low,
        [property: JsonPropertyName("High")] double High,
        [property: JsonPropertyName("Close")] double Close,
        [property: JsonPropertyName("Volume")] int Volume);

    public record OhlcMonthResponse(
        [property: JsonPropertyName("TotalRecords")] int TotalRecords,
        [property: JsonPropertyName("FullPath")] string FullPath,
        [property: JsonPropertyName("Recods")] List<RawCandle> Recods);

    public record TradingDayAvailability(string Date, bool Exists, string Path);

    public record HistoricalSufficiencyResponse(
        string Exchange, string InstrumentName, int DaysNeeded, int DaysChecked,
        int DaysAvailable, int DaysMissing, bool Sufficient, List<TradingDayAvailability> Days);

    public record HistoricalBar(DateTime WindowsStartTime, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

    // Mirrors WarmUpService/Common/TimeframeBuilder.cs exactly — see that file for the full
    // rationale (anchored to 9:15 session open, matching AggregationService's live bucketing, so a
    // backtest's bars line up bucket-for-bucket with what the live pipeline would have produced).
    public static class TimeframeBuilder
    {
        public static List<HistoricalBar> Build(IEnumerable<RawCandle> oneMinuteBars, int timeframeMinutes)
        {
            var ordered = oneMinuteBars.OrderBy(b => b.Date).ToList();

            if (timeframeMinutes <= 1)
                return ordered.Select(b => new HistoricalBar(b.Date, (decimal)b.Open, (decimal)b.High, (decimal)b.Low, (decimal)b.Close, b.Volume)).ToList();

            return ordered
                .GroupBy(b => FloorToBucketStart(b.Date, timeframeMinutes))
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var bucketRows = g.OrderBy(b => b.Date).ToList();
                    return new HistoricalBar(
                        g.Key,
                        (decimal)bucketRows[0].Open,
                        (decimal)bucketRows.Max(b => b.High),
                        (decimal)bucketRows.Min(b => b.Low),
                        (decimal)bucketRows[^1].Close,
                        bucketRows.Sum(b => (long)b.Volume));
                })
                .ToList();
        }

        public static DateTime FloorToBucketStart(DateTime candleTimeIst, int timeframeMinutes)
        {
            var sessionOpen = new DateTime(candleTimeIst.Year, candleTimeIst.Month, candleTimeIst.Day, 9, 15, 0, candleTimeIst.Kind);

            double minutesSinceOpen = (candleTimeIst - sessionOpen).TotalMinutes;
            double remainder = minutesSinceOpen % timeframeMinutes;
            if (remainder < 0) remainder += timeframeMinutes;

            return sessionOpen.AddMinutes(minutesSinceOpen - remainder);
        }
    }
}
