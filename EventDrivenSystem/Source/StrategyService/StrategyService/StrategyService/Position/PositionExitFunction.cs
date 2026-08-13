using System.Globalization;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using StrategyService.Common;
using StrategyService.RedisConfig;
using StrategyService.Strategy;

namespace StrategyService.Position
{
    // Reacts to every 1-min candle (live-dataingestion-ohlc-topic — the same topic
    // AggregationService's LatestMinuteCandleFunction also independently consumes to build
    // Candle:Last1Min) and evaluates every deployed strategy's real ExitRules (ExitRuleEvaluator.cs)
    // against whichever open virtual position that strategy currently has. Runs on the 1-min tick,
    // not the strategy's own (typically 5-min) Supertrend cadence, because ExitRule #2 ("Candle Low/
    // High (1 Minute) vs Supertrend") is specifically about catching an intrabar breach before the
    // 5-min bar it belongs to even closes.
    public class PositionExitFunction
    {
        private readonly ILogger<PositionExitFunction> _logger;
        private readonly RedisHelper _redisHelper;

        public PositionExitFunction(ILogger<PositionExitFunction> logger, RedisHelper redisHelper)
        {
            _logger = logger;
            _redisHelper = redisHelper;
        }

        [Function("PositionExitFunction")]
        public async Task Run(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-dataingestion-ohlc-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "strategy-live-position-exit")] string eventDataJson, FunctionContext context)
        {
            var candle = Unwrap(eventDataJson);
            if (candle == null || string.IsNullOrEmpty(candle.Ticker)) return;

            if (!InstrumentMapper.TryResolveByTicker(candle.Ticker, out var instrument) || instrument == null)
                return; // a ticker this feature doesn't track an instrument mapping for

            string? sessionState = await ReadTradingSessionStateAsync();

            foreach (var (id, strategy) in StrategyMaker.GetAllDeployed())
            {
                var ts = strategy.Strategies?.FirstOrDefault();
                if (ts == null) continue;

                try
                {
                    await EvaluateOneAsync(id, strategy.StrategyName ?? id, ts, instrument, candle, sessionState);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PositionExitFunction: failed to evaluate exit for strategy {StrategyId}", id);
                }
            }
        }

        private async Task EvaluateOneAsync(string strategyId, string strategyName, TradingStrategy ts, string instrument, DataIngestionMinDataEventDto candle, string? sessionState)
        {
            string positionKey = PositionState.KeyFor(strategyId);
            var hash = await _redisHelper.GetHashAsync(positionKey);
            var position = PositionState.FromHash(hash);

            if (position == null || position.Status != "Open") return;
            if (!string.Equals(position.Instrument, instrument, StringComparison.OrdinalIgnoreCase)) return;

            var activeSide = position.Side == "Long" ? ts.LongEntry : ts.ShortEntry;
            if (activeSide?.ExitRules == null || activeSide.ExitRules.Count == 0) return;

            decimal? supertrendValue = await ReadSupertrendValueAsync(position);

            var ctx = new ExitRuleEvaluator.LiveExitContext(
                Side: position.Side!,
                EntryPrice: position.EntryPrice ?? 0,
                InitialStopLoss: position.InitialStopLoss ?? 0,
                EntryTime: position.EntryTime ?? IstClock.Now(),
                AsOf: IstClock.Now(),
                Candle1MinClose: candle.Close, Candle1MinLow: candle.Low, Candle1MinHigh: candle.High,
                SupertrendValue: supertrendValue,
                TradingSessionState: sessionState);

            var decision = ExitRuleEvaluator.Evaluate(activeSide.ExitRules, ctx);

            var updated = position with
            {
                LastEvaluatedAt = IstClock.Now(),
            };

            if (decision.ShouldExit)
            {
                updated = updated with
                {
                    Status = "Flat",
                    ExitPrice = candle.Close,
                    ExitTime = IstClock.Now(),
                    ExitReason = decision.FiredRuleDescription ?? "Exit rule fired",
                };
            }

            await _redisHelper.SetHashAsync(positionKey, updated.ToHash(), TimeSpan.FromDays(7));

            if (decision.ShouldExit)
            {
                var alert = new PositionAlertRecord(
                    Kind: "PositionEvent", AlertType: "PositionExited",
                    StrategyId: strategyId, StrategyName: strategyName, Instrument: position.Instrument, Ticker: position.Ticker,
                    Side: position.Side!, EntryPrice: position.EntryPrice, ExitPrice: candle.Close, Reason: updated.ExitReason,
                    WindowsStartTime: candle.WindowsStartTime, ProducedAt: IstClock.Now().ToString("yyyy-MM-ddTHH:mm:ss"));
                await PushAlertAsync(alert);

                _logger.LogInformation("PositionExitFunction: {StrategyId} exited {Side} at {ExitPrice} — {Reason}", strategyId, position.Side, candle.Close, updated.ExitReason);
            }
        }

        private async Task<decimal?> ReadSupertrendValueAsync(PositionState position)
        {
            string key = $"Indicator:Running:{position.Instrument}:{position.Timeframe}:Supertrend:{position.Period}:{position.Multiplier}";
            var hash = await _redisHelper.GetHashAsync(key);
            if (hash.Count == 0) return null;

            string direction = hash.GetValueOrDefault("TrendDirection", "Up");
            string field = direction == "Down" ? "PrevUpperBand" : "PrevLowerBand";
            return hash.TryGetValue(field, out var raw) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : (decimal?)null;
        }

        private async Task<string?> ReadTradingSessionStateAsync()
        {
            // Already written by NotificationService/Functions/ExchangeNotificationFunctions.cs on
            // every NSE Open/PreClose/Close event — no new producer needed, just a new reader here.
            string? json = await _redisHelper.GetStringAsync("Exchange:NSE");
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                return obj["state"]?.ToString() switch
                {
                    "Opened" => "Opened",
                    "PreOpened" => "PreOpened",
                    "PreClosed" => "PreClosed",
                    "Closed" => "Closed",
                    "Initiated" => "Initiated",
                    var other => other,
                };
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "PositionExitFunction: could not parse Exchange:NSE cache: {Raw}", json);
                return null;
            }
        }

        private Task PushAlertAsync(PositionAlertRecord alert)
        {
            string key = $"Alert:Feed:{IstClock.TodayIso()}";
            string json = JsonSerializer.Serialize(alert);
            return _redisHelper.PushToListAsync(key, json, 5000, TimeSpan.FromDays(3));
        }

        private DataIngestionMinDataEventDto? Unwrap(string eventDataJson)
        {
            var jsonObj = JObject.Parse(eventDataJson);
            string eventDataValue = jsonObj?["Value"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(eventDataValue)) return null;

            try
            {
                return JsonSerializer.Deserialize<DataIngestionMinDataEventDto>(eventDataValue);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "PositionExitFunction: failed to deserialize candle event: {Raw}", eventDataValue);
                return null;
            }
        }
    }
}
