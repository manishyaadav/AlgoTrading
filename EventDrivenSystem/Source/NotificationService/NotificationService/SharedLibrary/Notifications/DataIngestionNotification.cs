using System.Text.Json.Serialization;
using SharedLibrary.Enums.DataFeed;

namespace SharedLibrary.Notifications
{
    public class DataIngestionNotification : NotificationBase
    {
        [JsonPropertyName("ticker")]
        public string SourceToken { get; set; } = string.Empty;

        [JsonPropertyName("dataType")]
        public string DataType {get;set;} = string.Empty;

        [JsonPropertyName("source")]
        public string DataSource {get;set;} = string.Empty;
        
        public int? Timeframe { get; set; }

        [JsonPropertyName("time")]
        public string WindowsStartTime { get; set; } = string.Empty;
    }
}