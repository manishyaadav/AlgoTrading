using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DataIngestionFunctions.SharedLibrary.Events
{
    public class TradingViewAlertEvent
    {
        [JsonPropertyName("ticker")] 
        public string SourceToken { get; set; } = string.Empty;

        [JsonPropertyName("timeframe")]
        public string Timeframe { get; set; } = string.Empty;

        [JsonPropertyName("candleLookback")]
        public string CandleLookback { get; set; } = string.Empty;

        [JsonPropertyName("minBodyStrength")]
        public string MinBodyStrength { get; set; } = string.Empty;

        [JsonPropertyName("q3Multiplier")]
        public string Q3Multiplier { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("level")]
        public string Level { get; set; } = string.Empty;

        [JsonPropertyName("length")]
        public string Length { get; set; } = string.Empty;

        [JsonPropertyName("pointVal")]
        public string PointVal { get; set; } = string.Empty;

        [JsonPropertyName("direction")]
        public string Direction { get; set; } = string.Empty;

        [JsonPropertyName("bodyStrength")]
        public string BodyStrength { get; set; } = string.Empty;

        [JsonPropertyName("q3Value")]
        public string Q3Value { get; set; } = string.Empty;

        [JsonPropertyName("eventTime")]
        public DateTime EventTime { get; set; }

        [JsonPropertyName("time")]
        public DateTime WindowsStartTime { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }
    }
}
