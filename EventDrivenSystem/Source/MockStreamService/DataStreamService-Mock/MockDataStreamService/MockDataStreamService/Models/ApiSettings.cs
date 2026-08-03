namespace MockDataStreamService.Models
{
    public class ApiSettings
    {
        public string ApiUrl { get; set; }
        public string BootstrapServer { get; set; }
        public string OhlcTopicName { get; set; }
        public string TickTopicName { get; set; }
        public string SQLServerConnectionString { get; set; }
    }
}
