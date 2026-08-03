using System.Text.Json.Serialization;
using SharedLibrary.Enums.DataFeed;

namespace SharedLibrary.Caches
{
    public class DataAggregationCache
    {
        [JsonPropertyName("ticker")]
        public string Ticker { get; set; } = string.Empty;

        [JsonPropertyName("dataType")]
        public string DataType {get;set;} = string.Empty;

        [JsonPropertyName("time")]
        public string WindowStartTime { get; set; } = string.Empty;

        [JsonPropertyName("frequency")]
        public int? Timeframe {get;set;}
        
        [JsonPropertyName("updatedOn")]
        public string UpdatedOn {get;set;} = string.Empty;

        [JsonPropertyName("lastUpdatedOn")]
        public string LastUpdateOn { get; set; } = string.Empty;
    }

}