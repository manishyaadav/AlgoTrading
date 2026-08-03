using System.Text.Json.Serialization;

namespace MockDataStreamService.Events
{
    // Example TradingViewAlertEvent class (adjust properties as needed)
    public class TradingViewAlertEvent
    {
        public int Id { get; set; }
        public string SourceToken { get; set; }
        public string Ticker { get; set; }
        public int Timeframe { get; set; }
        public int LookBackPeriod { get; set; }
        public int MinBodyStrength { get; set; }
        public int Q3Multiplier { get; set; }
        public string AlertType { get; set; }
        public int Level { get; set; }
        public int Length { get; set; }
        public int Direction { get; set; }
        public decimal PointVal { get; set; }
        public decimal BodyStrength { get; set; }
        public decimal Q3Value { get; set; }
        public DateTime ProducedAt { get; set; }
        public string ProducedBy { get; set; }
        public DateTime WindowsStartTime { get; set; }
        public DateTime InsertedDate { get; set; }
        public string Version { get; set; }
        // ... other properties ...
    }
}
