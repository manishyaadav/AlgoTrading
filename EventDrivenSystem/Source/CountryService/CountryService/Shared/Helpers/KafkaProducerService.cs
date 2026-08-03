// using Confluent.Kafka;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Options;
// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text;
// using System.Threading.Tasks;

// namespace CountryService.Shared.Helpers
// {
//     public class KafkaProducerService
//     {
//         private readonly IProducer<Null, string> _producer;
//         private readonly ILogger<KafkaProducerService> _logger;


//         public KafkaProducerService(IOptions<KafkaConfigOptions> kafkaConfigOptions, ILogger<KafkaProducerService> logger)
//         {
//             var config = new ProducerConfig
//             {
//                 BootstrapServers = kafkaConfigOptions.Value.BootstrapServers,
//                 // add other kafka configuraiton settings as needed
//             };
//             _producer = new ProducerBuilder<Null, string>(config).Build();
//             _logger = logger;
//         }

//         public async Task ProduceAsync(string topic, string message)
//         {
//             try
//             {
//                 var deliveryResult = await _producer.ProduceAsync(topic, new Message<Null, string> { Value = message });
//             }
//             catch (ProduceException<Null, string> e)
//             {
//                 _logger.LogError($"Delivery failed: {e.Error.Reason}");
//             }
//         }

//         private async Task ProduceToKafka(string topicName, string message, ILogger logger)
//         {
//             try
//             {
//                 var deliveryReport = await _producer.ProduceAsync(topicName, new Message<Null, string> { Value = message });
//                 logger.LogInformation($"Delivered message to: {deliveryReport.TopicPartitionOffset}");
//             }
//             catch (ProduceException<Null, string> e)
//             {
//                 logger.LogError($"Delivery failed: {e.Error.Reason}");
//             }
//         }
//     }
// }
