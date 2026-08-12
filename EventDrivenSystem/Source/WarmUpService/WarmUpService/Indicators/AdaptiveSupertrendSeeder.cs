using WarmUpService.Common;

namespace WarmUpService.Indicators
{
    // { TrendDirection, PrevUpperBand, PrevLowerBand, PrevClose, Atr, IsSeeded } — kept
    // field-name-compatible with SupertrendState on purpose, so StrategyService's RuleEvaluator can
    // resolve this indicator with (nearly) the same code path as regular Supertrend. Atr here is the
    // K-Means-ASSIGNED volatility centroid actually driving the bands, not the raw measured ATR —
    // RawAtr carries the latter, purely for transparency (what was actually measured vs. what the
    // bands are using), and VolatilityCluster/ClusterHigh/Medium/Low expose the K-Means state behind
    // that choice.
    public record AdaptiveSupertrendState(
        string TrendDirection, decimal PrevUpperBand, decimal PrevLowerBand, decimal PrevClose,
        decimal Atr, decimal RawAtr, string VolatilityCluster,
        decimal ClusterHigh, decimal ClusterMedium, decimal ClusterLow,
        bool IsSeeded, DateTime? LastBarWindowsStartTime);

    // One entry in the rolling raw-ATR window (the K-Means training set, the "List" half of this
    // indicator's hybrid persistence) — mirrors TrueRangeEntry's role for regular Supertrend, just
    // windowing raw ATR instead of raw True Range.
    public record AtrWindowEntry(DateTime WindowsStartTime, decimal Atr);

    // Ports the "Adaptive SuperTrend — Trend + Retracement Framework" Pine Script's core calculation
    // — a K-Means-clustered ATR (see AdaptiveVolatilityClusterer) feeding the exact same band-ratchet
    // formula regular Supertrend already uses (compare against SupertrendSeeder.cs: the ratchet math
    // is identical, only the ATR input differs). Duplicated rather than shared, same as EMA/
    // Supertrend are already duplicated between WarmUpService and AggregationService — no shared
    // project reference exists between any of these services.
    //
    // Execution/risk/display/alert logic from the source strategy (trade direction, position sizing,
    // EMA filter, penetration/retracement lines, the on-chart table) is deliberately NOT ported —
    // out of scope for "the calculation, like other indicators": this produces the same
    // (TrendDirection, band value) shape EMA/Supertrend already do, nothing that trades or draws.
    //
    // ⚠️ Not cross-checked against a reference implementation with real data — same caveat
    // SupertrendSeeder.cs carries, compounded here by the K-Means step being genuinely new territory
    // for this codebase. Validate before trusting for live trading decisions.
    public static class AdaptiveSupertrendSeeder
    {
        public static (AdaptiveSupertrendState State, List<AtrWindowEntry> Window) Seed(List<HistoricalBar> bars, int atrLength, decimal factor)
        {
            // Needs `atrLength` bars to seed the first raw ATR value, PLUS a further
            // AdaptiveVolatilityClusterer.TrainingWindow (100) bars of raw-ATR history before the
            // K-Means clustering has a full window to train on for even the earliest seedable bar —
            // see StrategyMaker.ComputeDaysNeeded's Adaptive Supertrend branch, which folds this
            // extra requirement into how much history WarmUpService actually fetches.
            int minBarsNeeded = atrLength + AdaptiveVolatilityClusterer.TrainingWindow;
            if (atrLength <= 0 || bars.Count < minBarsNeeded)
                return (new AdaptiveSupertrendState("Up", 0, 0, 0, 0, 0, "", 0, 0, 0, false,
                    bars.Count > 0 ? bars[^1].WindowsStartTime : null), new List<AtrWindowEntry>());

            // trueRange[0] is unused/undefined — TR needs a previous close, so it's only defined from index 1.
            var trueRange = new decimal[bars.Count];
            for (int i = 1; i < bars.Count; i++)
            {
                var prevClose = bars[i - 1].Close;
                trueRange[i] = Math.Max(bars[i].High - bars[i].Low,
                    Math.Max(Math.Abs(bars[i].High - prevClose), Math.Abs(bars[i].Low - prevClose)));
            }

            // Wilder/RMA-smoothed ATR (ta.atr(atrLength) in the source) — deliberately different from
            // regular Supertrend's plain-average ATR (see SupertrendSeeder.cs's own comment on that
            // choice). The two indicators intentionally read different volatility inputs; this one
            // exists only to feed this indicator's K-Means clustering.
            var atr = new decimal[bars.Count];
            atr[atrLength] = Enumerable.Range(1, atrLength).Select(i => trueRange[i]).Average();
            for (int i = atrLength + 1; i < bars.Count; i++)
                atr[i] = (atr[i - 1] * (atrLength - 1) + trueRange[i]) / atrLength;

            // First bar where a full 100-value trailing ATR window exists: atr[atrLength] is the
            // oldest usable value, so the first complete window is [atrLength .. atrLength+99].
            int firstClusterableBar = atrLength + AdaptiveVolatilityClusterer.TrainingWindow - 1;

            decimal finalUpper = 0, finalLower = 0;
            string direction = "Up";
            AdaptiveVolatilityClusterer.Result lastAssignment = new(0, "", 0, 0, 0, 0, 0);

            for (int i = firstClusterableBar; i < bars.Count; i++)
            {
                var trainingWindow = Enumerable.Range(i - AdaptiveVolatilityClusterer.TrainingWindow + 1, AdaptiveVolatilityClusterer.TrainingWindow)
                    .Select(idx => atr[idx]).ToList();
                lastAssignment = AdaptiveVolatilityClusterer.Assign(trainingWindow, atr[i]);

                decimal centroidAtr = lastAssignment.AssignedCentroid;
                decimal mid = (bars[i].High + bars[i].Low) / 2m;
                decimal basicUpper = mid + factor * centroidAtr;
                decimal basicLower = mid - factor * centroidAtr;

                if (i == firstClusterableBar)
                {
                    // First bar with a real (non-na, in Pine terms) ATR — no previous final band to
                    // ratchet against yet, so the basic bands stand as-is. The Pine source sets
                    // _direction := 1 (bearish/"Down" in our string convention) unconditionally here
                    // regardless of price — matched exactly rather than inferring a "smarter" start.
                    finalUpper = basicUpper;
                    finalLower = basicLower;
                    direction = "Down";
                    continue;
                }

                var prevClose = bars[i - 1].Close;
                finalUpper = (basicUpper < finalUpper || prevClose > finalUpper) ? basicUpper : finalUpper;
                finalLower = (basicLower > finalLower || prevClose < finalLower) ? basicLower : finalLower;

                bool trackingUpper = direction == "Down";
                bool nowTrackingUpper =
                    trackingUpper && bars[i].Close <= finalUpper ? true :
                    trackingUpper && bars[i].Close > finalUpper ? false :
                    !trackingUpper && bars[i].Close >= finalLower ? false :
                    true;

                direction = nowTrackingUpper ? "Down" : "Up";
            }

            var lastBar = bars[^1];
            var state = new AdaptiveSupertrendState(
                direction, finalUpper, finalLower, lastBar.Close,
                lastAssignment.AssignedCentroid, atr[bars.Count - 1], lastAssignment.Cluster,
                lastAssignment.HighCentroid, lastAssignment.MediumCentroid, lastAssignment.LowCentroid,
                true, lastBar.WindowsStartTime);

            var window = Enumerable.Range(bars.Count - AdaptiveVolatilityClusterer.TrainingWindow, AdaptiveVolatilityClusterer.TrainingWindow)
                .Select(i => new AtrWindowEntry(bars[i].WindowsStartTime, atr[i]))
                .ToList();

            return (state, window);
        }
    }
}
