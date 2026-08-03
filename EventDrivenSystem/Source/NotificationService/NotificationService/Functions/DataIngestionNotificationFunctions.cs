using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NotificationService.RedisConfig;
using SharedLibrary.Caches;
using SharedLibrary.Enums;
using SharedLibrary.Enums.DataFeed;
using SharedLibrary.Events.DataIngestion;
using SharedLibrary.Helpers;
using SharedLibrary.Notifications;

namespace NotificationService.Functions
{
    public class DataIngestionNotificationFunctions
    {
        private readonly ILogger<DataIngestionNotificationFunctions> _logger;
        private readonly IProducer<string, string> _producer;
        private readonly string _signalRServerUrl;
        private readonly RedisHelper _redisHelper;

        public DataIngestionNotificationFunctions(ILogger<DataIngestionNotificationFunctions> logger,
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

        [Function("DataIngestionNotificationFunction")]
        public async Task DataIngestionNotificationFunction(
                    [KafkaTrigger("%KAFKA_BROKER_URL%",
                        "live-dataingestion-ohlc-topic",
                        AuthenticationMode = BrokerAuthenticationMode.Plain,
                        ConsumerGroup = "live-dataingestion-notification-consumer")] string eventDataJson,
                    FunctionContext context)
        {
            _logger.LogInformation("Kafka triggered function: Data Ingestion Notification Starting");

            var eventDataValue = string.Empty;

            var JsonObj = JObject.Parse(eventDataJson);

            if (JsonObj != null)
            {
                if (JsonObj["Value"] != null)
                    eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
            }

            var dataIngestionEvent = JsonConvert.DeserializeObject<DataIngestionMinDataEvent>(eventDataValue);
            if (dataIngestionEvent != null)
            {
                var newnotification = CreateNotification(dataIngestionEvent);

                //exchangeEvent.CimplifyType = CimplifyTypeEnum.Notification;
                var notification = JsonConvert.SerializeObject(newnotification);

                await SendNotification(notification, "dataingestion");

                await UpdateRedisCache(dataIngestionEvent);
                _logger.LogInformation($"C# Kafka trigger function processed a message: {eventDataValue}");
            }
            _logger.LogInformation("Kafka triggered function: Data Ingestion Notification SUCCESSFULLY PROCESSED");
        }

        private DataIngestionNotification CreateNotification(DataIngestionMinDataEvent dataIngestionEvent)
        {
            _logger.LogInformation($"Creating notification for : {dataIngestionEvent.Ticker} for Date: {dataIngestionEvent.WindowsStartTime} received at: {DateTime.Now.ToLocalTime()}");

            var notificaiton = new DataIngestionNotification()
            {
                SourceToken = dataIngestionEvent.Ticker,
                CimplifyType = CimplifyTypeEnum.Notification,
                DataType = DataFeedTypeEnum.OHLC.ToString(),
                Timeframe = 1,      // currently hardcoding
                DataSource = DataFeedSourceEnum.TradingView.ToString(),
                Producer = "notification.service",
                WindowsStartTime = DateTimeHelper.ToIsoStringWithTime(dataIngestionEvent.WindowsStartTime)
            };

            return notificaiton;
        }

        private async Task UpdateRedisCache(DataIngestionMinDataEvent dataEvent)
        {
            _logger.LogInformation($"Updating Redis Cache for Min Data: {dataEvent.Ticker} for Date: {dataEvent.WindowsStartTime}");

            string key = $"DataIngestion:TradingView:{dataEvent.Ticker}";
            // get the cache if it's available, otherwise create a new one
            var dataIngestionCacheJson = await _redisHelper.GetKeyValueFromRedis(key);
            DataIngestionCache oldCache = new DataIngestionCache();

            oldCache = JsonConvert.DeserializeObject<DataIngestionCache>(dataIngestionCacheJson);

            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);
            
            DataIngestionCache cacheData = new DataIngestionCache()
            {
                SourceToken = dataEvent.Ticker,
                DataSource = DataFeedSourceEnum.TradingView.ToString(),
                DataType = DataFeedTypeEnum.OHLC.ToString(),
                WindowsStartTime = DateTimeHelper.ToIsoStringWithTime(dataEvent.WindowsStartTime),
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