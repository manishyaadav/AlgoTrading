using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TopicToCSVConverter.Events
{
    internal class TradingViewMinDataEvent : EventBase
    {
        private DateTime _time;
        private string _exchange = string.Empty;

        [JsonPropertyName("exchange")]
        public string Exchange { get; set; } = string.Empty;

        [JsonPropertyName("ticker")]
        public required string Ticker { get; set; }
        [JsonPropertyName("timeframe")]
        public int Timeframe { get; set; }
        [JsonPropertyName("open")]
        public decimal Open { get; set; }
        [JsonPropertyName("high")]
        public decimal High { get; set; }
        [JsonPropertyName("low")]
        public decimal Low { get; set; }
        [JsonPropertyName("close")]
        public decimal Close { get; set; }

        [JsonPropertyName("volume")]
        public decimal Volume { get; set; }        

        [JsonPropertyName("time")]
        public DateTime Time { set; get; }        
        
    }
}
