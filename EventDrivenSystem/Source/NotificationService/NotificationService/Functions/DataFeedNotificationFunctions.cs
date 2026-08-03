using Microsoft.Azure.Functions.Worker;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NotificationService.RedisConfig;
using Microsoft.AspNetCore.SignalR.Client;
using SharedLibrary.Events;
using SharedLibrary.Notifications;
using SharedLibrary.Enums;
using SharedLibrary.Enums.DataFeed;
using SharedLibrary.Caches;
using SharedLibrary.Helpers;

namespace NotificationService.Functions
{
    internal class DataFeedNotificationFunctions
    {
        private readonly ILogger<DataFeedNotificationFunctions> _logger;
        private readonly IProducer<string, string> _producer;
        private readonly string _signalRServerUrl;
        private readonly RedisHelper _redisHelper;

        public DataFeedNotificationFunctions(ILogger<DataFeedNotificationFunctions> logger,
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

        [Function("DataFeedTradingViewNotificationFunction")]
        public async Task DataFeedTradingViewNotificationFunction(
                    [KafkaTrigger("%KAFKA_BROKER_URL%",
                        "live-tradingview-ohlc-topic",
                        AuthenticationMode = BrokerAuthenticationMode.Plain,
                        ConsumerGroup = "live-tradingview-notification-consumer")] string eventDataJson,
                    FunctionContext context)
        {
            _logger.LogInformation("Kafka triggered function: Trading View Data Feed Notification Starting");

            var eventDataValue = string.Empty;

            var JsonObj = JObject.Parse(eventDataJson);

            if (JsonObj != null)
            {
                if (JsonObj["Value"] != null)
                    eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
            }

            var dataFeedEvent = JsonConvert.DeserializeObject<TradingViewDataEvent>(eventDataValue);
            if (dataFeedEvent != null)
            {
                var newnotification = CreateNotification(dataFeedEvent);

                //exchangeEvent.CimplifyType = CimplifyTypeEnum.Notification;
                var notification = JsonConvert.SerializeObject(newnotification);

                await SendNotification(notification, "datafeed");

                await UpdateRedisCache(dataFeedEvent);
                _logger.LogInformation($"C# Kafka trigger function Trading View processed a message: {eventDataValue}");
            }
        }

        private DataFeedNotification CreateNotification(TradingViewDataEvent datafeedEvent)
        {
            _logger.LogInformation($"Creating notification for : {datafeedEvent.Ticker} for Date: {datafeedEvent.Time} received at: {DateTime.Now.ToLocalTime()}");

            var notificaiton = new DataFeedNotification()
            {
                SourceToken = datafeedEvent.Ticker,
                CimplifyType = CimplifyTypeEnum.Notification,
                DataType = DataFeedTypeEnum.OHLC.ToString(),
                Timeframe = 1,      // currently hardcoding
                DataSource = DataFeedSourceEnum.TradingView.ToString(),
                Producer = "notification.service",
                WindowsStartTime = DateTimeHelper.ToIsoStringWithTime(datafeedEvent.Time)
            };

            return notificaiton;
        }

        private async Task UpdateRedisCache(TradingViewDataEvent dataEvent)
        {
            _logger.LogInformation($"Updating Redis Cache for Min Data: {dataEvent.Ticker} for Date: {dataEvent.Time}");

            string key = $"DataFeed:TradingView:{dataEvent.Ticker}";
            // get the cache if it's available, otherwise create a new one
            var dataFeedCacheJson = await _redisHelper.GetKeyValueFromRedis(key);
            DataFeedCache oldCache = new DataFeedCache();

            oldCache = JsonConvert.DeserializeObject<DataFeedCache>(dataFeedCacheJson);

            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

            DataFeedCache cacheData = new DataFeedCache()
            {
                SourceToken = dataEvent.Ticker,
                DataSource = DataFeedSourceEnum.TradingView.ToString(),
                DataType = DataFeedTypeEnum.OHLC.ToString(),
                WindowsStartTime = DateTimeHelper.ToIsoStringWithTime(dataEvent.Time),
                Timeframe = 1,
                UpdatedOn = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset),
                LastUpdateOn = oldCache != null ? oldCache.UpdatedOn : null 
            };

            string value = JsonConvert.SerializeObject(cacheData);
            await _redisHelper.AddToRedis(key, value);

            _logger.LogInformation($"Updated Redis Cache for Token: {cacheData.SourceToken} for DateTime: {cacheData.WindowsStartTime}");
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
