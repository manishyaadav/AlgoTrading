using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrategyService.PerformanceStatistics
{
    public class PerformanceStats
    {
        public string Exchange { get; set; } = "";
        public string Instrument { get; set; } = "";
        public string FromDate { get; set; } = "";
        public string ToDate { get; set; } = "";
        public string CAGR { get; set; } = "";
        public string MaxDrawDown { get; set; } = "";
        public int Investment { get; set; }
        public int ProfitAndLoss { get; set; }
        // Additional common properties
        public double SharpeRatio { get; set; }
        public double SortinoRatio { get; set; }
        public double Beta { get; set; }
        public double Alpha { get; set; }
        public double AnnualVolatility { get; set; }
        public double WinRate { get; set; }
        public double AverageWinToLossRatio { get; set; }
        public int MaxConsecutiveWins { get; set; }
        public int MaxConsecutiveLosses { get; set; }
        public string DrawdownDuration { get; set; } = "";
    }
}
