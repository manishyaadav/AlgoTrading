using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SharedLibrary.Enums.Country;
using SharedLibrary.Enums.DataFeed;
using SharedLibrary.Enums.Exchange;
using SharedLibrary.Notifications;

namespace SharedLibrary.Notifications
{
    public class ExchangeNotification : NotificationBase
    {
        [JsonPropertyName("name")]
        public string ExchangeName { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;
    }
}
