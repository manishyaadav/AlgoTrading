namespace WarmUpService.Strategy
{
    // Mirrors StrategyService's StrategyService.Strategy.WarmUpReason/InstrumentWarmUpPlan — see
    // GET /api/strategies/warm-up-plan there. Deserialized with System.Text.Json's
    // PropertyNameCaseInsensitive (see StrategyServiceClient), which matches its camelCase JSON to
    // these PascalCase properties without needing any [JsonPropertyName] renaming on either side —
    // deliberately avoiding the cross-service JSON contract bug class documented in
    // NotificationService/README.md (that one happened because two *different* serializers
    // disagreed on a renamed property; here there's only one serializer involved on this side, and
    // no renaming, so the same failure mode can't occur).
    public record WarmUpReason(
        string Timeframe,
        string Reference,
        string Type,
        int Period,
        int Multiplier,
        string? RelativePosition,
        int DaysNeeded);

    public record InstrumentWarmUpPlan(
        string Instrument,
        int DaysToFetch,
        List<WarmUpReason> Reasons,
        List<string> StrategyIds);
}
