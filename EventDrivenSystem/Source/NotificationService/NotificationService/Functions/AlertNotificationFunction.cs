using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using NotificationService.RedisConfig;
using SharedLibrary.Caches;
using SharedLibrary.Enums;
using SharedLibrary.Events.Country;
using SharedLibrary.Helpers;

namespace NotificationService.Functions
{
    public class AlertNotificationFunction
    {
        private readonly ILogger<AlertNotificationFunction> _logger;
        private readonly IProducer<string, string> _producer;
        private readonly string _signalRServerUrl;
        private readonly RedisHelper _redisHelper;

        public AlertNotificationFunction(ILogger<AlertNotificationFunction> logger,
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

        [Function("AlertNotificationFunction")]
        public async Task Run(
                    [KafkaTrigger("%KAFKA_BROKER_URL%",
                        "live-tradingview-alert-topic",
                        AuthenticationMode = BrokerAuthenticationMode.Plain,
                        ConsumerGroup = "live-alert-notification-consumer")] string eventDataJson,
                    FunctionContext context)
        {
            _logger.LogInformation("Kafka triggered function: Alert  Notification Starting");

            var eventDataValue = string.Empty;

            var JsonObj = JObject.Parse(eventDataJson);

            if (JsonObj != null)
            {
                if (JsonObj["Value"] != null)
                    eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
            }

            var countryEvent = JsonConvert.DeserializeObject<CountryEvent>(eventDataValue);
            if (countryEvent != null)
            {
                countryEvent.CimplifyType = CimplifyTypeEnum.Notification;
                var notification = JsonConvert.SerializeObject(countryEvent);

                await SendNotification(notification, "country");

                await UpdateRedisCache(countryEvent);
                _logger.LogInformation($"C# Kafka trigger function processed a message: {eventDataValue}");
            }
        }

        private async Task UpdateRedisCache(CountryEvent countryEvent)
        {
            _logger.LogInformation($"Updating Redis Cache for Country: {countryEvent.Name} for Date: {countryEvent.Date}");

            // get the cache if it's available, otherwise create a new one
            var countryCacheJson = await _redisHelper.GetKeyValueFromRedis(countryEvent.Name);
            CountryCache oldCache = new CountryCache();
            if (string.IsNullOrEmpty(countryCacheJson) == false)
                oldCache = JsonConvert.DeserializeObject<CountryCache>(countryCacheJson);

            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

            string key = countryEvent.Name;
            CountryCache cacheData = new CountryCache()
            {
                Name = countryEvent.Name,
                Date = countryEvent.Date,
                UpdatedOn = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset),
                State = countryEvent.State.ToString(),
                LastUpdateOn = oldCache != null ? oldCache.UpdatedOn : null,
                Holiday = countryEvent.Holiday != null ? new HolidayItem() { Date = countryEvent.Holiday.Date, Reason = countryEvent.Holiday.Reason } : null,
                NextHoliday = countryEvent.NextHoliday != null ? new HolidayItem() { Date = countryEvent.NextHoliday.Date, Reason = countryEvent.NextHoliday.Reason } : null,
            };
            string value = JsonConvert.SerializeObject(cacheData);
            await _redisHelper.AddToRedis(key, value);

            _logger.LogInformation($"Updated Redis Cache for Country: {countryEvent.Name} for Date: {countryEvent.Date}");
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
