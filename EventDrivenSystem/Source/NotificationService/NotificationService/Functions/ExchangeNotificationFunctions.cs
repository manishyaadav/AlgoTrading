using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NotificationService.RedisConfig;
using SharedLibrary.Caches;
using SharedLibrary.Enums;
using SharedLibrary.Enums.Exchange;
using SharedLibrary.Events.Exchange;

namespace NotificationService.Functions
{
    public class ExchangeNotificationFunctions
    {
        private readonly ILogger<ExchangeNotificationFunctions> _logger;
        private readonly IProducer<string, string> _producer;
        private readonly string _signalRServerUrl;
        private readonly RedisHelper _redisHelper;

        public ExchangeNotificationFunctions(ILogger<ExchangeNotificationFunctions> logger,
                                            IConfiguration configuration,
                                            IProducer<string, string> producer,
                                            RedisHelper redisHelper)
        {
            _logger = logger;
            _producer = producer; // Inject the producer   

            _signalRServerUrl = configuration["SignalRServiceUrl"] ?? "";
            _logger.LogInformation($"SignalR Server URL: {_signalRServerUrl}");

            _redisHelper = redisHelper;          
        }

        [Function("ExchangeNotificationFunction")]
        public async Task Run(
                    [KafkaTrigger("%KAFKA_BROKER_URL%",
                        "live-exchange-workflow-topic",
                        AuthenticationMode = BrokerAuthenticationMode.Plain,
                        ConsumerGroup = "live-exchange-notification-consumer")] string eventDataJson,
                    FunctionContext context)
        {
            _logger.LogInformation("Kafka triggered function: Exchange Notification Starting");

            var eventDataValue = string.Empty;

            var JsonObj = JObject.Parse(eventDataJson);

            if (JsonObj != null)
            {
                if (JsonObj["Value"] != null)
                    eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
            }

            var exchangeEvent = JsonConvert.DeserializeObject<ExchangeEvent>(eventDataValue);
            if (exchangeEvent != null)
            {
                exchangeEvent.CimplifyType = CimplifyTypeEnum.Notification;
                var notification = JsonConvert.SerializeObject(exchangeEvent);

                await SendNotification(notification, "exchange");

                await UpdateRedisCache(exchangeEvent);
                _logger.LogInformation($"C# Kafka trigger function processed a message: {eventDataValue}");
            } 
        }

        private async Task UpdateRedisCache(ExchangeEvent exchangeEvent)
        {
            _logger.LogInformation($"Updating Redis Cache for Exchange: {exchangeEvent.ExchangeName} for Date: {exchangeEvent.Date}");

            string key = $"Exchange:{exchangeEvent.ExchangeName}";
            // get the cache if it's available, otherwise create a new one
            var exchangeCacheJson = await _redisHelper.GetKeyValueFromRedis(key);
            ExchangeCache oldCache = new ExchangeCache();
            if (string.IsNullOrEmpty(exchangeCacheJson) == false)
                oldCache = JsonConvert.DeserializeObject<ExchangeCache>(exchangeCacheJson);

            
            ExchangeCache cacheData = new ExchangeCache()
            {
                Name = exchangeEvent.ExchangeName,
                Date = exchangeEvent.Date,
                UpdatedOn = exchangeEvent.ProducedAt,
                State = GetState (exchangeEvent.ExchangeTimerAction),
                LastUpdateOn = oldCache != null ? oldCache.UpdatedOn : null                
            };
            string value = JsonConvert.SerializeObject(cacheData);
            await _redisHelper.AddToRedis(key, value);

            _logger.LogInformation($"Updated Redis Cache for Exchange: {cacheData.Name} for Date: {cacheData.Date}");
        }

        private ExchangeStateEnum GetState(ExchangeActionEnum action)
        {
            switch (action)
            {
                case ExchangeActionEnum.Init:
                    return ExchangeStateEnum.Initiated;
                case ExchangeActionEnum.PreOpen:
                    return ExchangeStateEnum.PreOpened;
                case ExchangeActionEnum.Open:
                    return ExchangeStateEnum.Opened;
                case ExchangeActionEnum.PreClose:
                    return ExchangeStateEnum.PreClosed;
                case ExchangeActionEnum.Close:
                    return ExchangeStateEnum.Closed;
                default:
                    // Handle unexpected actions
                    throw new ArgumentException("Invalid ExchangeActionEnum value", nameof(action));
            }
        }

        private async Task SendNotification(string message, string hubName)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl($"{_signalRServerUrl}/{hubName}Hub")
                .Build();

            await connection.StartAsync();
            await connection.InvokeAsync("SendMessage", $"{hubName}.service", message);
        }
    }
}
