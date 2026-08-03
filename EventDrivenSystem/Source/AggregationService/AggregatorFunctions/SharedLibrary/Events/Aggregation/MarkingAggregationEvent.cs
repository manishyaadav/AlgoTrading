using AggregatorFunctions.SharedLibrary.Enums.Swing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AggregatorFunctions.SharedLibrary.Events.Aggregation
{
    public class MarkingAggregationEvent
    {
        [JsonPropertyName("Ticker")]
        public string Ticker { get; set; } = string.Empty;

        [JsonPropertyName("Timeframe")]
        public int Timeframe { get; set; }

        [JsonPropertyName("MarkingTime")]
        public DateTime MarkingTime { get; set; }

        [JsonPropertyName("MarkingPointVal")]
        public decimal MarkingPointVal { get; set; }

        [JsonPropertyName("MarkingType")]
        public MarkingTypeEnum MarkingType { get; set; }
    }
}
