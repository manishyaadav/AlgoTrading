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
        /// <summary>Which Version was last explicitly deployed, distinct from the current (possibly newer, unreleased) working Version. Set only via the deploy endpoint.</summary>
        public string? DeployedVersion { get; set; }
        public string? Broker { get; set; }
        public List<string>? Goals { get; set; }
        public List<TradingStrategy>? Strategies { get; set; }
    }
}
