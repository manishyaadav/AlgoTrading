namespace WarmUpService.Common
{
    // StrategyService identifies instruments by a strategy-facing name ("Nifty_Index_Spot" — the
    // literal value in the deployed second-income strategy's config); the live pipeline (Kafka
    // payloads, Redis keys, ohlc-live's blob paths) only ever knows the bare ticker ("NIFTY") plus
    // which exchange it trades on. Nothing else in the codebase bridges these two vocabularies —
    // this is that bridge. Kept as a small explicit table rather than string-parsing the strategy
    // name (e.g. stripping "_Index_Spot") because a wrong guess here would silently seed or query
    // the wrong instrument with no error to catch it. Extend this table, not parsing rules, when a
    // new instrument shows up in a strategy config.
    public static class InstrumentMapper
    {
        public record ResolvedInstrument(string Ticker, string Exchange);

        private static readonly Dictionary<string, ResolvedInstrument> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nifty_Index_Spot"] = new ResolvedInstrument("NIFTY", "NSE"),
            ["Banknifty_Index_Spot"] = new ResolvedInstrument("BANKNIFTY", "NSE"),
        };

        public static bool TryResolve(string strategyInstrument, out ResolvedInstrument? resolved) =>
            Map.TryGetValue(strategyInstrument, out resolved);
    }
}
