using System.Globalization;
using System.Text.Json;
using AggregatorFunctions.RedisConfig;
using StackExchange.Redis;

namespace AggregatorFunctions.Indicators
{
    // Live half of Supertrend's hybrid design (WARMUP_AND_INDICATOR_PLAN.md section 2e) —
    // WarmUpService's SupertrendSeeder computes the cold-start band/direction state and the initial
    // True-Range window from history; this applies exactly one step of the same band-ratchet logic
    // per completed N-min bar from here on, using the identical formula (see SupertrendSeeder.cs for
    // the full derivation and the same validation caveat — this hasn't been cross-checked against a
    // reference implementation with real data).
    public static class SupertrendCalculator
    {
        // Returns null (a genuine no-op) if the Hash isn't there or isn't seeded yet — same reasoning
        // as EmaCalculator: a live candle alone can't seed Supertrend, only WarmUpService can.
        public static async Task<(string Direction, decimal Value)?> UpdateAsync(
            RedisHelper redisHelper, string runningKey, string windowKey, int period, decimal multiplier,
            decimal high, decimal low, decimal close, DateTime windowsStartTime, int ttlDays)
        {
            var hash = await redisHelper.GetHashAsync(runningKey);
            if (hash.Length == 0) return null;

            var map = hash.ToDictionary(h => h.Name.ToString(), h => h.Value.ToString());
            if (!map.TryGetValue("IsSeeded", out var seededRaw) || seededRaw != "true") return null;

            decimal prevClose = ParseDecimal(map, "PrevClose");
            decimal prevUpper = ParseDecimal(map, "PrevUpperBand");
            decimal prevLower = ParseDecimal(map, "PrevLowerBand");
            string prevDirection = map.TryGetValue("TrendDirection", out var dir) ? dir : "Up";

            decimal tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));

            // Roll the True-Range window forward (RPUSH+LTRIM, same PushToListAsync the 20-period
            // candle-stats window already uses), then recompute the plain-average ATR from it — not
            // an incremental running average, so the window stays the single source of truth for ATR
            // rather than two numbers that could drift apart.
            string entryJson = JsonSerializer.Serialize(new { WindowsStartTime = windowsStartTime, TrueRange = tr, High = high, Low = low, Close = close });
            await redisHelper.PushToListAsync(windowKey, entryJson, period, TimeSpan.FromDays(ttlDays));
            var windowValues = await redisHelper.GetListAsync(windowKey);
            decimal atr = windowValues.Length > 0 ? windowValues.Select(v => ExtractTrueRange((string)v!)).Average() : tr;

            decimal mid = (high + low) / 2m;
            decimal basicUpper = mid + multiplier * atr;
            decimal basicLower = mid - multiplier * atr;

            decimal finalUpper = (basicUpper < prevUpper || prevClose > prevUpper) ? basicUpper : prevUpper;
            decimal finalLower = (basicLower > prevLower || prevClose < prevLower) ? basicLower : prevLower;

            bool trackingUpper = prevDirection == "Down";
            bool nowTrackingUpper =
                trackingUpper && close <= finalUpper ? true :
                trackingUpper && close > finalUpper ? false :
                !trackingUpper && close >= finalLower ? false :
                true;

            string direction = nowTrackingUpper ? "Down" : "Up";
            decimal value = nowTrackingUpper ? finalUpper : finalLower;

            await redisHelper.SetHashAsync(runningKey, new HashEntry[]
            {
                new("TrendDirection", direction),
                new("PrevUpperBand", finalUpper.ToString(CultureInfo.InvariantCulture)),
                new("PrevLowerBand", finalLower.ToString(CultureInfo.InvariantCulture)),
                new("PrevClose", close.ToString(CultureInfo.InvariantCulture)),
                new("Atr", atr.ToString(CultureInfo.InvariantCulture)),
                new("IsSeeded", "true"),
                new("LastBarWindowsStartTime", windowsStartTime.ToString("yyyy-MM-ddTHH:mm:ss")),
            }, TimeSpan.FromDays(ttlDays));

            return (direction, value);
        }

        private static decimal ExtractTrueRange(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("TrueRange").GetDecimal();
        }

        private static decimal ParseDecimal(Dictionary<string, string> map, string field) =>
            map.TryGetValue(field, out var raw) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : 0m;
    }
}
