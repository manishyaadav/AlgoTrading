using System.Net;
using System.Text.Json;
using DataIngestionFunctions.Services;
using DataIngestionFunctions.SharedLibrary.Events;
using DataIngestionFunctions.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace DataIngestionFunctions
{
    public class TradingViewFunctions
    {
        private readonly ILogger<TradingViewFunctions> _logger;
        private readonly ITradingViewService _tradingViewService;
        private readonly IKafkaProducerService _kafkaService;
        private readonly KafkaSettings _kafkaSettings;
        private readonly RateLimitSettings _rateLimitSettings;
        private readonly SemaphoreSlim _throttler;
        private readonly bool _dataIngestionEnabled;

        public TradingViewFunctions(
            ILoggerFactory loggerFactory,
            ITradingViewService tradingViewService,
            IKafkaProducerService kafkaService,
            IOptions<KafkaSettings> kafkaSettings,
            IOptions<RateLimitSettings> rateLimitSettings,
            IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<TradingViewFunctions>();
            _tradingViewService = tradingViewService;
            _kafkaService = kafkaService;
            _kafkaSettings = kafkaSettings.Value;
            _rateLimitSettings = rateLimitSettings.Value;
            _throttler = new SemaphoreSlim(_rateLimitSettings.MaxConcurrentRequests);
            _dataIngestionEnabled = configuration.GetValue<bool>("Features:DataIngestion", true);
        }

        [Function(nameof(TradingViewMinDataFeedFunction))]
        public async Task<HttpResponseData> TradingViewMinDataFeedFunction(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dataingestion/tradingview/funcTradingViewDataFeed")] HttpRequestData req,
            CancellationToken cancellationToken)
        {
            var response = req.CreateResponse();

            if (!_dataIngestionEnabled)
            {
                response.StatusCode = HttpStatusCode.ServiceUnavailable;
                await response.WriteAsJsonAsync(new { error = "Data ingestion is currently disabled" });
                return response;
            }

            if (!await _throttler.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
            {
                response.StatusCode = HttpStatusCode.TooManyRequests;
                await response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
                return response;
            }

            try
            {
                string requestBody;
                using (var reader = new StreamReader(req.Body))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrEmpty(requestBody))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new { error = "Request body is empty" });
                    return response;
                }

                var dataFeedEvent = await _tradingViewService.ProcessDataFeed(requestBody);
                var key = dataFeedEvent.SourceToken;
                var message = JsonSerializer.Serialize(dataFeedEvent);

                await _kafkaService.ProduceMessage(
                    _kafkaSettings.Topics.TradingViewData,
                    key,
                    message,
                    cancellationToken);

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new
                {
                    status = "success",
                    message = "Data processed successfully",
                    data = new
                    {
                        sourceToken = dataFeedEvent.SourceToken,
                        timeframe = dataFeedEvent.Timeframe,
                        eventTime = dataFeedEvent.EventTime
                    }
                });

                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid request data: {Message}", ex.Message);
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteAsJsonAsync(new { error = ex.Message });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing trading view data");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { error = "Internal server error" });
                return response;
            }
            finally
            {
                _throttler.Release();
            }
        }        private bool IsValidDataFeed(TradingViewDataEvent dataFeed)
        {
            return !string.IsNullOrEmpty(dataFeed.SourceToken) &&
                   !string.IsNullOrEmpty(dataFeed.Timeframe) &&
                   dataFeed.EventTime != DateTime.MinValue &&
                   dataFeed.WindowsStartTime != DateTime.MinValue;
        }


        public TradingViewFunctions(ILoggerFactory loggerFactory, IProducer<string, string> producer)
        {
            _logger = loggerFactory.CreateLogger<TradingViewFunctions>();
            _producer = producer;
        }

        [Function(nameof(TradingViewMinDataFeedFunction))]
        public async Task<HttpResponseData> TradingViewMinDataFeedFunction(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dataingestion/tradingview/funcTradingViewDataFeed")] HttpRequestData req,
            CancellationToken cancellationToken)
        {
            var response = req.CreateResponse();

            if (!await _throttler.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
            {
                response.StatusCode = HttpStatusCode.TooManyRequests;
                await response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
                return response;
            }

            try
            {
                string requestBody;
                using (var reader = new StreamReader(req.Body))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrEmpty(requestBody))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new { error = "Request body is empty" });
                    return response;
                }

                var dataFeedEvent = await _tradingViewService.ProcessDataFeed(requestBody);
                var key = dataFeedEvent.SourceToken;
                var message = JsonSerializer.Serialize(dataFeedEvent);

                await _kafkaService.ProduceMessage(
                    _kafkaSettings.Topics.TradingViewData,
                    key,
                    message,
                    cancellationToken);

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new { 
                    status = "success", 
                    message = "Data processed successfully",
                    data = new {
                        sourceToken = dataFeedEvent.SourceToken,
                        timeframe = dataFeedEvent.Timeframe,
                        eventTime = dataFeedEvent.EventTime
                    }
                });
                
                return response;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid request data: {Message}", ex.Message);
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteAsJsonAsync(new { error = ex.Message });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing trading view data");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { error = "Internal server error" });
                return response;
            }
            finally
            {
                _throttler.Release();
            }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize trading view data");
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteAsJsonAsync(new { error = "Invalid JSON format" });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing trading view data");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { error = "Internal server error" });
                return response;
            }
            }
        }

        //[Function(nameof(TradingViewAlertFeedFunction))]
        //public async Task TradingViewAlertFeedFunction(
        //    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dataingestion/tradingview/funcTradingViewAlertFeed")] HttpRequestData req)
        //{

        //    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

        //    var options = new JsonSerializerOptions
        //    {
        //        NumberHandling = JsonNumberHandling.AllowReadingFromString // Allow numeric strings
        //    };

        //    TradingViewAlertEvent? alertFromTradingView = JsonSerializer.Deserialize<TradingViewAlertEvent>(requestBody, options);

        //    if (alertFromTradingView != null)
        //    {
        //        TradingViewAlertEvent alertEvent = CreateAlertEvent(alertFromTradingView);
        //        var key = alertEvent.SourceToken;
        //        var message = JsonSerializer.Serialize(alertEvent);

        //        _logger.LogInformation($"{DateTime.Now} - Processing TradingView Alert: {alertFromTradingView?.SourceToken}, PointValue: {alertFromTradingView?.PointVal}, Direction: {alertFromTradingView?.Direction}, EventTime: {alertFromTradingView?.EventTime}, Time: {alertFromTradingView?.WindowsStartTime}");



        //        await ProduceToKafka(_producerAlertTopicName, key, message, _logger);

        //        _logger.LogInformation($"{DateTime.Now} - Alert pushed to Kafka: {alertFromTradingView?.SourceToken}, PointValue: {alertFromTradingView?.PointVal}, Direction: {alertFromTradingView?.Direction}, EventTime: {alertFromTradingView?.EventTime}, Time: {alertFromTradingView?.WindowsStartTime}");
        //    }
        //}

        private TradingViewAlertEvent CreateAlertEvent(TradingViewAlertEvent alertFromTradingView)
        {
            return new TradingViewAlertEvent()
            {
                SourceToken = alertFromTradingView.SourceToken,
                
                Level = alertFromTradingView.Level,
                PointVal = alertFromTradingView.PointVal,
                Timeframe = alertFromTradingView.Timeframe,
                Direction = alertFromTradingView.Direction,
                Type = alertFromTradingView.Type,

                EventTime = ConvertToLocalTime(alertFromTradingView.EventTime),
                WindowsStartTime = ConvertToLocalTime(alertFromTradingView.WindowsStartTime)
            };
        }

        public TradingViewDataEvent CreateDataFeedEvent(TradingViewDataEvent dataFeed)
        {
            return new TradingViewDataEvent()
            {
                   SourceToken = dataFeed.SourceToken,
                   Timeframe = dataFeed.Timeframe,
                   Open = dataFeed.Open,
                   High = dataFeed.High,
                   Low = dataFeed.Low,
                   Close = dataFeed.Close,
                   Volume = dataFeed.Volume,
                   EventTime = ConvertToLocalTime(dataFeed.EventTime),
                   WindowsStartTime = ConvertToLocalTime(dataFeed.WindowsStartTime)
            };
        }

        public static DateTime ConvertToLocalTime(DateTime utcTime)
        {
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); // For example, "GMT Standard Time" represents GMT+0
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone);
        }

        private async Task ProduceToKafka(
            string topicName, 
            string key, 
            string value, 
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                var deliveryResult = await _producer.ProduceAsync(
                    topicName,
                    new Message<string, string>
                    {
                        Key = key,
                        Value = value,
                        Headers = new Headers
                        {
                            { "timestamp", BitConverter.GetBytes(DateTime.UtcNow.Ticks) }
                        }
                    },
                    cancellationToken
                );

                logger.LogInformation(
                    "Message delivered to Kafka: {@KafkaDelivery}",
                    new
                    {
                        Topic = deliveryResult.Topic,
                        Partition = deliveryResult.Partition.Value,
                        Offset = deliveryResult.Offset.Value,
                        Key = key,
                        Timestamp = deliveryResult.Timestamp.UtcDateTime
                    });
            }
            catch (ProduceException<string, string> e)
            {
                logger.LogError(
                    e,
                    "Failed to deliver message to Kafka: {@KafkaError}",
                    new
                    {
                        Topic = topicName,
                        Key = key,
                        ErrorCode = e.Error.Code,
                        Reason = e.Error.Reason
                    });
                throw; // Rethrow to handle in the calling method
            }
        }
    }
}
