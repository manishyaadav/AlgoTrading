using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrategyService.PerformanceStatistics
{
    public class Statistics
    {
        public Backtest Backtest { get; set; } = new Backtest();
        public Current Current { get; set; } = new Current();
    }
}
