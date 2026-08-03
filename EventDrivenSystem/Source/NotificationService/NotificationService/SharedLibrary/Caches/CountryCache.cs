using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SharedLibrary.Enums.Country;
using SharedLibrary.Events.Country;

namespace SharedLibrary.Caches
{
    public class CountryCache
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("holiday")]
        public HolidayItem? Holiday { get; set; }

        [JsonPropertyName("nextHoliday")]
        public HolidayItem? NextHoliday { get; set; }

        [JsonPropertyName("updatedOn")]
        public string UpdatedOn {get;set;} = string.Empty;

        [JsonPropertyName("lastUpdatedOn")]
        public string LastUpdateOn { get; set; } = string.Empty;
    }
}
