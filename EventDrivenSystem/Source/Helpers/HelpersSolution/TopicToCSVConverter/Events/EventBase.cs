using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TopicToCSVConverter.Events
{
    public abstract class EventBase
    {
        [JsonPropertyName("eventId")]
        public Guid EventId { get; set; } = Guid.NewGuid();

        [JsonPropertyName("priority")]
        public EventPriority Priority { get; set; } = EventPriority.Medium; // Default priority

        [JsonPropertyName("eventTime")]
        public DateTime EventTime { get; set; } = DateTime.Now;

        [JsonPropertyName("eventProducer")]
        public string EventProducer { get; set; } = "";

        [JsonPropertyName("timeZone")]
        public string TimeZoneId { get; set; } = "Asia/Kolkata";

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
