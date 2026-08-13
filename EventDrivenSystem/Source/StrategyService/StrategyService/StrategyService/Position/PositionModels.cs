using System.Globalization;
using StackExchange.Redis;

namespace StrategyService.Position
{
    // Virtual/paper position state for one deployed strategy — Alerts feature. Entry is the simple
    // heuristic the user described (first bar of day, side = Supertrend's color then; a genuine ST
    // flip while flat re-enters later the same day); exit evaluates the strategy's own real
    // ExitRules live (see ExitRuleEvaluator.cs). Points, not currency — EntryPrice/InitialStopLoss
    // stay in raw index points, matching Backtest's own disclosed convention; LotSize is stored for
    // display/reference only, never multiplied into anything.
    public record PositionState(
        string Status,                          // "Open" | "Flat"
        string? Side,                           // "Long" | "Short"
        decimal? EntryPrice, DateTime? EntryTime,
        decimal? InitialStopLoss,               // points, |EntryPrice - Supertrend value at entry|
        string Instrument, string Ticker, string Exchange,
        string Timeframe, int Period, int Multiplier,   // which Supertrend instance drives this position
        int LotSize,
        decimal? ExitPrice, DateTime? ExitTime, string? ExitReason,
        DateTime? LastEntryWindowsStartTime,    // idempotency guard — the bar whose close triggered the current/most-recent entry
        DateTime? LastEvaluatedAt)
    {
        public static string KeyFor(string strategyId) => $"Position:Strategy:{strategyId}";

        private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";

        public static PositionState? FromHash(Dictionary<string, string> hash)
        {
            if (hash.Count == 0 || !hash.TryGetValue("Status", out var status)) return null;

            return new PositionState(
                Status: status,
                Side: GetOrNull(hash, "Side"),
                EntryPrice: GetDecimalOrNull(hash, "EntryPrice"),
                EntryTime: GetDateTimeOrNull(hash, "EntryTime"),
                InitialStopLoss: GetDecimalOrNull(hash, "InitialStopLoss"),
                Instrument: hash.GetValueOrDefault("Instrument", ""),
                Ticker: hash.GetValueOrDefault("Ticker", ""),
                Exchange: hash.GetValueOrDefault("Exchange", ""),
                Timeframe: hash.GetValueOrDefault("Timeframe", ""),
                Period: GetIntOrDefault(hash, "Period"),
                Multiplier: GetIntOrDefault(hash, "Multiplier"),
                LotSize: GetIntOrDefault(hash, "LotSize"),
                ExitPrice: GetDecimalOrNull(hash, "ExitPrice"),
                ExitTime: GetDateTimeOrNull(hash, "ExitTime"),
                ExitReason: GetOrNull(hash, "ExitReason"),
                LastEntryWindowsStartTime: GetDateTimeOrNull(hash, "LastEntryWindowsStartTime"),
                LastEvaluatedAt: GetDateTimeOrNull(hash, "LastEvaluatedAt"));
        }

        public HashEntry[] ToHash() => new HashEntry[]
        {
            new("Status", Status),
            new("Side", Side ?? ""),
            new("EntryPrice", EntryPrice?.ToString(CultureInfo.InvariantCulture) ?? ""),
            new("EntryTime", EntryTime?.ToString(DateTimeFormat) ?? ""),
            new("InitialStopLoss", InitialStopLoss?.ToString(CultureInfo.InvariantCulture) ?? ""),
            new("Instrument", Instrument),
            new("Ticker", Ticker),
            new("Exchange", Exchange),
            new("Timeframe", Timeframe),
            new("Period", Period.ToString(CultureInfo.InvariantCulture)),
            new("Multiplier", Multiplier.ToString(CultureInfo.InvariantCulture)),
            new("LotSize", LotSize.ToString(CultureInfo.InvariantCulture)),
            new("ExitPrice", ExitPrice?.ToString(CultureInfo.InvariantCulture) ?? ""),
            new("ExitTime", ExitTime?.ToString(DateTimeFormat) ?? ""),
            new("ExitReason", ExitReason ?? ""),
            new("LastEntryWindowsStartTime", LastEntryWindowsStartTime?.ToString(DateTimeFormat) ?? ""),
            new("LastEvaluatedAt", LastEvaluatedAt?.ToString(DateTimeFormat) ?? ""),
        };

        private static string? GetOrNull(Dictionary<string, string> hash, string field) =>
            hash.TryGetValue(field, out var raw) && !string.IsNullOrEmpty(raw) ? raw : null;

        private static decimal? GetDecimalOrNull(Dictionary<string, string> hash, string field) =>
            hash.TryGetValue(field, out var raw) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : (decimal?)null;

        private static DateTime? GetDateTimeOrNull(Dictionary<string, string> hash, string field) =>
            hash.TryGetValue(field, out var raw) && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var val) ? val : (DateTime?)null;

        private static int GetIntOrDefault(Dictionary<string, string> hash, string field) =>
            hash.TryGetValue(field, out var raw) && int.TryParse(raw, out var val) ? val : 0;
    }
}
