using System.Text.Json.Serialization;

namespace StrategyService.Position
{
    // This service's own copy of the 1-min candle payload on live-dataingestion-ohlc-topic (see
    // AggregationService/SharedLibrary/Events/DataIngestion/DataIngestionMinDataEvent.cs) — no shared
    // project reference between services, same established convention. Only the fields
    // PositionExitFunction actually reads.
    public class DataIngestionMinDataEventDto
    {
        [JsonPropertyName("Ticker")]
        public string Ticker { get; set; } = string.Empty;

        [JsonPropertyName("WindowsStartTime")]
        public DateTime WindowsStartTime { get; set; }

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
