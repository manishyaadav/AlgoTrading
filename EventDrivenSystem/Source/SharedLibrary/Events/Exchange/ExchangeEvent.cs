using SharedLibrary.Enums;
using SharedLibrary.Enums.Exchange;
using SharedLibrary.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Events.Exchange
{
    public class ExchangeEvent : EventBase
    {
        [JsonPropertyName("name")]
        public string ExchangeName { get; set; } = string.Empty;

        [JsonPropertyName("action")]
        public ExchangeActionEnum ExchangeTimerAction { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;
    }
}
