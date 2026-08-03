using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace May6StreamAnalytics
{
    internal class AggregationRequest
    {
        [JsonPropertyName("aggregationRequestItems")] // Optional, but good for clarity
        public List<AggregationRequestItem> AggregationRequestItem { get; set; }
    }

    public class AggregationRequestItem
    {
        [JsonPropertyName("exchangeName")]
        [Required]
        [RegularExpression("^(nse|nfo)$", ErrorMessage = "ExchangeName must be 'nse' or 'nfo' (case-insensitive)")]
        public string ExchangeName { get; set; }

        [JsonPropertyName("partialInstrumentName")]
        public string PartialInstrumentName { get; set; }

        [JsonPropertyName("producerFrequencyInMins")] // More consistent JSON naming 
        [Required]
        [Range(1, 59, ErrorMessage = "ProducerFrequencyInMins must be between 1 and 59")]
        public int ProducerFrequencyInMins { get; set; }
    }
}
