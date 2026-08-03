using MockDataStreamService.Validators;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MockDataStreamService.Models
{
    public class MockZigZagAlertStreamRequest
    {
        public List<MockZigZagAlertStreamItem> RequestData { get; set; }
    }

    public class MockZigZagAlertStreamItem
    {
        [JsonPropertyName("tickerName")]
        [Required]        
        public string TickerName { get; set; }

        [JsonPropertyName("timeframe")]
        [Required]
        [Range(1, 59, ErrorMessage = "Timeframe must be between 1 and 59")]
        public int Timeframe { get; set; }

        [JsonPropertyName("lookBackPeriod")]
        [Required]
        [Range(1, 200, ErrorMessage = "LookBackPeriod must be between 1 and 200")]
        public int LookBackPeriod { get; set; }

        [JsonPropertyName("minBodyStrength")]
        [Required]
        [Range(1, 100, ErrorMessage = "MinBodyStrength must be between 1 and 100")]
        public int MinBodyStrength { get; set; }

        [JsonPropertyName("q3Multiplier")]
        [Required]        
        public decimal Q3Multiplier { get; set; }

        [JsonPropertyName("level")]
        [Required]
        [Range(0, 5, ErrorMessage = "Level must be between 0 and 5")]
        public int Level { get; set; }

        [JsonPropertyName("length")]
        [Required]
        [Range(2, 10, ErrorMessage = "Length must be between 2 and 10")]
        public int Length { get; set; }

        [JsonPropertyName("version")]
        [Required] 
        public string Version { get; set; } = string.Empty;

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
}
