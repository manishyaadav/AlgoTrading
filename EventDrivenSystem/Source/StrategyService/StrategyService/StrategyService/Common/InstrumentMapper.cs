namespace StrategyService.Common
{
    // This service's own copy of WarmUpService/Common/InstrumentMapper.cs's strategy-facing-name ->
    // ticker/exchange bridge (no shared project reference between services — established convention).
    // Extended here with LotSize, needed by the Alerts feature's virtual position tracking: a fixed
    // lot count per instrument, stored/displayed on the position for reference only — never
    // multiplied into any P&L figure (this codebase has repeatedly and deliberately refused to
    // fabricate a capital/currency model; Current Profit and Initial Stop Loss stay in raw index
    // points, same as Backtest's own disclosed convention).
    public static class InstrumentMapper
    {
        public record ResolvedInstrument(string Ticker, string Exchange, int LotSize);

        private static readonly Dictionary<string, ResolvedInstrument> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nifty_Index_Spot"] = new ResolvedInstrument("NIFTY", "NSE", 65),
            ["Banknifty_Index_Spot"] = new ResolvedInstrument("BANKNIFTY", "NSE", 15),
        };

        public static bool TryResolve(string strategyInstrument, out ResolvedInstrument? resolved) =>
            Map.TryGetValue(strategyInstrument, out resolved);

        // Reverse lookup — PositionExitFunction only has the raw ticker off Kafka
        // (DataIngestionMinDataEventDto.Ticker), not the strategy-facing instrument name, and needs
        // to find which open positions' Instrument that ticker corresponds to.
        public static bool TryResolveByTicker(string ticker, out string? strategyInstrument)
        {
            foreach (var (instrument, resolved) in Map)
            {
                if (string.Equals(resolved.Ticker, ticker, StringComparison.OrdinalIgnoreCase))
                {
                    strategyInstrument = instrument;
                    return true;
                }
            }
            strategyInstrument = null;
            return false;
        }
    }
}
