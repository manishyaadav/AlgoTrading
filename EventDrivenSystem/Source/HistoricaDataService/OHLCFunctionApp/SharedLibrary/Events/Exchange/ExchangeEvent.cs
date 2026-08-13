using SharedLibrary.Enums.Exchange;

namespace SharedLibrary.Events.Exchange
{
    // Deliberately a flat DTO, not the full EventBase/CimplifyBase chain other services duplicate —
    // same rationale as LiveCandleEvent.cs in this project: DailyFileWarmUpFunction and
    // DailyToMonthlyMergeFunction only ever read these 3 fields off live-exchange-workflow-topic.
    //
    // No [JsonPropertyName]/property renaming here — WarmUpService's own copy of this DTO carries an
    // explicit warning that renaming ExchangeTimerAction (e.g. to "action") previously crashed a
    // consumer of this exact event type (see NotificationService/README.md's cross-service JSON
    // contract section). Deserialized with Newtonsoft (JsonConvert.DeserializeObject), which ignores
    // wire fields this DTO doesn't map (CimplifyType, Priority, ProducedAt, Producer, Id,
    // TimeZoneId, Version, ExchangeTimerActionName) rather than failing on them.
    public class ExchangeEvent
    {
        public string ExchangeName { get; set; } = string.Empty;
        public ExchangeActionEnum ExchangeTimerAction { get; set; }
        public string Date { get; set; } = string.Empty; // "yyyy-MM-dd", IST
    }
}
