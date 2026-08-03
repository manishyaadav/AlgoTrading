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
        public string DataType { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string DataSource { get; set; } = string.Empty;

        public int? Timeframe { get; set; }

        [JsonPropertyName("time")]
        public string WindowsStartTime { get; set; } = string.Empty;
    }
}
