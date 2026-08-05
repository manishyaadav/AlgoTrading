using SharedLibrary.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary
{
    // No [JsonPropertyName] renaming here on purpose: this type crosses a Kafka message boundary
    // between services that serialize with System.Text.Json (producers) and deserialize with
    // Newtonsoft.Json (NotificationService's consumers). Newtonsoft ignores System.Text.Json's
    // JsonPropertyNameAttribute entirely and falls back to matching the plain C# member name
    // case-insensitively — so a rename here (e.g. TimeZoneId -> "timeZone") silently fails to bind
    // on the consumer side and the property is left at its default value with no error. Keeping
    // property names as-is (relying on Newtonsoft's case-insensitive default match) avoids that
    // whole class of bug. See ExchangeEvent.ExchangeTimerAction for a case where this actually broke.
    public class CimplifyBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string TimeZoneId { get; set; } = "Asia/Kolkata";

        public string Version { get; set; } = "1.0";
    }
}
