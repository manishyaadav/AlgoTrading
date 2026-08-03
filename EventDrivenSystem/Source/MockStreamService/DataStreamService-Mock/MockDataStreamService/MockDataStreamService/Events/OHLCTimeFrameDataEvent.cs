using System.Text.Json.Serialization;

namespace MockDataStreamService.Events
{
    public class OHLCTimeFrameDataEvent
    {
        [JsonPropertyName("contractName")]
        public string ContractName { get; set; } = string.Empty;

        [JsonPropertyName("timeframe")]
        public int Timeframe { get; set; }

        [JsonPropertyName("date")]
        public DateTime Date { get; set; }

        [JsonPropertyName("open")]
        public double Open { get; set; }

        [JsonPropertyName("low")]
        public double Low { get; set; }

        [JsonPropertyName("high")]
        public double High { get; set; }

        [JsonPropertyName("close")]
        public double Close { get; set; }

        [JsonPropertyName("volume")]
        public int Volume { get; set; }
    }
}
