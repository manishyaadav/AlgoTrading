using SharedLibrary.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Events
{
    public class EventBase : CimplifyBase
    {
        [JsonPropertyName("cimplifyType")]
        public CimplifyTypeEnum CimplifyType { get; set; } = CimplifyTypeEnum.Event;

        [JsonPropertyName("priority")]
        public EventPriorityEnum Priority { get; set; } = EventPriorityEnum.Medium;

        [JsonPropertyName("producedAt")]
        public string ProducedAt { get; set; } = string.Empty;

        [JsonPropertyName("producerBy")]
        public string Producer { get; set; } = "";
    }
}
