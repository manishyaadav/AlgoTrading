using SharedLibrary.Enums.Exchange;
using SharedLibrary.Events;

namespace SharedLibrary.Events.Exchange
{
    // No [JsonPropertyName] renaming — see CimplifyBase.cs for why. This exact mismatch (renaming
    // ExchangeTimerAction to "action") is what crashed notification-live on every exchange event
    // before the fix — see NotificationService/README.md's cross-service JSON contract section.
    public class ExchangeEvent : EventBase
    {
        public string ExchangeName { get; set; } = string.Empty;

        public ExchangeActionEnum ExchangeTimerAction { get; set; }

        public string Date { get; set; } = string.Empty;
    }
}
