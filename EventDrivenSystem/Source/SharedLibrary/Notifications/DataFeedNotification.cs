using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SharedLibrary.Enums.DataFeed;
using SharedLibrary.Notifications;

namespace SharedLibrary.Notifications
{
    public class DataFeedNotification : NotificationBase
    {
        [JsonPropertyName("ticker")]
        public string SourceToken { get; set; } = string.Empty;

        [JsonPropertyName("dataType")]
        public DataFeedTypeEnum DataType { get; set; }

        [JsonPropertyName("source")]
        public DataFeedSourceEnum DataSource { get; set; }

        public int? Timeframe { get; set; }

        [JsonPropertyName("time")]
        public DateTime WindowsStartTime { get; set; } // Assuming "time" represents a duration
    }
}
