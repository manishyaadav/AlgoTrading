namespace StrategyService.Backtest
{
    public record BacktestRequest(DateTime StartDate, DateTime EndDate);

    // One simulated round-trip. EntryRuleText/ExitRuleText are the human-readable rule that actually
    // fired (describeRule()-equivalent, resolved through the dashboard's own formatter on the
    // frontend from the raw TradingRule already in the response) — kept as the raw rule + a plain
    // description here rather than pre-formatted, so the frontend's existing describeRule() stays
    // the single place rule text gets rendered, not a second copy of that logic in C#.
    public record BacktestTrade(
        string Side, // "Long" | "Short"
        DateTime EntryTime, decimal EntryPrice,
        DateTime ExitTime, decimal ExitPrice,
        decimal PointsPnl,
        string ExitReason, // the exit rule's description, or "Period end (forced close)"
        bool ForcedCloseAtPeriodEnd);

    public record BacktestStats(
        int TotalTrades, int Wins, int Losses,
        decimal WinRatePct,
        decimal TotalPoints,
        decimal AveragePointsPerTrade,
        decimal AverageWinPoints, decimal AverageLossPoints,
        decimal LargestWinPoints, decimal LargestLossPoints,
        decimal ProfitFactor, // sum(wins) / abs(sum(losses)) — decimal.MaxValue when there are wins and zero losses, 0 when there are no wins at all
        decimal MaxDrawdownPoints,
        int LongestWinStreak, int LongestLossStreak);

    // Status: "completed" | "insufficient-data" | "no-entry-rules" | "no-instrument" | "error"
    // Exactly one of {Trades+Stats} or {DataAvailability} or {Message alone} is populated,
    // depending on Status — the frontend branches on Status first, same contract as everywhere else
    // in this codebase that returns a "here's what happened, honestly" shape rather than throwing
    // for anything short of a genuine server error.
    public record BacktestResponse(
        string Status,
        string Message,
        string? Instrument,
        string? Exchange,
        string? SimulationTimeframe, // the finest timeframe the engine actually ticked at
        DateTime StartDate,
        DateTime EndDate,
        List<BacktestTrade>? Trades,
        BacktestStats? Stats,
        HistoricalSufficiencyResponse? DataAvailability);
}
