using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SharedLibrary.Events;

namespace SharedLibrary.Enums.Exchange
{
    public class CountryBase : EventBase
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "India";

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "Rs";
    }
}
