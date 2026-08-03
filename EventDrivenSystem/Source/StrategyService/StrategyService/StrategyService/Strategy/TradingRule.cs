using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrategyService.Strategy
{
    public class TradingRule
    {
        public int Sequence { get; set; }
        public Operand? LeftOperand { get; set; }
        public string? Operator { get; set; }
        public Operand? RightOperand { get; set; }
        public string? Link { get; set; }
    }
}
