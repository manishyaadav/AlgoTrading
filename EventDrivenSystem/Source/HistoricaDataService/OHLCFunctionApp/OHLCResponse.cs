using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace OHLCFunctionApp
{
    internal class OHLCResponse
    {
        public string ContractName {get;set;}
        public int Timeframe { get; set; }
        public DateTime Date { get; set; }
        public double Open { get; set; }
        public double Low { get; set; }
        public double High { get; set; }
        public double Close { get; set; }
        public int Volume { get; set; }
    }
}
