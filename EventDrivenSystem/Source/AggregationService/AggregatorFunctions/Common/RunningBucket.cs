using System.Globalization;
using SharedLibrary.Helpers;
using StackExchange.Redis;

namespace AggregatorFunctions.Common
{
    // Persisted running OHLCV aggregate for one ticker/timeframe's in-progress bucket — the
    // Redis-backed replacement for the old in-memory static _bufferData/_bucket fields, so an
    // aggregation-live restart or crash mid-bucket doesn't lose the candles already folded into it.
    //
    // First candle in a bucket: Open/High/Low/Close/VolumeSum are seeded straight from it, Count = 1.
    // Every candle after that: High/Low widen (max/min), Close moves to the latest candle's Close,
    // VolumeSum accumulates, Count increments — until Count reaches the timeframe's candle quota,
    // at which point the bucket is complete and gets published + cleared.
    //
    // BucketStart/BucketEnd are IST throughout — in memory, in the Redis Hash, and in the published
    // TimeFrameAggregationEvent.WindowsStartTime this bucket eventually feeds. That's true from the
    // moment a candle enters this service: DataIngestionFunctions.CreateDataForIngestion converts
    // WindowsStartTime from the wire's UTC value to IST once, at the root, and every stage after that
    // (including this one) just carries the value forward — no further UTC<->IST conversion happens
    // anywhere in this class.
    public class RunningBucket
    {
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long VolumeSum { get; set; }
        public int Count { get; set; }
        public DateTime BucketStart { get; set; }
        public DateTime BucketEnd { get; set; }

        // Floors a candle's own (already-IST) event time down to a bucket boundary anchored to
        // market open (9:15 IST) on that trading day — not the top of the wall-clock hour. That
        // distinction only shows up for timeframes that don't evenly divide 60 relative to the hour
        // (10-min: 9:15 floored against the hour lands on 9:10, not the conventional 9:15; 60/75-min
        // don't divide the hour at all). Anchoring to 9:15 instead makes every timeframe's bucket
        // marks match the actual candle sequence a lower timeframe naturally produces (9:15, 9:25,
        // 9:35... for 10-min; 9:15, 10:30, 11:45... for 75-min), and keeps a restart's first-arriving
        // candle landing in the same bucket it would have without the restart, instead of drifting
        // onto an hour-relative mark.
        public static DateTime FloorToBucketStart(DateTime candleTimeIst, int timeframeMinutes)
        {
            var sessionOpen = new DateTime(candleTimeIst.Year, candleTimeIst.Month, candleTimeIst.Day, 9, 15, 0, candleTimeIst.Kind);

            double minutesSinceOpen = (candleTimeIst - sessionOpen).TotalMinutes;
            double remainder = minutesSinceOpen % timeframeMinutes;
            if (remainder < 0) remainder += timeframeMinutes; // normalize negative modulo (candle before 9:15)
            double flooredMinutesSinceOpen = minutesSinceOpen - remainder;

            return sessionOpen.AddMinutes(flooredMinutesSinceOpen);
        }

        // Returns null when there's no in-progress bucket for this key (nothing in Redis yet, or
        // the hash was cleared after the last bucket completed) — callers treat that the same as
        // "this is the first candle of a new bucket".
        public static RunningBucket? FromHash(HashEntry[] hash)
        {
            if (hash == null || hash.Length == 0) return null;

            var map = hash.ToDictionary(h => h.Name.ToString(), h => h.Value.ToString());
            if (!map.TryGetValue("Count", out var countRaw) || !int.TryParse(countRaw, out var count) || count <= 0)
                return null;

            return new RunningBucket
            {
                Open = ParseDecimal(map, "Open"),
                High = ParseDecimal(map, "High"),
                Low = ParseDecimal(map, "Low"),
                Close = ParseDecimal(map, "Close"),
                VolumeSum = map.TryGetValue("VolumeSum", out var vol) && long.TryParse(vol, out var v) ? v : 0,
                Count = count,
                BucketStart = ParseDate(map, "BucketStart"),
                BucketEnd = ParseDate(map, "BucketEnd"),
            };
        }

        private static decimal ParseDecimal(Dictionary<string, string> map, string field) =>
            map.TryGetValue(field, out var raw) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : 0m;

        // Values are already IST (see the class-level note) — plain parse/format, no timezone
        // conversion in either direction.
        private static DateTime ParseDate(Dictionary<string, string> map, string field) =>
            map.TryGetValue(field, out var raw) && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var val)
                ? val
                : DateTime.MinValue;

        public HashEntry[] ToHash() => new[]
        {
            new HashEntry("Open", Open.ToString(CultureInfo.InvariantCulture)),
            new HashEntry("High", High.ToString(CultureInfo.InvariantCulture)),
            new HashEntry("Low", Low.ToString(CultureInfo.InvariantCulture)),
            new HashEntry("Close", Close.ToString(CultureInfo.InvariantCulture)),
            new HashEntry("VolumeSum", VolumeSum.ToString(CultureInfo.InvariantCulture)),
            new HashEntry("Count", Count.ToString(CultureInfo.InvariantCulture)),
            new HashEntry("BucketStart", DateTimeHelper.ToIsoStringWithTime(BucketStart)),
            new HashEntry("BucketEnd", DateTimeHelper.ToIsoStringWithTime(BucketEnd)),
        };
    }
}
