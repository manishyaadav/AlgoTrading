using WarmUpService.Ohlc;

namespace WarmUpService.Common
{
    public record HistoricalBar(DateTime WindowsStartTime, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

    public static class TimeframeBuilder
    {
        // Groups raw 1-min historical bars into N-min bars anchored to session open (9:15 IST) — the
        // exact same alignment AggregatorFunctions.Common.RunningBucket.FloorToBucketStart uses for
        // the live rollup. That match matters: an indicator seeded from bars built here has to
        // continue correctly the moment live candles take over, which only works if a historical bar
        // here lines up bucket-for-bucket with what the live pipeline would have produced. Duplicated
        // here rather than shared (WarmUpService and AggregationService don't share a project) — same
        // established pattern as BlobPathHelper.cs's duplication across services.
        //
        // Best-effort over whatever rows are actually present: if a bucket's underlying 1-min data has
        // a genuine gap (missing rows despite the CAS gap-fill and historical backfill), this still
        // builds a bar from whatever arrived rather than dropping the bucket — Open/Close come from
        // the first/last row actually present, not a fixed count. A bucket with materially incomplete
        // data will just be a slightly-off bar, not a missing one; there's no per-bucket completeness
        // signal surfaced back to the caller today.
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

        // Identical logic to AggregatorFunctions.Common.RunningBucket.FloorToBucketStart — see that
        // class for the full rationale (anchored to 9:15 market open, not the wall-clock hour).
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
