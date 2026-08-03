using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary
{
    public class CimplifyBase
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("timeZone")]
        public string TimeZoneId { get; set; } = "Asia/Kolkata";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";
    }
}
