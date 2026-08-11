using Azure.Storage.Blobs;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OHLCFunctionApp.Persistence;
using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;

namespace OHLCFunctionApp
{
    // Keeps Azurite in sync with the live 1-min feed, instead of relying on a manual after-hours
    // upload — same blob structure GetOHLCByYearAndMonth/GetOHLCDataByDate/HistoricalSufficiency
    // already read: header "Date,Open,Low,High,Close,Volume", date as dd-MM-yyyy HH:mm:ss, one
    // file per month/contract at {basePath}/{year}/{month}/{blobName}.csv (see BlobPathHelper).
    //
    // The live feed's ticker (e.g. "NIFTY") carries no exchange/instrument-type marker — confirmed
    // this represents both the NSE spot index and the NFO front-month future simultaneously, so
    // every candle is persisted under both blob paths, not just one.
    public class LiveCandlePersistenceFunction
    {
        private readonly ILogger<LiveCandlePersistenceFunction> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IBlobAppendStrategy _appendStrategy;

        private const string HeaderLine = "Date,Open,Low,High,Close,Volume";

        public LiveCandlePersistenceFunction(BlobServiceClient blobServiceClient, IBlobAppendStrategy appendStrategy, ILogger<LiveCandlePersistenceFunction> logger)
        {
            _blobServiceClient = blobServiceClient;
            _appendStrategy = appendStrategy;
            _logger = logger;
        }

        [Function("LiveCandlePersistenceFunction")]
        public async Task Run(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-dataingestion-ohlc-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                // Independent consumer group — same topic the 5-min aggregator and
                // NotificationService already read, Kafka fans out to each group separately.
                ConsumerGroup = "live-ohlc-azurite-persistence-consumer")] string eventDataJson,
            FunctionContext context)
        {
            var eventDataValue = string.Empty;

            using (JsonDocument document = JsonDocument.Parse(eventDataJson))
            {
                if (document.RootElement.TryGetProperty("Value", out JsonElement valueElement))
                {
                    eventDataValue = valueElement.GetString() ?? string.Empty;
                }
            }

            if (string.IsNullOrEmpty(eventDataValue))
            {
                _logger.LogWarning("LiveCandlePersistenceFunction: received an empty/unparseable Kafka message, skipping.");
                return;
            }

            LiveCandleEvent? candle;
            try
            {
                candle = JsonSerializer.Deserialize<LiveCandleEvent>(eventDataValue);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, $"LiveCandlePersistenceFunction: failed to deserialize candle event: {eventDataValue}");
                return;
            }

            if (candle == null || string.IsNullOrEmpty(candle.Ticker))
            {
                _logger.LogWarning("LiveCandlePersistenceFunction: deserialized candle had no ticker, skipping.");
                return;
            }

            string dataLine = BuildCsvRow(candle);
            var container = _blobServiceClient.GetBlobContainerClient("exchange-ohlc-container");

            foreach (var exchange in new[] { "nse", "nfo" })
            {
                string blobPath = BlobPathHelper.GetBlobPath(candle.WindowsStartTime, exchange, candle.Ticker);
                try
                {
                    await _appendStrategy.AppendAsync(container, blobPath, HeaderLine, dataLine, _logger);
                    _logger.LogInformation($"Persisted {candle.Ticker} {candle.WindowsStartTime:yyyy-MM-dd HH:mm} to {blobPath}");
                }
                catch (Exception ex)
                {
                    // One exchange's write failing shouldn't block the other, and shouldn't throw
                    // the whole Kafka trigger into a redelivery loop over a single bad blob.
                    _logger.LogError(ex, $"LiveCandlePersistenceFunction: failed to persist {candle.Ticker} to {blobPath}");
                }
            }
        }

        private static string BuildCsvRow(LiveCandleEvent candle)
        {
            string date = candle.WindowsStartTime.ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            string open = candle.Open.ToString("F2", CultureInfo.InvariantCulture);
            string low = candle.Low.ToString("F2", CultureInfo.InvariantCulture);
            string high = candle.High.ToString("F2", CultureInfo.InvariantCulture);
            string close = candle.Close.ToString("F2", CultureInfo.InvariantCulture);
            return $"{date},{open},{low},{high},{close},{candle.Volume}";
        }
    }
}
