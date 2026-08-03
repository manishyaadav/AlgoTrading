using SharedLibrary.Enums.AlertFeed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Events.AlertIngestion
{
    public class AlertEventBase : EventBase
    {
        [JsonPropertyName("sourceName")]
        public AlertFeedSourceEnum DataSource { get; set; }

        [JsonPropertyName("dataType")]
        public AlertFeedTypeEnum DataType { get; set; }
    }
}
