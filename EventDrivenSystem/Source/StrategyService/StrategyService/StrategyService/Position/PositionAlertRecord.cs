namespace StrategyService.Position
{
    // Pushed onto the same Alert:Feed:{yyyy-MM-dd} list AggregationService's IndicatorAlertRecord
    // goes onto — already carries strategy attribution (unlike indicator-sourced alerts, which get
    // attributed at GET /api/alerts read time) since that context is on hand when this is written.
    public record PositionAlertRecord(
        string Kind,        // always "PositionEvent"
        string AlertType,   // "PositionEntered" | "PositionExited"
        string StrategyId, string StrategyName, string Instrument, string Ticker,
        string Side, decimal? EntryPrice, decimal? ExitPrice, string? Reason,
        DateTime WindowsStartTime, string ProducedAt);
}
