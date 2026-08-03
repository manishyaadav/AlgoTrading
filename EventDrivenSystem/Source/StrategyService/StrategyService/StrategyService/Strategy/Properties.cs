using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrategyService.Strategy
{
    public class Properties
    {
        //[JsonConverter(typeof(PeriodConverter))] // Custom converter if needed
        public int Period { get; set; }
        public int Multiplier { get; set; }
        public string? Timeframe { get; set; }
        public string? Instrument { get; set; }
        public string? RelativePosition { get; set; }
    }
}
