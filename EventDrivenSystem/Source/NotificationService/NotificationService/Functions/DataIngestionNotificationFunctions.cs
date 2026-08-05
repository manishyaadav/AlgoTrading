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

        // dataEvent.DataSource now deserializes correctly (see DataEventBase.cs), but guard against
        // stale/malformed messages still landing with the enum's unnamed default (0) — falls back to
        // "Unknown" rather than silently mislabeling everything as TradingView again.
        private static string ResolveDataSource(DataFeedSourceEnum dataSource) =>
            Enum.IsDefined(typeof(DataFeedSourceEnum), dataSource) ? dataSource.ToString() : "Unknown";

        private DataIngestionNotification CreateNotification(DataIngestionMinDataEvent dataIngestionEvent)
        {
            _logger.LogInformation($"Creating notification for : {dataIngestionEvent.Ticker} for Date: {dataIngestionEvent.WindowsStartTime} received at: {DateTime.Now.ToLocalTime()}");

            var notificaiton = new DataIngestionNotification()
            {
                SourceToken = dataIngestionEvent.Ticker,
                CimplifyType = CimplifyTypeEnum.Notification,
                DataType = DataFeedTypeEnum.OHLC.ToString(),
                Timeframe = 1,      // currently hardcoding
                DataSource = ResolveDataSource(dataIngestionEvent.DataSource),
                Producer = "notification.service",
                WindowsStartTime = DateTimeHelper.ToIsoStringWithTime(dataIngestionEvent.WindowsStartTime)
            };

            return notificaiton;
        }

        private async Task UpdateRedisCache(DataIngestionMinDataEvent dataEvent)
        {
            _logger.LogInformation($"Updating Redis Cache for Min Data: {dataEvent.Ticker} for Date: {dataEvent.WindowsStartTime}");

            string provider = ResolveDataSource(dataEvent.DataSource);

            string key = $"DataIngestion:{provider}:{dataEvent.Ticker}";
            // get the cache if it's available, otherwise create a new one
            var dataIngestionCacheJson = await _redisHelper.GetKeyValueFromRedis(key);
            DataIngestionCache oldCache = new DataIngestionCache();

            oldCache = JsonConvert.DeserializeObject<DataIngestionCache>(dataIngestionCacheJson);

            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

            DataIngestionCache cacheData = new DataIngestionCache()
            {
                SourceToken = dataEvent.Ticker,
                DataSource = provider,
                DataType = DataFeedTypeEnum.OHLC.ToString(),
                WindowsStartTime = DateTimeHelper.ToIsoStringWithTime(dataEvent.WindowsStartTime),
                Timeframe = 1,
                UpdatedOn = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset),
                LastUpdateOn = oldCache != null ? oldCache.UpdatedOn : null
            };

            string value = JsonConvert.SerializeObject(cacheData);
            await _redisHelper.AddToRedis(key, value);

            // Separate from the snapshot cache above: tracks how many *distinct* 1-min candles have
            // landed for this ticker today, for the dashboard's Data page (e.g. "312 / 375 today").
            // SET membership de-dupes automatically if the same candle is re-delivered.
            // Provider is part of the key so two providers feeding the same ticker don't collide.
            // DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow) — not DateTime.Now — so this key's
            // date stays correct regardless of whether this container's TZ env var is set correctly.
            string countKey = $"Ingestion:Count:{provider}:{dataEvent.Ticker}:1min:{DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow):yyyy-MM-dd}";
            var count = await _redisHelper.AddToCountSetAsync(countKey, dataEvent.WindowsStartTime.ToString("yyyy-MM-ddTHH:mm:ss"), TimeSpan.FromDays(3));

            _logger.LogInformation($"Updated Redis Cache for Token: {cacheData.SourceToken} (provider: {provider}) for DateTime: {cacheData.WindowsStartTime}. Today's 1-min count: {count}");
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