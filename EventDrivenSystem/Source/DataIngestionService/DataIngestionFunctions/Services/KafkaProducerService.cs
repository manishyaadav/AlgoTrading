using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace DataIngestionFunctions.Services
{
    public class KafkaProducerService : IKafkaProducerService
    {
        private readonly IProducer<string, string> _producer;
        private readonly ILogger<KafkaProducerService> _logger;

        public KafkaProducerService(
            IProducer<string, string> producer,
            ILogger<KafkaProducerService> logger)
        {
            _producer = producer;
            _logger = logger;
        }

        public async Task<DeliveryResult<TKey, TValue>> ProduceMessage<TKey, TValue>(
            string topic,
            TKey key,
            TValue value,
            CancellationToken cancellationToken = default)
        {
            return await ProduceMessageWithHeaders<TKey, TValue>(
                topic,
                key,
                value,
                new Headers
                {
                    { "timestamp", BitConverter.GetBytes(DateTime.UtcNow.Ticks) }
                },
                cancellationToken);
        }

        public async Task<DeliveryResult<TKey, TValue>> ProduceMessageWithHeaders<TKey, TValue>(
            string topic,
            TKey key,
            TValue value,
            Headers headers,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var message = new Message<TKey, TValue>
                {
                    Key = key,
                    Value = value,
                    Headers = headers
                };

                var deliveryResult = await _producer.ProduceAsync(topic, message, cancellationToken);

                _logger.LogInformation(
                    "Message delivered to Kafka: {@KafkaDelivery}",
                    new
                    {
                        Topic = deliveryResult.Topic,
                        Partition = deliveryResult.Partition.Value,
                        Offset = deliveryResult.Offset.Value,
                        Key = key,
                        Timestamp = deliveryResult.Timestamp.UtcDateTime
                    });

                return deliveryResult;
            }
            catch (ProduceException<TKey, TValue> ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to deliver message to Kafka: {@KafkaError}",
                    new
                    {
                        Topic = topic,
                        Key = key,
                        ErrorCode = ex.Error.Code,
                        Reason = ex.Error.Reason
                    });
                throw;
            }
        }
    }
}
