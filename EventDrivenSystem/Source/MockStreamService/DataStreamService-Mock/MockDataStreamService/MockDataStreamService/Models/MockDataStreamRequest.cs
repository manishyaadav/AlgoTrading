using MockDataStreamService.Validators;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Text.Json.Serialization;

namespace MockDataStreamService.Models
{
    public class MockDataStreamRequest
    {
        public List<MockDataStreamItem> RequestData { get; set; }
    }

    public class MockDataStreamItem
    {
        [JsonPropertyName("streamType")]
        [Required]
        [Range(1, 2, ErrorMessage = "StreamType must be 1 or 2")]
        public StreamType StreamType { get; set; }

        [JsonPropertyName("dataType")]
        [Required]
        [Range(1, 2, ErrorMessage = "DataType must be 1 or 2")]
        public DataType DataType { get; set; }

        [JsonPropertyName("exchangeName")]
        [Required]
        [RegularExpression("^(nse|nfo)$", ErrorMessage = "ExchangeName must be 'nse' or 'nfo' (case-insensitive)")]
        public string ExchangeName { get; set; }

        [JsonPropertyName("partialInstrumentName")]
        [Required] // Ensure StreamType is always present
        public string PartialInstrumentName { get; set; }

        [JsonPropertyName("producerFrequencyInSeconds")]
        [Required]
        [Range(1, 59, ErrorMessage = "ProducerFrequencyInSeconds must be between 1 and 59")]
        public int ProducerFrequencyInSeconds { get; set; }

        [JsonPropertyName("year")]
        [Required]
        [ValidYear] // Apply our custom attribute
        public int Year { get; set; }

        [JsonPropertyName("month")] // Preserves case as-is
        [Required]
        [Range(1, 12, ErrorMessage = "Month must be between 1 and 12")]
        public int month { get; set; }

        
    }

    public enum StreamType
    {
        Ohlc = 1,
        Tick = 2
    }

    public enum DataType
    {
        OHLC = 1,
        Alert = 2
    }
}
