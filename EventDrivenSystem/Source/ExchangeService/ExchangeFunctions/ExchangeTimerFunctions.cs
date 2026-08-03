using System;
using Confluent.Kafka;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SharedLibrary.Enums;
using SharedLibrary.Events;
using SharedLibrary.Helpers;
using System.Text;

namespace ExchangeFunctions
{
    public class ExchangeTimerFunctions
    {
        private readonly ILogger _logger;
        private readonly IProducer<string, string> _producer;
        private static string _producerTopicName = string.Empty;
        private static string _environmentName = string.Empty;

        public ExchangeTimerFunctions(ILoggerFactory loggerFactory, IProducer<string, string> producer)
        {
            _logger = loggerFactory.CreateLogger<ExchangeTimerFunctions>();
            _producer = producer;
            _producerTopicName = Environment.GetEnvironmentVariable("ProducerTopicName") ?? "";
            _environmentName = Environment.GetEnvironmentVariable("EnvironmentName") ?? "";
        }

        [Function("ExchangeTimerInitFunction")]
        //public async Task Init([TimerTrigger("0 * * * * *")] TimerInfo myTimer)
        public async Task Init([TimerTrigger("0 0 9 * * *")] TimerInfo myTimer)
        {
            DateTime date = DateTime.UtcNow.ToLocalTime();
            _logger.LogInformation($"ExchangeTimerFunction INIT function executed at: {date}");

            if (myTimer.ScheduleStatus is not null) 
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next.ToLocalTime}");
            }

            ExchangeEvent exchangeEventNSE = CreateExchangeEvent(ExchangeActionEnum.Init, "NSE");            
            await ProduceToKafka(_producerTopicName, exchangeEventNSE,  _logger);
            _logger.LogInformation($"Exchange Event INIT: NSE pushed to kafka");

            ExchangeEvent exchangeEventNFO = CreateExchangeEvent(ExchangeActionEnum.Init, "NFO");           
            await ProduceToKafka(_producerTopicName, exchangeEventNFO, _logger);
            _logger.LogInformation($"Exchange Event INIT: NFO pushed to kafka");
        }

        [Function("ExchangeTimerPreOpenFunction")]
        public async Task PreOpen([TimerTrigger("0 7 9 * * *")] TimerInfo myTimer)
        {
            DateTime date = DateTime.UtcNow.ToLocalTime();
            _logger.LogInformation($"ExchangeTimerFunction PREOPEN function executed at: {date}");

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next.ToLocalTime}");
            }

            ExchangeEvent exchangeEventNSE = CreateExchangeEvent(ExchangeActionEnum.PreOpen, "NSE");
            await ProduceToKafka(_producerTopicName, exchangeEventNSE, _logger);
            _logger.LogInformation($"Exchange Event PREOPEN: NSE pushed to kafka");

            ExchangeEvent exchangeEventNFO = CreateExchangeEvent(ExchangeActionEnum.PreOpen, "NFO");
            await ProduceToKafka(_producerTopicName, exchangeEventNFO, _logger);
            _logger.LogInformation($"Exchange Event PREOPEN: NFO pushed to kafka");
        }

        [Function("ExchangeTimerOpenFunction")]
        public async Task Open([TimerTrigger("0 15 9 * * *")] TimerInfo myTimer)
        {
            DateTime date = DateTime.UtcNow.ToLocalTime();
            _logger.LogInformation($"ExchangeTimerFunction OPEN function executed at: {date}");

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next.ToLocalTime}");
            }

            ExchangeEvent exchangeEventNSE = CreateExchangeEvent(ExchangeActionEnum.Open, "NSE");
            await ProduceToKafka(_producerTopicName, exchangeEventNSE, _logger);
            _logger.LogInformation($"Exchange Event OPEN: NSE pushed to kafka");

            ExchangeEvent exchangeEventNFO = CreateExchangeEvent(ExchangeActionEnum.Open, "NFO");
            await ProduceToKafka(_producerTopicName, exchangeEventNFO, _logger);
            _logger.LogInformation($"Exchange Event OPEN: NFO pushed to kafka");
        }

        [Function("ExchangeTimerPreCloseFunction")]
        public async Task PreClose([TimerTrigger("0 15 15 * * *")] TimerInfo myTimer)
        {
            DateTime date = DateTime.UtcNow.ToLocalTime();
            _logger.LogInformation($"ExchangeTimerFunction PRECLOSE function executed at: {date}");

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next.ToLocalTime}");
            }

            ExchangeEvent exchangeEventNSE = CreateExchangeEvent(ExchangeActionEnum.PreClose, "NSE");
            await ProduceToKafka(_producerTopicName, exchangeEventNSE, _logger);
            _logger.LogInformation($"Exchange Event PRECLOSE: NSE pushed to kafka");

            ExchangeEvent exchangeEventNFO = CreateExchangeEvent(ExchangeActionEnum.PreClose, "NFO");
            await ProduceToKafka(_producerTopicName, exchangeEventNFO, _logger);
            _logger.LogInformation($"Exchange Event PRECLOSE: NFO pushed to kafka");
        }

        [Function("ExchangeTimerCloseFunction")]
        public async Task Close([TimerTrigger("0 30 15 * * *")] TimerInfo myTimer)
        {
            DateTime date = DateTime.UtcNow.ToLocalTime();
            _logger.LogInformation($"ExchangeTimerFunction CLOSE function executed at: {date}");

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next.ToLocalTime}");
            }

            ExchangeEvent exchangeEventNSE = CreateExchangeEvent(ExchangeActionEnum.Close, "NSE");
            await ProduceToKafka(_producerTopicName, exchangeEventNSE, _logger);
            _logger.LogInformation($"Exchange Event CLOSE: NSE pushed to kafka");

            ExchangeEvent exchangeEventNFO = CreateExchangeEvent(ExchangeActionEnum.Close, "NFO");
            await ProduceToKafka(_producerTopicName, exchangeEventNFO, _logger);
            _logger.LogInformation($"Exchange Event CLOSE: NFO pushed to kafka");
        }

        private static ExchangeEvent CreateExchangeEvent(ExchangeActionEnum action, string exchangeName)
        {
            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

            return new ExchangeEvent
            {
                Date = DateTimeHelper.ToIsoStringWithoutTime(indianDateTimeOffset),
                ExchangeTimerAction = action,
                ExchangeTimerActionName = action.ToString(),
                ExchangeName = exchangeName,
                Producer = "exchange.service",
                ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset)
            };
        }

        private async Task ProduceToKafka(string topicName, ExchangeEvent eventValue, ILogger logger)
        {
            try
            {
                var key = $"{eventValue.ExchangeName}:{eventValue.Date}";
                var kafkaMessage = JsonSerializer.Serialize(eventValue);
                // Create headers with required values
                var headers = new Headers
                {
                    { "exchange", Encoding.UTF8.GetBytes(eventValue.ExchangeName) } ,
                     { "date", Encoding.UTF8.GetBytes(eventValue.Date) } ,
                    { "environment", Encoding.UTF8.GetBytes(_environmentName) }
                };
                var deliveryReport = await _producer.ProduceAsync
                        (
                            topicName,
                            new Message<string, string>
                            {
                                Key = key,
                                Value = kafkaMessage,
                                Headers = headers
                            }
                        );
                logger.LogInformation($"Delivered message to: {deliveryReport.TopicPartitionOffset}");
            }
            catch (ProduceException<string, string> e)
            {
                logger.LogError($"Delivery failed: {e.Error.Reason}");
            }
        }
    }
}
