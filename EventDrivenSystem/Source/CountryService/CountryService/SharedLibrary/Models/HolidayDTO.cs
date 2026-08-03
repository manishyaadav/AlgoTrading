using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Models
{
    /// <summary>
    /// Works as the DTO, when getting the data from the blob
    /// </summary>
    public class HolidayDTO
    {
        [JsonPropertyName("holidays")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public DateTime Date { get; set; }
    }
}
