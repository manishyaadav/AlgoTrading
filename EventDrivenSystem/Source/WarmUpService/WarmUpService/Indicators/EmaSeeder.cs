using WarmUpService.Common;

namespace WarmUpService.Indicators
{
    // { LastEma, SeedBarsSeenSoFar, IsSeeded } — WARMUP_AND_INDICATOR_PLAN.md section 2e's exact
    // state shape. Tiny by design: EMA needs no candle history once seeded, just the running value.
    public record EmaState(decimal LastEma, int SeedBarsSeenSoFar, bool IsSeeded, DateTime? LastBarWindowsStartTime);

    public static class EmaSeeder
    {
        // Standard EMA seeding: the first `period` closes' simple average becomes the seed value,
        // then the recursive formula (2/(period+1) multiplier) runs forward through every remaining
        // historical bar — so the state this returns reflects the most recent available bar (in
        // practice, yesterday's session close), ready for today's live candles to continue updating
        // it, not a value stale from the start of the fetch window.
        //
        // If fewer than `period` bars are available, there's nothing to seed yet — returns
        // IsSeeded=false with however many bars were actually seen, mirroring the same
        // SeedBarsSeenSoFar/IsSeeded fields AggregationService's live path would use if it had to
        // accumulate bars one at a time instead of seeding from a batch of history.
        public static EmaState Seed(List<HistoricalBar> bars, int period)
        {
            if (period <= 0 || bars.Count < period)
                return new EmaState(0, bars.Count, false, bars.Count > 0 ? bars[^1].WindowsStartTime : null);

            decimal ema = bars.Take(period).Average(b => b.Close);
            decimal multiplier = 2m / (period + 1);

            for (int i = period; i < bars.Count; i++)
                ema = (bars[i].Close - ema) * multiplier + ema;

            return new EmaState(ema, period, true, bars[^1].WindowsStartTime);
        }
    }
}
