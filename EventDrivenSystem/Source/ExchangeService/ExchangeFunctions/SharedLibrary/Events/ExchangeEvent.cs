using SharedLibrary.Enums;
using SharedLibrary.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Events
{
    public class ExchangeEvent : EventBase
    {
        [JsonPropertyName("name")]
        public string ExchangeName { get; set; } = string.Empty;

        [JsonPropertyName("action")]
        public ExchangeActionEnum ExchangeTimerAction { get; set; }

        [JsonPropertyName("actionName")]
        public string ExchangeTimerActionName { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;
    }
}
