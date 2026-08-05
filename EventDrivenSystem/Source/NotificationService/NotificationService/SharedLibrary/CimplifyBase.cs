using SharedLibrary.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary
{
    // No [JsonPropertyName] renaming here on purpose — see the matching comment in
    // ExchangeService's copy of this file. This side deserializes with Newtonsoft.Json, which
    // ignores System.Text.Json's JsonPropertyNameAttribute and matches the plain C# member name
    // case-insensitively instead, so keeping names unrenamed is what makes producer/consumer agree.
    public class CimplifyBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string TimeZoneId { get; set; } = "Asia/Kolkata";

        public string Version { get; set; } = "1.0";
    }
}
