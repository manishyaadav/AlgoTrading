using System.Globalization;
using System.Text.Json;
using AggregatorFunctions.RedisConfig;
using StackExchange.Redis;

namespace AggregatorFunctions.Indicators
{
    // Live half of Adaptive Supertrend (WarmUpService/Indicators/AdaptiveSupertrendSeeder.cs computes
    // the cold-start state and the initial rolling raw-ATR window from history; this applies exactly
    // one step of the same Wilder-ATR update + K-Means volatility reclassification + band-ratchet per
    // completed N-min bar from here on). See the seeder for the full algorithm writeup and the same
    // "not cross-checked against a reference implementation" caveat.
    public static class AdaptiveSupertrendCalculator
    {
        // Returns null (a genuine no-op) if the Hash isn't there or isn't seeded yet — same reasoning
        // as SupertrendCalculator: a live candle alone can't seed this, only WarmUpService can (it
        // needs atrLength + 100 bars of history just to fill the K-Means training window once).
        //
        // Returns PrevDirection/PrevValue alongside the new ones, same rationale as
        // SupertrendCalculator's own version of this change — both already in scope, no second read.
        public static async Task<(string Direction, decimal Value, string PrevDirection, decimal PrevValue)?> UpdateAsync(
            RedisHelper redisHelper, string runningKey, string windowKey, int atrLength, decimal factor,
            decimal high, decimal low, decimal close, DateTime windowsStartTime, int ttlDays)
        {
            var hash = await redisHelper.GetHashAsync(runningKey);
            if (hash.Length == 0) return null;

            var map = hash.ToDictionary(h => h.Name.ToString(), h => h.Value.ToString());
            if (!map.TryGetValue("IsSeeded", out var seededRaw) || seededRaw != "true") return null;

            decimal prevClose = ParseDecimal(map, "PrevClose");
            decimal prevUpper = ParseDecimal(map, "PrevUpperBand");
            decimal prevLower = ParseDecimal(map, "PrevLowerBand");
            decimal prevRawAtr = ParseDecimal(map, "RawAtr");
            string prevDirection = map.TryGetValue("TrendDirection", out var dir) ? dir : "Up";

            decimal tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));

            // Wilder/RMA-smoothed ATR — one recursive step continuing from the last persisted raw
            // ATR. Deliberately different from regular Supertrend's plain-average ATR; see the
            // seeder's comment on why the two indicators intentionally use different volatility
            // inputs. prevRawAtr == 0 only happens if RawAtr somehow wasn't persisted despite
            // IsSeeded being true — falls back to the bare TR rather than dividing by a zero history.
            decimal rawAtr = prevRawAtr == 0 ? tr : (prevRawAtr * (atrLength - 1) + tr) / atrLength;

            // Roll the raw-ATR window forward (RPUSH+LTRIM to the K-Means training length, same
            // mechanism the True-Range window already uses for regular Supertrend), then re-run the
            // full clustering against it — the Pine source re-clusters from scratch on every bar too,
            // not an incremental update to the previous assignment.
            string entryJson = JsonSerializer.Serialize(new { WindowsStartTime = windowsStartTime, Atr = rawAtr });
            await redisHelper.PushToListAsync(windowKey, entryJson, AdaptiveVolatilityClusterer.TrainingWindow, TimeSpan.FromDays(ttlDays));
            var windowValues = await redisHelper.GetListAsync(windowKey);
            var window = windowValues.Length > 0 ? windowValues.Select(v => ExtractAtr((string)v!)).ToList() : new List<decimal> { rawAtr };

            var assignment = AdaptiveVolatilityClusterer.Assign(window, rawAtr);
            decimal centroidAtr = assignment.AssignedCentroid;

            decimal mid = (high + low) / 2m;
            decimal basicUpper = mid + factor * centroidAtr;
            decimal basicLower = mid - factor * centroidAtr;

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
            decimal prevValue = prevDirection == "Down" ? prevUpper : prevLower;

            await redisHelper.SetHashAsync(runningKey, new HashEntry[]
            {
                new("TrendDirection", direction),
                new("PrevUpperBand", finalUpper.ToString(CultureInfo.InvariantCulture)),
                new("PrevLowerBand", finalLower.ToString(CultureInfo.InvariantCulture)),
                new("PrevClose", close.ToString(CultureInfo.InvariantCulture)),
                new("Atr", centroidAtr.ToString(CultureInfo.InvariantCulture)),
                new("RawAtr", rawAtr.ToString(CultureInfo.InvariantCulture)),
                new("VolatilityCluster", assignment.Cluster),
                new("ClusterHigh", assignment.HighCentroid.ToString(CultureInfo.InvariantCulture)),
                new("ClusterMedium", assignment.MediumCentroid.ToString(CultureInfo.InvariantCulture)),
                new("ClusterLow", assignment.LowCentroid.ToString(CultureInfo.InvariantCulture)),
                new("IsSeeded", "true"),
                new("LastBarWindowsStartTime", windowsStartTime.ToString("yyyy-MM-ddTHH:mm:ss")),
            }, TimeSpan.FromDays(ttlDays));

            return (direction, value, prevDirection, prevValue);
        }

        private static decimal ExtractAtr(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("Atr").GetDecimal();
        }

        private static decimal ParseDecimal(Dictionary<string, string> map, string field) =>
            map.TryGetValue(field, out var raw) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : 0m;
    }
}
