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
        public DateTime? UpdatedOn {get;set;}

        [JsonPropertyName("lastUpdatedOn")]
        public DateTime? LastUpdateOn { get; set; }
    }
        
}