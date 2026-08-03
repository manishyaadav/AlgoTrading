using Confluent.Kafka;

namespace DataIngestionFunctions.Services
{
    public interface IKafkaProducerService
    {
        Task<DeliveryResult<TKey, TValue>> ProduceMessage<TKey, TValue>(
            string topic, 
            TKey key, 
            TValue value, 
            CancellationToken cancellationToken = default);
        
        Task<DeliveryResult<TKey, TValue>> ProduceMessageWithHeaders<TKey, TValue>(
            string topic,
            TKey key,
            TValue value,
            Headers headers,
            CancellationToken cancellationToken = default);
    }
}
