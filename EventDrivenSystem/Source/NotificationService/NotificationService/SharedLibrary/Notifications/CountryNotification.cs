using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SharedLibrary.Enums.Country;
using SharedLibrary.Enums.DataFeed;
using SharedLibrary.Notifications;

namespace SharedLibrary.Notifications
{
    public class CountryNotification : NotificationBase
    {
        [JsonPropertyName("name")]
        public string CountryName { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public CountryStateEnum State { get; set; }
    }
}
