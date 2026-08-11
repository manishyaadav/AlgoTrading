namespace WarmUpService.Manifest
{
    // One row per period-based indicator instance (EMA/Supertrend — Pivot Central Range is excluded,
    // it has no live phase for AggregationService to drive) that's locked in for today. Written once
    // to Redis (Indicator:Manifest:Active) at the end of every Init run, and read fresh by
    // AggregationService's live calculators on every candle — a single source of truth for "what's
    // active today" so warm-up's seed and the live update path can never independently disagree
    // about which instances exist. Carries both Instrument (the strategy-facing name, e.g.
    // "Nifty_Index_Spot" — used in the Indicator:Running:* key) and Ticker (the live-pipeline name,
    // e.g. "NIFTY" — used to match an incoming candle) so AggregationService doesn't need its own
    // copy of InstrumentMapper just to bridge the two vocabularies.
    public record ActiveIndicatorInstance(
        string Instrument, string Ticker, string Exchange,
        string Timeframe, int TimeframeMinutes,
        string Reference, int Period, int Multiplier);
}
