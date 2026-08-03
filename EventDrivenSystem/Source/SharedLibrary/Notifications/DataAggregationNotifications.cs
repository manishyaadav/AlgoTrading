using System.Text.Json.Serialization;
using SharedLibrary.Enums.DataFeed;

namespace SharedLibrary.Notifications
{
    public class DataAggregationNotification : NotificationBase
    {
        [JsonPropertyName("ticker")]
        public string Ticker { get; set; } = string.Empty;
        
        [JsonPropertyName("dataType")]
        public DataFeedTypeEnum DataType {get;set;}
        
        [JsonPropertyName("timeframe")]        
        public int? Timeframe { get; set; }

        [JsonPropertyName("time")]
        public DateTime WindowsStartTime { get; set; } // Assuming "time" represents a duration
    }
}