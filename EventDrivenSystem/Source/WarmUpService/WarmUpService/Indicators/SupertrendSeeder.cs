using WarmUpService.Common;

namespace WarmUpService.Indicators
{
    // { TrendDirection, PrevUpperBand, PrevLowerBand } — WARMUP_AND_INDICATOR_PLAN.md section 2e's
    // exact hybrid-state shape, plus PrevClose (needed to compute the *next* live bar's True Range)
    // and Atr (the live path's rolling window average, kept here too so a live consumer doesn't have
    // to recompute it from the window on every single update). TrendDirection is "Up"/"Down" per the
    // plan doc — translating that to the strategy config's "GREEN"/"RED" literal is the (parked)
    // execution engine's job, not this seeder's.
    public record SupertrendState(
        string TrendDirection, decimal PrevUpperBand, decimal PrevLowerBand, decimal PrevClose,
        decimal Atr, bool IsSeeded, DateTime? LastBarWindowsStartTime);

    // One entry in the rolling True-Range window (the Redis List side of the hybrid design) — stores
    // the precomputed TR plus enough of the source bar to be useful for debugging, not just the bare
    // number.
    public record TrueRangeEntry(DateTime WindowsStartTime, decimal TrueRange, decimal High, decimal Low, decimal Close);

    // ⚠️ Not cross-checked against a reference Supertrend implementation (e.g. TradingView) with real
    // data — this follows the commonly-published band-ratchet formula (the same one Pine Script's
    // built-in indicator uses), but Supertrend has several slightly different popular variants and a
    // subtle indexing mistake here wouldn't throw, it would just produce a plausible-looking but wrong
    // value. Validate against a known-good source before trusting this for live trading decisions.
    public static class SupertrendSeeder
    {
        // Plain (simple-average) ATR over the trailing `period` True Ranges — deliberately not
        // Wilder's recursive smoothing, see plan doc section 2e for the tradeoff/rationale (reuses
        // the same proven Redis List mechanism the codebase already has, rather than a second
        // persistence pattern).
        public static (SupertrendState State, List<TrueRangeEntry> Window) Seed(List<HistoricalBar> bars, int period, decimal multiplier)
        {
            // Each TR needs the prior bar's close, and the window needs `period` TRs — period+1 bars minimum.
            if (period <= 0 || bars.Count < period + 1)
                return (new SupertrendState("Up", 0, 0, 0, 0, false, bars.Count > 0 ? bars[^1].WindowsStartTime : null), new List<TrueRangeEntry>());

            // trueRange[0] is unused/undefined — TR needs a previous close, so it's only defined from index 1.
            var trueRange = new decimal[bars.Count];
            for (int i = 1; i < bars.Count; i++)
            {
                var prevClose = bars[i - 1].Close;
                trueRange[i] = Math.Max(bars[i].High - bars[i].Low,
                    Math.Max(Math.Abs(bars[i].High - prevClose), Math.Abs(bars[i].Low - prevClose)));
            }

            decimal Atr(int i) => Enumerable.Range(i - period + 1, period).Select(idx => trueRange[idx]).Average();

            decimal finalUpper = 0, finalLower = 0;
            bool trackingUpper = true; // overwritten unconditionally on the first iteration (i == period) below
            string direction = "Up";

            for (int i = period; i < bars.Count; i++)
            {
                decimal atr = Atr(i);
                decimal mid = (bars[i].High + bars[i].Low) / 2m;
                decimal basicUpper = mid + multiplier * atr;
                decimal basicLower = mid - multiplier * atr;

                if (i == period)
                {
                    // First bar of the seed window — no previous final band to ratchet against yet.
                    finalUpper = basicUpper;
                    finalLower = basicLower;
                    trackingUpper = bars[i].Close <= finalUpper; // start on whichever side price is actually on
                    direction = trackingUpper ? "Down" : "Up";
                    continue;
                }

                var prevClose = bars[i - 1].Close;
                finalUpper = (basicUpper < finalUpper || prevClose > finalUpper) ? basicUpper : finalUpper;
                finalLower = (basicLower > finalLower || prevClose < finalLower) ? basicLower : finalLower;

                if (trackingUpper && bars[i].Close <= finalUpper) trackingUpper = true;
                else if (trackingUpper && bars[i].Close > finalUpper) trackingUpper = false;
                else if (!trackingUpper && bars[i].Close >= finalLower) trackingUpper = false;
                else trackingUpper = true;

                direction = trackingUpper ? "Down" : "Up";
            }

            var lastBar = bars[^1];
            var state = new SupertrendState(direction, finalUpper, finalLower, lastBar.Close, Atr(bars.Count - 1), true, lastBar.WindowsStartTime);

            var window = Enumerable.Range(bars.Count - period, period)
                .Select(i => new TrueRangeEntry(bars[i].WindowsStartTime, trueRange[i], bars[i].High, bars[i].Low, bars[i].Close))
                .ToList();

            return (state, window);
        }
    }
}
