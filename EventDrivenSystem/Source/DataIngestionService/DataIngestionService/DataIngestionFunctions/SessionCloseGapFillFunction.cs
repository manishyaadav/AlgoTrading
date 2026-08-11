using System.Linq;
using System.Text.Json;
using Confluent.Kafka;
using DataIngestionFunctions.RedisConfig;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Enums.DataFeed;
using SharedLibrary.Events.DataIngestion;
using SharedLibrary.Helpers;

namespace DataIngestionFunctions
{
    // SEBI's Closing Auction Session (CAS) for index derivatives means TradingView/Kite genuinely
    // stop streaming 1-min ticks for the index in the last ~15 minutes before close — no trading
    // happens until the single auction-uncrossing print lands, typically right at 15:29. That's
    // expected silence, not an outage, so unlike a real gap the honest fix is to forward-fill the
    // last known price for whichever minutes in that window never got a real candle, instead of
    // leaving them "missing" forever. Confirmed against real NIFTY/BANKNIFTY data: candles run
    // normally through 15:14, nothing arrives 15:15-15:28, then one real print lands at 15:29.
    //
    // This only ever acts inside the configured window near close (GapFill:WindowMinutesBeforeClose,
    // default 15 — see docker-compose-live.yml). A gap anywhere else in the session is left alone
    // and stays a genuine "missing" bucket on the dashboard — outside this window, a gap really
    // does mean the pipeline broke, and silently papering over that would hide a real problem.
    //
    // Publishing the synthetic candle onto live-dataingestion-ohlc-topic — the exact same topic and
    // DataIngestionMinDataEvent shape DataIngestionTradingViewFunction publishes for a real one — is
    // what makes this transparent to everything downstream. NotificationService, all 6
    // AggregationService timeframes, and ohlc-live's LiveCandlePersistenceFunction each already
    // consume this topic independently and have no way to tell a filled candle from a real one, so
    // every aggregation timeframe and the Azurite blob self-correct with zero code changes on their
    // end — the whole point of injecting this as far upstream as possible instead of patching each
    // consumer separately.
    public class SessionCloseGapFillFunction
    {
        private readonly ILogger<SessionCloseGapFillFunction> _logger;
        private readonly IProducer<string, string> _producer;
        private readonly RedisHelper _redisHelper;
        private readonly bool _enabled;
        private readonly int _windowMinutesBeforeClose;
        private readonly int _graceMinutes;
        private readonly TimeSpan _sessionClose;
        private static readonly string _producerTopicName = "live-dataingestion-ohlc-topic";

        public SessionCloseGapFillFunction(ILoggerFactory loggerFactory, IProducer<string, string> producer, RedisHelper redisHelper, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<SessionCloseGapFillFunction>();
            _producer = producer;
            _redisHelper = redisHelper;

            // Same Features:* toggle idiom TradingViewFunctions already uses for
            // Features:DataIngestion — one flip in docker-compose-live.yml turns this whole feature
            // off if it ever needs to be pulled without a redeploy of code.
            _enabled = configuration.GetValue<bool>("Features:GapFill", true);
            _windowMinutesBeforeClose = configuration.GetValue<int>("GapFill:WindowMinutesBeforeClose", 15);
            _graceMinutes = configuration.GetValue<int>("GapFill:GraceMinutes", 1);

            // Defaults to 15:30 IST, matching close everywhere else in this codebase
            // (ExchangeTimerFunctions, DashboardService's ExpectedSoFar) — configurable rather than
            // hardcoded because NSE/BSE actually do run a different, shorter session a few times a
            // year (Muhurat trading), so this needs to be adjustable without a code change.
            _sessionClose = TimeSpan.TryParse(configuration["GapFill:SessionCloseTime"], out var configuredClose)
                ? configuredClose
                : new TimeSpan(15, 30, 0);
        }

        [Function("SessionCloseGapFillFunction")]
        public async Task Run([TimerTrigger("0 * * * * *")] TimerInfo timer)
        {
            if (!_enabled) return;

            var nowIst = DateTimeHelper.GetCurrentIndianTime().DateTime;

            // Check the minute that closed (1 + GraceMinutes) minutes ago, not just the last one —
            // extra grace so a real candle landing a little late (normal webhook jitter) always
            // wins over a synthetic fill instead of racing it.
            var candidateMinute = new DateTime(nowIst.Year, nowIst.Month, nowIst.Day, nowIst.Hour, nowIst.Minute, 0)
                .AddMinutes(-(1 + _graceMinutes));

            var close = nowIst.Date + _sessionClose;
            var windowStart = close - TimeSpan.FromMinutes(_windowMinutesBeforeClose);

            // Outside the CAS window entirely — nothing to do. This is the guard that keeps a
            // genuine mid-session outage red instead of silently copy-filled.
            if (candidateMinute < windowStart || candidateMinute >= close) return;

            string today = candidateMinute.ToString("yyyy-MM-dd");
            string candidateIso = candidateMinute.ToString("yyyy-MM-ddTHH:mm:ss");

            foreach (var lastCandleKey in await _redisHelper.GetKeysAsync("Ingestion:LastCandle:*"))
            {
                // Key shape: Ingestion:LastCandle:{provider}:{ticker}
                var segments = lastCandleKey.Split(':');
                if (segments.Length < 4) continue;
                string provider = segments[2];
                string ticker = string.Join(':', segments.Skip(3)); // tickers don't contain ':' today, but don't assume it

                string countKey = $"Ingestion:Count:{provider}:{ticker}:1min:{today}";

                // Only act for tickers that actually traded today — without this, a LastCandle key
                // surviving its TTL into a holiday/weekend would manufacture a whole fake session.
                if (await _redisHelper.GetSetLengthAsync(countKey) == 0) continue;

                if (await _redisHelper.SetContainsAsync(countKey, candidateIso)) continue; // real candle already landed for this minute

                string lastCandleJson = await _redisHelper.GetKeyValueFromRedis(lastCandleKey);
                if (string.IsNullOrEmpty(lastCandleJson)) continue;

                DataIngestionMinDataEvent? lastCandle;
                try { lastCandle = JsonSerializer.Deserialize<DataIngestionMinDataEvent>(lastCandleJson); }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "GapFill: could not parse {Key}, skipping this tick for {Ticker}.", lastCandleKey, ticker);
                    continue;
                }
                if (lastCandle == null) continue;

                var indianNow = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);
                var synthetic = new DataIngestionMinDataEvent
                {
                    SourceToken = lastCandle.SourceToken,
                    Ticker = ticker,
                    WindowsStartTime = candidateMinute,
                    Timeframe = 1,
                    Open = lastCandle.Close,
                    High = lastCandle.Close,
                    Low = lastCandle.Close,
                    Close = lastCandle.Close,
                    Volume = 0, // CAS genuinely halts trading in this window — zero real volume, not an approximation
                    Producer = "dataingestion.gapfill", // tags this as synthesized; every other field matches a real candle's shape exactly
                    ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianNow),
                    DataSource = lastCandle.DataSource,
                    DataType = DataFeedTypeEnum.OHLC
                };

                var kafkaMessage = JsonSerializer.Serialize(synthetic);
                await ProduceToKafka($"{ticker}:1Min", kafkaMessage);

                // Carry the (unchanged) price forward so the next missing minute in the same window
                // still has something to copy from — a flat copy-of-a-copy is fine since the price
                // never moves until a real candle lands again.
                await _redisHelper.AddToRedisWithExpiry(lastCandleKey, kafkaMessage, TimeSpan.FromDays(3));

                _logger.LogInformation(
                    "CAS window gap-fill: {Ticker} had no real candle for {Minute} — published a copy of the last close ({Close}).",
                    ticker, candidateIso, synthetic.Close);
            }
        }

        private async Task ProduceToKafka(string key, string value)
        {
            try
            {
                var deliveryReport = await _producer.ProduceAsync(_producerTopicName, new Message<string, string> { Key = key, Value = value });
                _logger.LogInformation("Delivered gap-fill message to: {TopicPartitionOffset}", deliveryReport.TopicPartitionOffset);
            }
            catch (ProduceException<string, string> e)
            {
                _logger.LogError(e, "Gap-fill delivery failed: {Reason}", e.Error.Reason);
            }
        }
    }
}
