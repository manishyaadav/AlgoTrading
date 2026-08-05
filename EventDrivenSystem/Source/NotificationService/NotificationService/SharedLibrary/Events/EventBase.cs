using SharedLibrary.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Events
{
    // No [JsonPropertyName] renaming — see CimplifyBase.cs for why. "Producer" was previously
    // renamed to "producerBy" on the producer side, which doesn't even case-insensitively match
    // "Producer", so it silently deserialized as empty here for every event.
    public class EventBase : CimplifyBase
    {
        public CimplifyTypeEnum CimplifyType { get; set; } = CimplifyTypeEnum.Event;

        public EventPriorityEnum Priority { get; set; } = EventPriorityEnum.Medium;

        public string ProducedAt { get; set; } = string.Empty;

        public string Producer { get; set; } = "";
    }
}
