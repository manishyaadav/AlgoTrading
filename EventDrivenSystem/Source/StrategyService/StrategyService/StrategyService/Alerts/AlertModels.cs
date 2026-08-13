namespace StrategyService.Alerts
{
    // Permissive union shape used only to PARSE whatever's actually in Alert:Feed:{date} — the list
    // holds two independently-serialized record shapes (AggregationService's IndicatorAlertRecord,
    // this service's own PositionAlertRecord; no shared project reference between them, established
    // convention), discriminated by Kind ("IndicatorSignal" | "PositionEvent"). Every field beyond
    // Kind/AlertType/WindowsStartTime is nullable/optional since which ones are populated depends on
    // which of the two shapes a given entry actually is.
    public record RawAlertFeedRecord(
        string? Kind, string? AlertType, string? Instrument, string? Ticker, string? Timeframe, int? TimeframeMinutes,
        string? Reference, int? Period, int? Multiplier, decimal? Value, decimal? PreviousValue,
        string? Direction, string? PreviousDirection, decimal? PenetratedPoints, decimal? Close, decimal? PreviousClose,
        string? StrategyId, string? StrategyName, string? Side, decimal? EntryPrice, decimal? ExitPrice, string? Reason,
        DateTime WindowsStartTime, string? ProducedAt);

    public record AlertsResponse(string Date, List<PositionSummary> Positions, List<AlertLogEntry> Alerts, string GeneratedAt);

    public record PositionSummary(
        string StrategyId, string StrategyName, string Instrument, string Ticker, int LotSize,
        string Status,           // "NotTrackable" | "NotYetEntered" | "Open" | "Flat"
        string? Side, decimal? EntryPrice, string? EntryTime, decimal? InitialStopLoss,
        decimal? CurrentProfit,  // recomputed live at request time — never cached
        string? TimeInTrade,     // display string, e.g. "37m"
        decimal? ExitPrice, string? ExitTime, string? ExitReason);

    public record AlertLogEntry(
        string Kind, string AlertType, string Instrument, string Ticker, string Timeframe,
        string? Reference, int? Period, int? Multiplier, decimal? Value, decimal? PreviousValue,
        string? Direction, string? PreviousDirection, decimal? PenetratedPoints, decimal? Close,
        List<string> StrategyIds, List<string> StrategyNames,
        string? Side, decimal? EntryPrice, decimal? ExitPrice, string? Reason,
        string WindowsStartTime, string? ProducedAt);
}
