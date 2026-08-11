using System;
using System.Text.Json.Serialization;

namespace OHLCFunctionApp
{
    // Deliberately not the full DataEventBase/EventBase/CimplifyBase inheritance chain other
    // services duplicate per-project — this consumer only ever reads these 6 fields off
    // live-dataingestion-ohlc-topic, so a flat DTO avoids pulling in three more enum files for
    // properties (CimplifyType, Priority, DataSource as an enum) it never uses.
    internal class LiveCandleEvent
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
