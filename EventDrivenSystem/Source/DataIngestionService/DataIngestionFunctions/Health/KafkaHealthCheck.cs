using Microsoft.Extensions.Diagnostics.HealthChecks;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace DataIngestionFunctions.Health
{
    public class KafkaHealthCheck : IHealthCheck
    {
        private readonly IProducer<string, string> _producer;
        private readonly string _topic;

        public KafkaHealthCheck(
            IProducer<string, string> producer,
            IOptions<KafkaSettings> settings)
        {
            _producer = producer;
            _topic = settings.Value.HealthCheckTopic;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var metadata = _producer.GetMetadata(_topic, TimeSpan.FromSeconds(5));
                
                if (metadata.Topics.Any(t => t.Topic == _topic))
                {
                    return HealthCheckResult.Healthy($"Successfully connected to Kafka and found topic {_topic}");
                }
                
                return HealthCheckResult.Degraded($"Topic {_topic} not found");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Failed to connect to Kafka", ex);
            }
        }
    }
}
