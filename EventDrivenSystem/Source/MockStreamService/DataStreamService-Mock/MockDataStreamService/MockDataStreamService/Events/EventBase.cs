using System.Text.Json.Serialization;

namespace MockDataStreamService.Events
{
    public abstract class EventBase
    {
        [JsonPropertyName("eventId")]
        public Guid EventId { get; set; } = Guid.NewGuid();

        [JsonPropertyName("priority")]
        public EventPriority Priority { get; set; } = EventPriority.Medium; // Default priority

        [JsonPropertyName("eventTime")]
        public DateTime EventTime { get; set; }

        [JsonPropertyName("eventProducer")]
        public string EventProducer { get; set; } = "";

        [JsonPropertyName("timeZone")]
        public string TimeZoneId { get; set; } = "Asia/Kolkata";

        [JsonIgnore]
        public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";
    }

    public enum EventPriority
    {
        Low = 1,
        Medium = 2,
        High = 3
    }
}
