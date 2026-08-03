using SharedLibrary.Events.AlertIngestion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PivotMarkingAggregationFunction.SharedLibrary.Events.AlertIngestion
{
    public class PivotMarkingEvent : AlertEventBase
    {
        [JsonPropertyName("SourceToken")]
        public string SourceToken { get; set; } = string.Empty;

        [JsonPropertyName("Ticker")]
        public string Ticker { get; set; } = string.Empty;

        [JsonPropertyName("Timeframe")]
        public int Timeframe { get; set; }

        [JsonPropertyName("AlertType")]
        public string AlertType { get; set; } = string.Empty;

        [JsonPropertyName("Level")]
        public int Level { get; set; }

        [JsonPropertyName("Length")]
        public int Length { get; set; }

        [JsonPropertyName("Direction")]
        public int Direction { get; set; }

        [JsonPropertyName("MarkingType")]
        public string MarkingType { get; set; } = string.Empty;

        [JsonPropertyName("PointVal")]
        public decimal PointVal { get; set; }

        [JsonPropertyName("WindowsStartTime")]
        public DateTime WindowsStartTime { get; set; }
    }
}
