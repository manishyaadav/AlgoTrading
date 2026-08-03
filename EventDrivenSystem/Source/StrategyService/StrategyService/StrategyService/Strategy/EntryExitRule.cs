using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrategyService.Strategy
{
    public class EntryExitRule
    {
        public List<TradingRule>? EntryRules { get; set; }
        public List<TradingRule>? RiskManagementRules { get; set; }
        public List<TradingRule>? UpdateStopLossRules { get; set; }
        public List<TradingRule>? ExitRules { get; set; }
    }
}
