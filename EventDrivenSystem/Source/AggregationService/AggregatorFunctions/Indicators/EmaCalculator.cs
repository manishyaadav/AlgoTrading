using System.Globalization;
using AggregatorFunctions.RedisConfig;
using StackExchange.Redis;

namespace AggregatorFunctions.Indicators
{
    // Live half of EMA's design (WARMUP_AND_INDICATOR_PLAN.md section 2e) — WarmUpService's
    // EmaSeeder computes the cold-start value from history; this applies exactly one recursive
    // update per completed N-min bar from here on. Standard formula, no open questions:
    //   multiplier = 2 / (Period + 1)
    //   EMA_today = (Close_today - EMA_yesterday) * multiplier + EMA_yesterday
    public static class EmaCalculator
    {
        // Returns null (a genuine no-op, not an error) if the Hash isn't there or isn't seeded yet —
        // a live candle alone can never seed EMA (needs Period closes' worth of history first), only
        // WarmUpService's cold-start path can. This just means today's warm-up hasn't seeded this
        // instance yet; the manifest listed it as active, but there's nothing to update.
        public static async Task<decimal?> UpdateAsync(RedisHelper redisHelper, string key, int period, decimal close, DateTime windowsStartTime, int ttlDays)
        {
            var hash = await redisHelper.GetHashAsync(key);
            if (hash.Length == 0) return null;

            var map = hash.ToDictionary(h => h.Name.ToString(), h => h.Value.ToString());
            if (!map.TryGetValue("IsSeeded", out var seededRaw) || seededRaw != "true") return null;
            if (!map.TryGetValue("LastEma", out var lastEmaRaw) ||
                !decimal.TryParse(lastEmaRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var lastEma))
                return null;

            decimal multiplier = 2m / (period + 1);
            decimal newEma = (close - lastEma) * multiplier + lastEma;

            await redisHelper.SetHashAsync(key, new HashEntry[]
            {
                new("LastEma", newEma.ToString(CultureInfo.InvariantCulture)),
                new("SeedBarsSeenSoFar", map.TryGetValue("SeedBarsSeenSoFar", out var seen) ? seen : period.ToString(CultureInfo.InvariantCulture)),
                new("IsSeeded", "true"),
                new("LastBarWindowsStartTime", windowsStartTime.ToString("yyyy-MM-ddTHH:mm:ss")),
            }, TimeSpan.FromDays(ttlDays));

            return newEma;
        }
    }
}
