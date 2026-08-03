using SharedLibrary.Enums;
using SharedLibrary.Enums.DataFeed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Events.DataIngestion
{
    public class DataEventBase : EventBase
    {
        [JsonPropertyName("sourceName")]
        public DataFeedSourceEnum DataSource { get; set; }

        [JsonPropertyName("dataType")]
        public DataFeedTypeEnum DataType { get; set; }
    }
}
