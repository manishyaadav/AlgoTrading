using System.Text.Json.Serialization;
using SharedLibrary.Events;

namespace AggregatorFunctions.Indicators
{
    // WARMUP_AND_INDICATOR_PLAN.md section 2e's "Output" spec: one topic per indicator type
    // (live-indicator-ema-topic, live-indicator-supertrend-topic), payload carries Instrument,
    // Timeframe, Period, Multiplier, Value, WindowsStartTime — enough for a consumer to filter to
    // exactly the instance it cares about. Ticker/TimeframeMinutes/Direction added on top: Ticker so
    // a consumer never has to re-derive it via a mapping table just to correlate against the live
    // pipeline, Direction because that's literally what a Supertrend rule compares against
    // ("== GREEN") — null for EMA, where there's no direction, just a value.
    public class IndicatorOutputEvent : EventBase
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

        [JsonPropertyName("WindowsStartTime")]
        public DateTime WindowsStartTime { get; set; }
    }
}
