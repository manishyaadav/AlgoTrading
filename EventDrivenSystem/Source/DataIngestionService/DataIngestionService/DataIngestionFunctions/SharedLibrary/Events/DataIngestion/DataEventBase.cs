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
        // No [JsonPropertyName] renaming — see CimplifyBase.cs for why. "sourceName"/"dataType"
        // don't case-insensitively match DataSource/DataType, so Newtonsoft on the consumer side
        // silently deserialized these to enum default 0 (not a valid DataFeedSourceEnum member).
        public DataFeedSourceEnum DataSource { get; set; }

        public DataFeedTypeEnum DataType { get; set; }
    }
}
