using Azure.Storage.Blobs;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OHLCFunctionApp.Persistence;
using SharedLibrary.Enums.Exchange;
using SharedLibrary.Events.Exchange;
using SharedLibrary.Helpers;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace OHLCFunctionApp
{
    // Reacts to NSE's Init (09:00 IST) to pre-create today's daily files with just their header
    // row, so LiveCandlePersistenceFunction's first candle of the day is a plain append instead of
    // racing to create the blob. Covers the same 4 (exchange, ticker) combos
    // LiveCandlePersistenceFunction's own "foreach exchange" loop writes for: NIFTY and BANKNIFTY,
    // each on nse and nfo. Safe to run more than once a day — EnsureHeaderAsync is a no-op if the
    // file already exists.
    public class DailyFileWarmUpFunction
    {
        private readonly ILogger<DailyFileWarmUpFunction> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IBlobAppendStrategy _appendStrategy;

        private static readonly string[] Exchanges = { "nse", "nfo" };
        private static readonly string[] Tickers = { "NIFTY", "BANKNIFTY" };

        public DailyFileWarmUpFunction(BlobServiceClient blobServiceClient, IBlobAppendStrategy appendStrategy, ILogger<DailyFileWarmUpFunction> logger)
        {
            _blobServiceClient = blobServiceClient;
            _appendStrategy = appendStrategy;
            _logger = logger;
        }

        [Function("DailyFileWarmUpFunction")]
        public async Task Run(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-exchange-workflow-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                // Independent consumer group — same topic WarmUpService, NotificationService, and
                // DailyToMonthlyMergeFunction also read; Kafka fans out to each group separately.
                ConsumerGroup = "live-ohlc-daily-file-warmup-consumer")] string eventDataJson,
            FunctionContext context)
        {
            var eventDataValue = string.Empty;
            var jsonObj = JObject.Parse(eventDataJson);
            if (jsonObj?["Value"] != null)
                eventDataValue = jsonObj["Value"]?.ToString() ?? string.Empty;

            var exchangeEvent = JsonConvert.DeserializeObject<ExchangeEvent>(eventDataValue);
            if (exchangeEvent == null)
            {
                _logger.LogWarning($"DailyFileWarmUpFunction: could not deserialize exchange event: {eventDataValue}");
                return;
            }

            if (!string.Equals(exchangeEvent.ExchangeName, "NSE", StringComparison.OrdinalIgnoreCase) ||
                exchangeEvent.ExchangeTimerAction != ExchangeActionEnum.Init)
            {
                _logger.LogInformation($"DailyFileWarmUpFunction: ignoring {exchangeEvent.ExchangeName} {exchangeEvent.ExchangeTimerAction}");
                return;
            }

            DateTime date = ResolveDate(exchangeEvent.Date);
            var container = _blobServiceClient.GetBlobContainerClient("exchange-ohlc-container");

            foreach (var exchange in Exchanges)
            {
                foreach (var ticker in Tickers)
                {
                    string blobPath = BlobPathHelper.GetDailyBlobPath(date, exchange, ticker);
                    try
                    {
                        await _appendStrategy.EnsureHeaderAsync(container, blobPath, BlobPathHelper.HeaderLine, _logger);
                        _logger.LogInformation($"DailyFileWarmUpFunction: ensured {blobPath} exists for {date:yyyy-MM-dd}");
                    }
                    catch (Exception ex)
                    {
                        // One combo failing shouldn't block the other 3, same posture as
                        // LiveCandlePersistenceFunction's per-exchange try/catch.
                        _logger.LogError(ex, $"DailyFileWarmUpFunction: failed to ensure {blobPath}");
                    }
                }
            }
        }

        private static DateTime ResolveDate(string rawDate)
        {
            if (DateTime.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
            return DateTimeHelper.GetCurrentIndianTime().Date;
        }
    }
}
