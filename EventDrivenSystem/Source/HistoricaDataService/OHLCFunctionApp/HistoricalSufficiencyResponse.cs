using System.Collections.Generic;

namespace OHLCFunctionApp
{
    // One checked trading day and whether Azurite actually has a blob for it.
    public record TradingDayAvailability(string Date, bool Exists, string Path);

    // "Given this instrument and N days needed, does Azurite actually have that much?" — see
    // WARMUP_AND_INDICATOR_PLAN.md section 2d. Existence-only (is the blob there), not a
    // bar-count completeness check — that's a separate, deeper capability if it's ever needed.
    public record HistoricalSufficiencyResponse(
        string Exchange,
        string InstrumentName,
        int DaysNeeded,
        int DaysChecked,
        int DaysAvailable,
        int DaysMissing,
        bool Sufficient,
        List<TradingDayAvailability> Days);
}
