using SharedLibrary.Enums;
using SharedLibrary.Enums.Exchange;
using SharedLibrary.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Events.Exchange
{
    // No [JsonPropertyName] renaming — see CimplifyBase.cs for why. This was the actual bug:
    // ExchangeTimerAction was renamed to "action" on the producer side, which doesn't
    // case-insensitively match "ExchangeTimerAction" here, so Newtonsoft silently left it at enum
    // value 0 — not a valid ExchangeActionEnum member — which threw on every exchange event before
    // it ever reached the Redis-cache write.
    public class ExchangeEvent : EventBase
    {
        public string ExchangeName { get; set; } = string.Empty;

        public ExchangeActionEnum ExchangeTimerAction { get; set; }

        public string Date { get; set; } = string.Empty;
    }
}
