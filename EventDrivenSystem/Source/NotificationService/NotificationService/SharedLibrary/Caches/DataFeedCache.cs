using System.Text.Json.Serialization;
using SharedLibrary.Enums.DataFeed;

namespace SharedLibrary.Caches
{
    public class DataFeedCache
    {
        [JsonPropertyName("ticker")]
        public string SourceToken { get; set; } = string.Empty;

        [JsonPropertyName("dataType")]
        public string DataType {get;set;} = string.Empty;

        [JsonPropertyName("source")]
        public string DataSource {get;set;} = string.Empty;

        [JsonPropertyName("time")]
        public string WindowsStartTime { get; set; } = string.Empty;

        [JsonPropertyName("frequency")]
        public int? Timeframe {get;set;}
        
        [JsonPropertyName("updatedOn")]
        public string UpdatedOn {get;set;} = string.Empty;

        [JsonPropertyName("lastUpdatedOn")]
        public string LastUpdateOn { get; set; } = string.Empty;
    }        
}