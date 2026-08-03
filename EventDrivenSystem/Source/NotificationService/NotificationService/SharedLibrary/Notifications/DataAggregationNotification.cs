using System.Text.Json.Serialization;
using SharedLibrary.Enums.DataFeed;

namespace SharedLibrary.Notifications
{
    public class DataAggregationNotification : NotificationBase
    {
        [JsonPropertyName("ticker")]
        public string Ticker { get; set; } = string.Empty;
        
        [JsonPropertyName("dataType")]
        public string DataType {get;set;} = string.Empty;
        
        [JsonPropertyName("timeframe")]        
        public int? Timeframe { get; set; }

        [JsonPropertyName("time")]
        public string WindowsStartTime { get; set; } = string.Empty;
    }
}