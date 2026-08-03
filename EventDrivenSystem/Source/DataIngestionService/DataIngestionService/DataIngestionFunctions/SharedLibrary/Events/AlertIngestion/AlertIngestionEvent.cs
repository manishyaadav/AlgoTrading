using SharedLibrary.Enums;
using SharedLibrary.Enums.DataFeed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Events.AlertIngestion
{
    public class AlertIngestionEvent : AlertEventBase
    {
        [JsonPropertyName("SourceToken")]
        public string SourceToken { get; set; } = string.Empty;

        [JsonPropertyName("Ticker")]
        public string Ticker { get; set; } = string.Empty;

        [JsonPropertyName("Timeframe")]
        public int Timeframe { get; set; }

        [JsonPropertyName("LookBackPeriod")]
        public int LookBackPeriod { get; set; }

        [JsonPropertyName("MinBodyStrength")]
        public int MinBodyStrength { get; set; }

        [JsonPropertyName("Q3Multiplier")]
        public decimal Q3Multiplier { get; set; }

        [JsonPropertyName("AlertType")]
        public string AlertType { get; set; } = string.Empty;

        [JsonPropertyName("Level")]
        public int Level { get; set; }

        [JsonPropertyName("Length")]
        public int Length { get; set; }

        [JsonPropertyName("Direction")]
        public int Direction { get; set; }

        [JsonPropertyName("PointVal")]
        public decimal PointVal { get; set; }

        [JsonPropertyName("Q3Value")]
        public decimal Q3Value { get; set; }

        [JsonPropertyName("BodyStrength")]
        public decimal BodyStrength { get; set; }

        [JsonPropertyName("WindowsStartTime")]
        public DateTime WindowsStartTime { get; set; } 
    }
}
