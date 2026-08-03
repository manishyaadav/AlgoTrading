using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrategyService.Strategy
{
    public class TradingStrategy
    {
        public string? Risk { get; set; }
        public List<string>? Instruments { get; set; }
        /// <summary>Strike moneyness (ITM/ATM/OTM) — only meaningful when Instruments includes an options instrument.</summary>
        public string? Moneyness { get; set; }
        public string? TradeType { get; set; }
        public List<TradingRule>? TradingSessionRules { get; set; }
        public EntryExitRule? LongEntry { get; set; }
        public EntryExitRule? ShortEntry { get; set; }
    }
}
