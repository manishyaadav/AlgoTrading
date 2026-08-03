using SharedLibrary.Enums;
using SharedLibrary.Enums.DataFeed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Events.DataIngestion
{
    public class DataIngestionMinDataEvent : DataEventBase
    {
        [JsonPropertyName("SourceToken")]
        public string SourceToken { get; set; } = string.Empty;

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
