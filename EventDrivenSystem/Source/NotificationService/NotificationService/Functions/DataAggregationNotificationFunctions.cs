using AggregatorFunctions.SharedLibrary.Events.Aggregation.TimeFrame;
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
using SharedLibrary.Helpers;
using SharedLibrary.Notifications;

namespace NotificationService.Functions
{
    public class DataAggregationNotificationFunctions
    {
        private readonly ILogger<DataAggregationNotificationFunctions> _logger;
        private readonly IProducer<string, string> _producer;
        private readonly string _signalRServerUrl;
        private readonly RedisHelper _redisHelper;

        public DataAggregationNotificationFunctions(ILogger<DataAggregationNotificationFunctions> logger, 
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

        [Function("Aggregation5MNotification")]
        public async Task Aggregation5MNotification(
                   [KafkaTrigger("%KAFKA_BROKER_URL%",
                       "live-aggregation-ohlc-5min-topic",
                       AuthenticationMode = BrokerAuthenticationMode.Plain,
                       ConsumerGroup = "live-aggregator-5m-notification-consumer")] string eventDataJson,
                   FunctionContext context)
        {
           _logger.LogInformation("Kafka triggered function: Data Aggregation Notification Starting");

           var eventDataValue = string.Empty;

           var JsonObj = JObject.Parse(eventDataJson);

           if (JsonObj != null)
           {
               if (JsonObj["Value"] != null)
                   eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
           }

           var aggregationEvent = JsonConvert.DeserializeObject<TimeFrameAggregationEvent>(eventDataValue);
           if (aggregationEvent != null)
           {
               var newnotification = CreateNotification (aggregationEvent);

               //exchangeEvent.CimplifyType = CimplifyTypeEnum.Notification;
               var notification = JsonConvert.SerializeObject(newnotification);

               await SendNotification(notification, "aggregation");

               await UpdateRedisCache(aggregationEvent);
               _logger.LogInformation($"C# Kafka trigger function processed a message: {eventDataValue}");
           } 
        }

        [Function("Aggregation10MNotification")]
        public async Task Aggregation10MNotification(
                   [KafkaTrigger("%KAFKA_BROKER_URL%",
                       "live-aggregation-ohlc-10min-topic",
                       AuthenticationMode = BrokerAuthenticationMode.Plain,
                       ConsumerGroup = "live-aggregator-10m-notification-consumer")] string eventDataJson,
                   FunctionContext context)
        {
           _logger.LogInformation("Kafka triggered function: Data Aggregation Notification Starting");

           var eventDataValue = string.Empty;

           var JsonObj = JObject.Parse(eventDataJson);

           if (JsonObj != null)
           {
               if (JsonObj["Value"] != null)
                   eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
           }

           var aggregationEvent = JsonConvert.DeserializeObject<TimeFrameAggregationEvent>(eventDataValue);
           if (aggregationEvent != null)
           {
               var newnotification = CreateNotification(aggregationEvent);

               //exchangeEvent.CimplifyType = CimplifyTypeEnum.Notification;
               var notification = JsonConvert.SerializeObject(newnotification);

               await SendNotification(notification, "aggregation");

               await UpdateRedisCache(aggregationEvent);
               _logger.LogInformation($"C# Kafka trigger function processed a message: {eventDataValue}");
           }
        }

        [Function("Aggregation15MNotification")]
        public async Task Aggregation15MNotification(
                   [KafkaTrigger("%KAFKA_BROKER_URL%",
                       "live-aggregation-ohlc-15min-topic",
                       AuthenticationMode = BrokerAuthenticationMode.Plain,
                       ConsumerGroup = "live-aggregator-15m-notification-consumer")] string eventDataJson,
                   FunctionContext context)
        {
           _logger.LogInformation("Kafka triggered function: Data Aggregation Notification Starting");

           var eventDataValue = string.Empty;

           var JsonObj = JObject.Parse(eventDataJson);

           if (JsonObj != null)
           {
               if (JsonObj["Value"] != null)
                   eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
           }

           var aggregationEvent = JsonConvert.DeserializeObject<TimeFrameAggregationEvent>(eventDataValue);
           if (aggregationEvent != null)
           {
               var newnotification = CreateNotification(aggregationEvent);

               //exchangeEvent.CimplifyType = CimplifyTypeEnum.Notification;
               var notification = JsonConvert.SerializeObject(newnotification);

               await SendNotification(notification, "aggregation");

               await UpdateRedisCache(aggregationEvent);
               _logger.LogInformation($"C# Kafka trigger function processed a message: {eventDataValue}");
           }
        }

        [Function("Aggregation30MNotification")]
        public async Task Aggregation30MNotification(
                   [KafkaTrigger("%KAFKA_BROKER_URL%",
                       "live-aggregation-ohlc-30min-topic",
                       AuthenticationMode = BrokerAuthenticationMode.Plain,
                       ConsumerGroup = "live-aggregator-30m-notification-consumer")] string eventDataJson,
                   FunctionContext context)
        {
           _logger.LogInformation("Kafka triggered function: Data Aggregation Notification Starting");

           var eventDataValue = string.Empty;

           var JsonObj = JObject.Parse(eventDataJson);

           if (JsonObj != null)
           {
               if (JsonObj["Value"] != null)
                   eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
           }

           var aggregationEvent = JsonConvert.DeserializeObject<TimeFrameAggregationEvent>(eventDataValue);
           if (aggregationEvent != null)
           {
               var newnotification = CreateNotification(aggregationEvent);

               //exchangeEvent.CimplifyType = CimplifyTypeEnum.Notification;
               var notification = JsonConvert.SerializeObject(newnotification);

               await SendNotification(notification, "aggregation");

               await UpdateRedisCache(aggregationEvent);
               _logger.LogInformation($"C# Kafka trigger function processed a message: {eventDataValue}");
           }
        }


        [Function("Aggregation60MNotification")]
        public async Task Aggregation60MNotification(
                    [KafkaTrigger("%KAFKA_BROKER_URL%",
                        "live-aggregation-ohlc-60min-topic",
                        AuthenticationMode = BrokerAuthenticationMode.Plain,
                        ConsumerGroup = "live-aggregator-60m-notification-consumer")] string eventDataJson,
                    FunctionContext context)
        {
            _logger.LogInformation("Kafka triggered function: Data Aggregation Notification Starting");

            var eventDataValue = string.Empty;

            var JsonObj = JObject.Parse(eventDataJson);

            if (JsonObj != null)
            {
                if (JsonObj["Value"] != null)
                    eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
            }

            var aggregationEvent = JsonConvert.DeserializeObject<TimeFrameAggregationEvent>(eventDataValue);
            if (aggregationEvent != null)
            {
                var newnotification = CreateNotification(aggregationEvent);

                //exchangeEvent.CimplifyType = CimplifyTypeEnum.Notification;
                var notification = JsonConvert.SerializeObject(newnotification);

                await SendNotification(notification, "aggregation");

                await UpdateRedisCache(aggregationEvent);
                _logger.LogInformation($"C# Kafka trigger function processed a message: {eventDataValue}");
            }
        }


        [Function("Aggregation75MNotification")]
        public async Task Aggregation75MNotification(
                    [KafkaTrigger("%KAFKA_BROKER_URL%",
                        "live-aggregation-ohlc-75min-topic",
                        AuthenticationMode = BrokerAuthenticationMode.Plain,
                        ConsumerGroup = "live-aggregator-75m-notification-consumer")] string eventDataJson,
                    FunctionContext context)
        {
            _logger.LogInformation("Kafka triggered function: Data Aggregation Notification Starting");

            var eventDataValue = string.Empty;

            var JsonObj = JObject.Parse(eventDataJson);

            if (JsonObj != null)
            {
                if (JsonObj["Value"] != null)
                    eventDataValue = JsonObj?["Value"]?.ToString() ?? string.Empty;
            }

            var aggregationEvent = JsonConvert.DeserializeObject<TimeFrameAggregationEvent>(eventDataValue);
            if (aggregationEvent != null)
            {
                var newnotification = CreateNotification(aggregationEvent);

                //exchangeEvent.CimplifyType = CimplifyTypeEnum.Notification;
                var notification = JsonConvert.SerializeObject(newnotification);

                await SendNotification(notification, "aggregation");

                await UpdateRedisCache(aggregationEvent);
                _logger.LogInformation($"C# Kafka trigger function processed a message: {eventDataValue}");
            }
        }


        private DataAggregationNotification CreateNotification(TimeFrameAggregationEvent aggregationEvent)
        {
            _logger.LogInformation($"Creating notification for Timeframe: {aggregationEvent.Timeframe}, {aggregationEvent.Ticker} for Date: {aggregationEvent.WindowsStartTime} received at: {DateTime.Now.ToLocalTime()}");

            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

            var notificaiton = new DataAggregationNotification()
            {
                Ticker = aggregationEvent.Ticker,
                CimplifyType = CimplifyTypeEnum.Notification,
                DataType = DataFeedTypeEnum.OHLC.ToString(),
                Timeframe = aggregationEvent.Timeframe,      // currently hardcoding                
                Producer = "notification.service",
                WindowsStartTime = DateTimeHelper.ToIsoStringWithTime(aggregationEvent.WindowsStartTime)
            };

            notificaiton.ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset);
            return notificaiton;
        }

        public static DateTime ConvertToLocalTime(DateTime utcTime)
        {
            string dateString = utcTime.ToString("yyyy-MM-ddTHH:mm:ss");
            var utcNewDate = DateTime.Parse(dateString);
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); // For example, "GMT Standard Time" represents GMT+0
            return TimeZoneInfo.ConvertTimeFromUtc(utcNewDate, timeZone);
        }


        private async Task UpdateRedisCache(TimeFrameAggregationEvent dataEvent)
        {
            _logger.LogInformation($"Updating Redis Cache for Min Data: Timeframe {dataEvent.Timeframe}, {dataEvent.Ticker} for Date: {dataEvent.WindowsStartTime}");

            string key = $"Aggregation:OHLC:{dataEvent.Ticker}:{dataEvent.Timeframe}:Min";
            // get the cache if it's available, otherwise create a new one
            var dataAggregationCacheJson = await _redisHelper.GetKeyValueFromRedis(key);
            DataAggregationCache oldCache = new DataAggregationCache();
               
            oldCache = JsonConvert.DeserializeObject<DataAggregationCache>(dataAggregationCacheJson);

            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);


            DataAggregationCache cacheData = new DataAggregationCache()
            {
                Ticker = dataEvent.Ticker,                
                DataType = DataFeedTypeEnum.OHLC.ToString(),
                WindowStartTime = DateTimeHelper.ToIsoStringWithTime(dataEvent.WindowsStartTime),
                Timeframe = dataEvent.Timeframe,
                UpdatedOn = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset),
                LastUpdateOn = oldCache != null ? oldCache.UpdatedOn : null                   
            };

            string value = JsonConvert.SerializeObject(cacheData);
            await _redisHelper.AddToRedis(key, value);

            // Separate from the snapshot cache above: tracks how many *distinct* candles have landed
            // for this ticker/timeframe today, for the dashboard's Data page (e.g. "62 / 75 today").
            // Shared by all six timeframe functions (5/10/15/30/60/75-min) since they all funnel
            // through this one method — no per-timeframe duplication needed.
            string countKey = $"Aggregation:Count:{dataEvent.Ticker}:{dataEvent.Timeframe}min:{DateTime.Now:yyyy-MM-dd}";
            var count = await _redisHelper.AddToCountSetAsync(countKey, dataEvent.WindowsStartTime.ToString("yyyy-MM-ddTHH:mm:ss"), TimeSpan.FromDays(3));

            _logger.LogInformation($"Updated Redis Cache for Token: {key} for DateTime: {value}. Today's {dataEvent.Timeframe}-min count: {count}");
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