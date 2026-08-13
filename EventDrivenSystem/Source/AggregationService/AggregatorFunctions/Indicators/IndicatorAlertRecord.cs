namespace AggregatorFunctions.Indicators
{
    // One entry pushed onto Alert:Feed:{yyyy-MM-dd} (IST) for the Alerts dashboard page. Deliberately
    // carries no strategy attribution — this service has no knowledge of strategies at all, only of
    // raw indicator instances. StrategyService's GET /api/alerts resolves "which deployed strategy
    // does this belong to" at read time (via GetDeployedDataRequirements), not here.
    public record IndicatorAlertRecord(
        string Kind,              // always "IndicatorSignal"
        string AlertType,         // "EmaValueChanged" | "PriceCrossedEma" | "SupertrendValueChanged"
                                   // | "SupertrendColorChanged" | "SupertrendFalsePenetration"
        string Instrument, string Ticker, string Timeframe, int TimeframeMinutes,
        string Reference, int Period, int Multiplier,
        decimal? Value, decimal? PreviousValue,
        string? Direction, string? PreviousDirection,   // "Up"/"Down" — raw Redis vocabulary, translated to GREEN/RED only at display time
        decimal? PenetratedPoints,                       // SupertrendFalsePenetration only
        decimal? Close, decimal? PreviousClose,          // PriceCrossedEma only
        DateTime WindowsStartTime,
        string ProducedAt);
}
