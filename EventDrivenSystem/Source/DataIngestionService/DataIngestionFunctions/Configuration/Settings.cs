namespace DataIngestionFunctions.Configuration
{
    public class KafkaSettings
    {
        public string BootstrapServers { get; set; } = string.Empty;
        public TopicSettings Topics { get; set; } = new();
        public RetrySettings RetrySettings { get; set; } = new();
        
        public class TopicSettings
        {
            public string TradingViewData { get; set; } = string.Empty;
            public string HealthCheck { get; set; } = string.Empty;
        }
        
        public class RetrySettings
        {
            public int MaxRetries { get; set; }
            public int RetryIntervalMs { get; set; }
        }
    }

    public class RateLimitSettings
    {
        public int MaxRequestsPerMinute { get; set; }
        public int MaxConcurrentRequests { get; set; }
    }

    public class MonitoringSettings
    {
        public bool MetricsEnabled { get; set; }
        public string LogLevel { get; set; } = "Information";
    }

    public class HealthCheckSettings
    {
        public bool Enabled { get; set; }
        public int IntervalSeconds { get; set; }
    }
}
