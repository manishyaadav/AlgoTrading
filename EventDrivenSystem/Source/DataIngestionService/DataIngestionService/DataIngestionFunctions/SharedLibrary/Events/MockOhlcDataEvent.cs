using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DataIngestionFunctions.SharedLibrary.Events
{
    public class MockOhlcDataEvent
    {       
        [JsonPropertyName("contractName")]
        public string ContractName { get; set; } = string.Empty;

        [JsonPropertyName("timeframe")]
        public int Timeframe { get; set; }

        [JsonPropertyName("open")]
        public decimal Open { get; set; }

        [JsonPropertyName("high")]
        public decimal High { get; set; }

        [JsonPropertyName("low")]
        public decimal Low { get; set; }

        [JsonPropertyName("close")]
        public decimal Close { get; set; }

        [JsonPropertyName("volume")]
        public long Volume { get; set; }
       
        [JsonPropertyName("date")]
        public DateTime WindowsStartTime { get; set; } // Assuming "time" represents a duration
    }
}
