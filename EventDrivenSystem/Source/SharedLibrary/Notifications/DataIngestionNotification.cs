using System.Text.Json.Serialization;
using SharedLibrary.Enums.DataFeed;

namespace SharedLibrary.Notifications
{
    public class DataIngestionNotification : NotificationBase
    {
        [JsonPropertyName("ticker")]
        public string SourceToken { get; set; } = string.Empty;

        [JsonPropertyName("dataType")]
        public DataFeedTypeEnum DataType {get;set;}

        [JsonPropertyName("source")]
        public DataFeedSourceEnum DataSource {get;set;}
        
        public int? Timeframe { get; set; }

        [JsonPropertyName("time")]
        public DateTime WindowsStartTime { get; set; } // Assuming "time" represents a duration
    }
}