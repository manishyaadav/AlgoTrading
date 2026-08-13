using System.Text.Json.Serialization;

namespace StrategyService.Position
{
    // This service's own copy of AggregationService's IndicatorOutputEvent.cs — no shared project
    // reference between services (established convention; see OHLCFunctionApp/LiveCandleEvent.cs's
    // own doc comment for the same rationale). Only the fields PositionEntryFunction actually reads.
    public class IndicatorOutputEventDto
    {
        [JsonPropertyName("Instrument")]
        public string Instrument { get; set; } = string.Empty;

        [JsonPropertyName("Ticker")]
        public string Ticker { get; set; } = string.Empty;

        [JsonPropertyName("Timeframe")]
        public string Timeframe { get; set; } = string.Empty;

        [JsonPropertyName("TimeframeMinutes")]
        public int TimeframeMinutes { get; set; }

        [JsonPropertyName("Reference")]
        public string Reference { get; set; } = string.Empty;

        [JsonPropertyName("Period")]
        public int Period { get; set; }

        [JsonPropertyName("Multiplier")]
        public int Multiplier { get; set; }

        [JsonPropertyName("Value")]
        public decimal Value { get; set; }

        [JsonPropertyName("Direction")]
        public string? Direction { get; set; }

        [JsonPropertyName("PreviousDirection")]
        public string? PreviousDirection { get; set; }

        [JsonPropertyName("WindowsStartTime")]
        public DateTime WindowsStartTime { get; set; }
    }
}
