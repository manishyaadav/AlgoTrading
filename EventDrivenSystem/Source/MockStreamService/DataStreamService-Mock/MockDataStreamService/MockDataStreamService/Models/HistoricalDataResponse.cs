using MockDataStreamService.Events;
using System.Text.Json.Serialization;

namespace MockDataStreamService.Models
{
    public class HistoricalDataResponse
    {
        [JsonPropertyName("totalRecords")]
        public int TotalRecords { get; set; }

        [JsonPropertyName("fullPath")]
        public string FullPath { get; set; } = string.Empty;

        [JsonPropertyName("recods")]
        public List<OHLCTimeFrameDataEvent> Records { get; set; } = new List<OHLCTimeFrameDataEvent>();
    }
}
