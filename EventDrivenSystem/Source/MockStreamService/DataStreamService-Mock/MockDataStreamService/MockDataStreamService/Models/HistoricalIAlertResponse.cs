using MockDataStreamService.Events;
using System.Text.Json.Serialization;

namespace MockDataStreamService.Models
{
    public class HistoricalIAlertResponse
    {
        [JsonPropertyName("totalRecords")]
        public int TotalRecords { get; set; }

        [JsonPropertyName("fullPath")]
        public string FullPath { get; set; } = string.Empty;

        [JsonPropertyName("recods")]
        public List<MockZigZagAlertStreamItem> Records { get; set; } = new List<MockZigZagAlertStreamItem>();
    }
}
