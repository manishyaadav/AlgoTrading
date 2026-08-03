using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Events
{
    public class TradingViewDataEvent
    {
        [JsonPropertyName("Ticker")]
        public string Ticker { get; set; } = string.Empty;

        [JsonPropertyName("Timeframe")]
        public int Timeframe { get; set; }

        [JsonPropertyName("Open")]
        public decimal Open { get; set; }

        [JsonPropertyName("High")]
        public decimal High { get; set; }

        [JsonPropertyName("Low")]
        public decimal Low { get; set; }

        [JsonPropertyName("Close")]
        public decimal Close { get; set; }

        [JsonPropertyName("Volume")]
        public long Volume { get; set; }

        [JsonPropertyName("EventTime")]
        public DateTime EventTime { get; set; } // Assuming "timenow" is a DateTime

        [JsonPropertyName("Time")]
        public DateTime Time { get; set; } // Assuming "time" represents a duration
    }
}
