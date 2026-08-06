using SharedLibrary.Enums;

namespace SharedLibrary.Events
{
    // No [JsonPropertyName] renaming — see CimplifyBase.cs for why.
    public class EventBase : CimplifyBase
    {
        public CimplifyTypeEnum CimplifyType { get; set; } = CimplifyTypeEnum.Event;

        public EventPriorityEnum Priority { get; set; } = EventPriorityEnum.Medium;

        public string ProducedAt { get; set; } = string.Empty;

        public string Producer { get; set; } = "";
    }
}
