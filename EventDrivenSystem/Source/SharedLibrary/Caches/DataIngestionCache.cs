using System.Text.Json.Serialization;
using SharedLibrary.Enums.DataFeed;

namespace SharedLibrary.Caches
{
    public class DataIngestionCache
    {
        [JsonPropertyName("ticker")]
        public string SourceToken { get; set; } = string.Empty;

        [JsonPropertyName("dataType")]
        public DataFeedTypeEnum DataType {get;set;}

        [JsonPropertyName("source")]
        public DataFeedSourceEnum DataSource {get;set;}

        [JsonPropertyName("time")]
        public DateTime Time { get; set; }

        [JsonPropertyName("frequency")]
        public int? Timeframe {get;set;}
        
        [JsonPropertyName("updatedOn")]
        public DateTime? UpdatedOn {get;set;}

        [JsonPropertyName("lastUpdatedOn")]
        public DateTime? LastUpdateOn { get; set; }
    }        
}