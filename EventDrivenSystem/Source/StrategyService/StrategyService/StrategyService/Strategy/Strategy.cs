using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrategyService.Strategy
{
    public class Strategy
    {
        public string? Exchange { get; set; }
        public string? StrategyName { get; set; }
        public string? Version { get; set; }
        /// <summary>
        /// Which Version was last explicitly deployed, distinct from the current (possibly newer,
        /// unreleased) working Version. Not persisted on the saved file — StrategyMaker computes this
        /// on every read by checking config/strategies/deployed/ for a matching snapshot, which is
        /// also where the deployed version's actual rule content lives (see DeployById). Setting this
        /// property directly has no effect beyond the in-memory object; it's overwritten by GetById/
        /// LoadAll immediately after.
        /// </summary>
        public string? DeployedVersion { get; set; }
        public string? Broker { get; set; }
        public List<string>? Goals { get; set; }
        public List<TradingStrategy>? Strategies { get; set; }
    }
}
