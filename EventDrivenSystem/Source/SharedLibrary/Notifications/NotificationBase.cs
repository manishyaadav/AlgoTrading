using SharedLibrary.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Notifications
{
    public class NotificationBase
    {
        [JsonPropertyName("cimplifyType")]
        public CimplifyTypeEnum CimplifyType { get; set; } = CimplifyTypeEnum.Notification;

        [JsonPropertyName("priority")]
        public NotificationPriorityEnum Priority { get; set; } = NotificationPriorityEnum.Medium;

        [JsonPropertyName("producedAt")]
        public string ProducedAt { get; set; } = string.Empty;

        [JsonPropertyName("producerBy")]
        public string Producer { get; set; } = "";
    }
}
