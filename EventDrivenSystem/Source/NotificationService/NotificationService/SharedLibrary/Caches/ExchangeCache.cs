using System.Text.Json.Serialization;
using SharedLibrary.Enums.Exchange;

namespace SharedLibrary.Caches
{
    public class ExchangeCache
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public ExchangeStateEnum State { get; set; }
        
        [JsonPropertyName("updatedOn")]
        public string UpdatedOn {get;set;} = string.Empty;

        [JsonPropertyName("lastUpdatedOn")]
        public string LastUpdateOn { get; set; } = string.Empty;
    }
        
}