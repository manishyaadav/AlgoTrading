namespace WarmUpService.Indicators
{
    // Pivot = (H+L+C)/3, BC (Bottom Central) = (H+L)/2, TC (Top Central) = 2*Pivot - BC.
    // Width = TC - BC — the value the deployed strategy's rule actually compares against a
    // percentage of closing price ("Pivot Central Range < 0.0038 * Closing Price"), i.e. this is a
    // narrow/wide-range-day gauge, not a price level itself. Pivot/TC/BC are carried in the state
    // too, since a future rule could reasonably want one of those instead of just the width.
    public record PivotCentralRangeState(decimal Pivot, decimal TopCentral, decimal BottomCentral, decimal Width, DateTime SessionDate);

    // No live phase at all (WARMUP_AND_INDICATOR_PLAN.md section 2e) — computed fresh every morning
    // from the prior trading session's one "1 Day" OHLC bar, not seeded-then-carried-forward like
    // EMA/Supertrend. AggregationService never touches this.
    public static class PivotCentralRangeCalculator
    {
        public static PivotCentralRangeState Compute(decimal high, decimal low, decimal close, DateTime sessionDate)
        {
            decimal pivot = (high + low + close) / 3m;
            decimal bottomCentral = (high + low) / 2m;
            decimal topCentral = 2m * pivot - bottomCentral;
            decimal width = topCentral - bottomCentral;

            return new PivotCentralRangeState(pivot, topCentral, bottomCentral, width, sessionDate);
        }
    }
}
