using AggregatorFunctions.SharedLibrary.Events.Aggregation.TimeFrame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AggregatorFunctions.SharedLibrary.Events.Aggregation.Candle
{
    public class CandleAggregationEvent : TimeFrameAggregationEvent
    {
        [JsonPropertyName("color")]
        public string Color { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public decimal Size { get; set; }

        [JsonPropertyName("sizeCategory")]
        public string SizeCategory { get; set; } = string.Empty;

        [JsonPropertyName("isSizeRelevant")]
        public bool IsSizeRelevant { get; set; }

        [JsonPropertyName("isSizeOutlier")]
        public bool IsSizeOutlier { get; set; }

        [JsonPropertyName("body")]
        public decimal Body { get; set; }

        [JsonPropertyName("bodyCategory")]
        public string BodyCategory { get; set; } = string.Empty;

        [JsonPropertyName("isBodyRelevant")]
        public bool IsBodyRelevant { get; set; }

        [JsonPropertyName("isBodyOutlier")]
        public bool IsBodyOutlier { get; set; }

        [JsonPropertyName("isStrong")]
        public bool IsStrong { get; set; }

        [JsonPropertyName("bodyPerc")]
        public decimal BodyPerc { get; set; }
    }
}
