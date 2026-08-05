using System.Net;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Events;

namespace DataIngestionFunctions
{
    // Receives the raw TradingView webhook and republishes it as-is onto Kafka for
    // DataIngestionTradingViewFunction (in DataIngestionFunctions.cs) to pick up, enrich with
    // ticker/DataSource/DataType, and forward onto live-dataingestion-ohlc-topic.
    public class TradingViewFunctions
    {
        private readonly ILogger<TradingViewFunctions> _logger;
        private readonly IProducer<string, string> _producer;
        private readonly bool _dataIngestionEnabled;
        private static readonly string _producerTopicName = "live-tradingview-ohlc-topic";

        public TradingViewFunctions(ILoggerFactory loggerFactory, IProducer<string, string> producer, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<TradingViewFunctions>();
            _producer = producer;
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

                var dataFeedEvent = JsonSerializer.Deserialize<TradingViewDataEvent>(requestBody);
                if (dataFeedEvent == null || !IsValidDataFeed(dataFeedEvent))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new { error = "Invalid or incomplete data feed payload" });
                    return response;
                }

                var key = dataFeedEvent.SourceToken;
                var message = JsonSerializer.Serialize(dataFeedEvent);
                await ProduceToKafka(_producerTopicName, key, message, _logger, cancellationToken);

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

        private static bool IsValidDataFeed(TradingViewDataEvent dataFeed)
        {
            return !string.IsNullOrEmpty(dataFeed.SourceToken) &&
                   dataFeed.Timeframe > 0 &&
                   dataFeed.EventTime != DateTime.MinValue &&
                   dataFeed.WindowsStartTime != DateTime.MinValue;
        }

        private async Task ProduceToKafka(string topicName, string key, string value, ILogger logger, CancellationToken cancellationToken)
        {
            try
            {
                var deliveryResult = await _producer.ProduceAsync(
                    topicName,
                    new Message<string, string> { Key = key, Value = value },
                    cancellationToken);

                logger.LogInformation("Delivered message to: {TopicPartitionOffset}", deliveryResult.TopicPartitionOffset);
            }
            catch (ProduceException<string, string> e)
            {
                logger.LogError(e, "Delivery failed: {Reason}", e.Error.Reason);
                throw;
            }
        }
    }
}
