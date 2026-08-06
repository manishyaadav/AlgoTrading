namespace StrategyService.Strategy
{
    /// <summary>
    /// One (Indicator, Period, Multiplier, RelativePosition) combination referencing a
    /// DataRequirement's (Instrument, Timeframe) pair, and which deployed strategy id(s) use exactly
    /// that combination. Despite the name, "Indicator" here is just the operand's Value — it covers
    /// both true Indicator-type operands (EMA, Supertrend, Pivot Central Range — where Period/
    /// Multiplier are the real lookback inputs) and Expression-type operands that reference raw price
    /// data (e.g. "Closing Price", "Candle Low" — where Period/Multiplier are 0 and RelativePosition,
    /// e.g. "Previous", is what actually signals a historical need). Two strategies referencing the
    /// same indicator/expression with different parameters (e.g. EMA(200) vs EMA(550) on the same
    /// 5-min NIFTY candle) are two separate entries, not merged, since "how much history to fetch"
    /// depends on the specific parameters, not just the name.
    /// </summary>
    public record IndicatorUsage(string Indicator, string Type, int Period, int Multiplier, string? RelativePosition, List<string> StrategyIds);

    /// <summary>
    /// One entry in the deployed-strategies data-requirements manifest — see
    /// StrategyMaker.GetDeployedDataRequirements(). StrategyIds is every deployed strategy that
    /// references this (Instrument, Timeframe) pair at all; References breaks that down by which
    /// specific indicator(s)/expression(s) and Period/Multiplier/RelativePosition need it, which is
    /// what actually determines how much historical data a warm-up job needs to fetch — the
    /// (Instrument, Timeframe) pair alone doesn't say whether it's backing an EMA(550) or a
    /// Supertrend(20,4). Type ("Indicator" vs "Expression") is carried through because it changes how
    /// StrategyMaker.GetWarmUpPlan() interprets Period == 0 — see there.
    /// </summary>
    public record DataRequirement(string Instrument, string Timeframe, List<string> StrategyIds, List<IndicatorUsage> References);

    /// <summary>One (Timeframe, Reference) contributing to an instrument's warm-up day count — see StrategyMaker.GetWarmUpPlan().</summary>
    public record WarmUpReason(string Timeframe, string Reference, string Type, int Period, int Multiplier, string? RelativePosition, int DaysNeeded);

    /// <summary>
    /// "Fetch the last DaysToFetch trading days of Instrument data" — the answer to the actual
    /// question a warm-up job needs. DaysToFetch is the max DaysNeeded across every Reason (the most
    /// demanding requirement decides the fetch window, since one historical pull covers everything
    /// below it too). See StrategyMaker.GetWarmUpPlan() for the day-count assumptions.
    /// </summary>
    public record InstrumentWarmUpPlan(string Instrument, int DaysToFetch, List<WarmUpReason> Reasons, List<string> StrategyIds);
}
