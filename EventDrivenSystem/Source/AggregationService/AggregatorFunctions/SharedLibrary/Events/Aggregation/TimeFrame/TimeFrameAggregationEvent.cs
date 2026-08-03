using SharedLibrary.Events.DataIngestion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AggregatorFunctions.SharedLibrary.Events.Aggregation.TimeFrame
{
    public class TimeFrameAggregationEvent : AggregationEventBase
    {        
        [JsonPropertyName("Ticker")]
        public string Ticker { get; set; } = string.Empty;

        [JsonPropertyName("WindowsStartTime")]
        public DateTime WindowsStartTime { get; set; }

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
    }
}
