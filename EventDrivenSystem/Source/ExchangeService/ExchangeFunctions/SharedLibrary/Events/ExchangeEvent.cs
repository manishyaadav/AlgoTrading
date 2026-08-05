using SharedLibrary.Enums;
using SharedLibrary.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Events
{
    // No [JsonPropertyName] renaming — see CimplifyBase.cs for why. ExchangeTimerAction was
    // previously renamed to "action" on the wire, which doesn't case-insensitively match
    // "ExchangeTimerAction" at all — it silently deserialized to enum value 0 (not a valid member
    // of ExchangeActionEnum) on the NotificationService side, which then threw
    // "Invalid ExchangeActionEnum value" on every single exchange event and never reached the
    // Redis-cache write. ExchangeName ("name") had the same problem, silently landing as "".
    public class ExchangeEvent : EventBase
    {
        public string ExchangeName { get; set; } = string.Empty;

        public ExchangeActionEnum ExchangeTimerAction { get; set; }

        public string ExchangeTimerActionName { get; set; } = string.Empty;

        public string Date { get; set; } = string.Empty;
    }
}
