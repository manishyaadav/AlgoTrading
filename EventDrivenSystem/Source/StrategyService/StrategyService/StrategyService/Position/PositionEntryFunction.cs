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
    // Reacts to every completed Supertrend/Adaptive Supertrend bar (the same events
    // IndicatorDispatcher.PublishAsync already produces in AggregationService, on
    // live-indicator-supertrend-topic / live-indicator-adaptive-supertrend-topic) and, for every
    // deployed strategy whose LongEntry/ShortEntry references this exact
    // (Instrument, Timeframe, Reference, Period, Multiplier) instance, opens a virtual position —
    // or re-opens one after an earlier exit — per the design in vast-whistling-zebra.md:
    //
    //   1. First bar of the day (WindowsStartTime == 9:15, session open) and no position yet today
    //      -> enter, side = the bar's own Supertrend color. This is the literal "in position as soon
    //      as the first N-minute candle closes" the feature was asked for — not a real EntryRules
    //      evaluation.
    //   2. A position exists for today with Status=="Flat" (PositionExitFunction closed it earlier)
    //      -> re-enter only on a genuine flip (Direction != PreviousDirection), not merely "still the
    //      same color" — per the user's own answer on re-entry behavior.
    //   3. Idempotency: skip if this exact bar (WindowsStartTime) already triggered the current/most
    //      recent entry — guards a redelivered event from double-entering.
    //
    // Entry price comes from Candle:Last1Min (AggregationService's LatestMinuteCandleFunction), not
    // the Supertrend event's own Value — that's the band, not price; using it as entry price would
    // make Initial Stop Loss always the (fixed, small) band-to-band distance rather than a real
    // price distance.
    public class PositionEntryFunction
    {
        private readonly ILogger<PositionEntryFunction> _logger;
        private readonly RedisHelper _redisHelper;

        public PositionEntryFunction(ILogger<PositionEntryFunction> logger, RedisHelper redisHelper)
        {
            _logger = logger;
            _redisHelper = redisHelper;
        }

        [Function("PositionEntryOnSupertrend")]
        public Task RunSupertrend(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-indicator-supertrend-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "strategy-live-position-entry")] string eventDataJson, FunctionContext context)
            => HandleAsync(eventDataJson);

        [Function("PositionEntryOnAdaptiveSupertrend")]
        public Task RunAdaptiveSupertrend(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-indicator-adaptive-supertrend-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "strategy-live-position-entry")] string eventDataJson, FunctionContext context)
            => HandleAsync(eventDataJson);

        private async Task HandleAsync(string eventDataJson)
        {
            var evt = Unwrap(eventDataJson);
            if (evt == null) return;

            if (evt.WindowsStartTime.TimeOfDay != new TimeSpan(9, 15, 0) && string.IsNullOrEmpty(evt.Direction))
                return; // no direction at all — an unseeded instance, nothing to act on either way

            foreach (var (id, strategy) in StrategyMaker.GetAllDeployed())
            {
                var ts = strategy.Strategies?.FirstOrDefault();
                if (ts == null) continue;

                var (side, matched) = MatchSide(ts, evt);
                if (side == null || matched == null) continue;

                try
                {
                    await EvaluateOneAsync(id, strategy.StrategyName ?? id, side, matched, evt);
                }
                catch (Exception ex)
                {
                    // One strategy's entry logic failing shouldn't block another's, same posture as
                    // every other per-item loop in this codebase.
                    _logger.LogError(ex, "PositionEntryFunction: failed to evaluate entry for strategy {StrategyId}", id);
                }
            }
        }

        private async Task EvaluateOneAsync(string strategyId, string strategyName, string side, Operand matchedOperand, IndicatorOutputEventDto evt)
        {
            var props = matchedOperand.Properties!;
            string positionKey = PositionState.KeyFor(strategyId);
            var existingHash = await _redisHelper.GetHashAsync(positionKey);
            var existing = PositionState.FromHash(existingHash);

            if (existing?.LastEntryWindowsStartTime == evt.WindowsStartTime)
                return; // this exact bar already triggered the current/most-recent entry — redelivery, no-op

            bool isFirstBarOfDay = evt.WindowsStartTime.TimeOfDay == new TimeSpan(9, 15, 0);
            bool enteredToday = existing?.EntryTime?.Date == evt.WindowsStartTime.Date;

            bool shouldEnter;
            if (isFirstBarOfDay && !enteredToday)
            {
                shouldEnter = true; // first entry of the day
            }
            else if (existing?.Status == "Flat" && evt.Direction != evt.PreviousDirection)
            {
                shouldEnter = true; // re-entry: a genuine flip after an earlier exit
            }
            else
            {
                shouldEnter = false;
            }

            if (!shouldEnter) return;
            if (!InstrumentMapper.TryResolve(props.Instrument!, out var resolved) || resolved == null)
            {
                _logger.LogWarning("PositionEntryFunction: {StrategyId} references unmapped instrument {Instrument}, skipping.", strategyId, props.Instrument);
                return;
            }

            decimal entryPrice = await ReadLastMinuteCloseAsync(resolved.Ticker) ?? evt.Value;
            decimal initialStopLoss = Math.Abs(entryPrice - evt.Value);
            DateTime entryTime = evt.WindowsStartTime.AddMinutes(evt.TimeframeMinutes);

            var position = new PositionState(
                Status: "Open",
                Side: side,
                EntryPrice: entryPrice,
                EntryTime: entryTime,
                InitialStopLoss: initialStopLoss,
                Instrument: props.Instrument!,
                Ticker: resolved.Ticker,
                Exchange: resolved.Exchange,
                Timeframe: props.Timeframe!,
                Period: props.Period,
                Multiplier: props.Multiplier,
                LotSize: resolved.LotSize,
                ExitPrice: null, ExitTime: null, ExitReason: null,
                LastEntryWindowsStartTime: evt.WindowsStartTime,
                LastEvaluatedAt: IstClock.Now());

            await _redisHelper.SetHashAsync(positionKey, position.ToHash(), TimeSpan.FromDays(7));

            var alert = new PositionAlertRecord(
                Kind: "PositionEvent", AlertType: "PositionEntered",
                StrategyId: strategyId, StrategyName: strategyName, Instrument: props.Instrument!, Ticker: resolved.Ticker,
                Side: side, EntryPrice: entryPrice, ExitPrice: null, Reason: null,
                WindowsStartTime: evt.WindowsStartTime, ProducedAt: IstClock.Now().ToString("yyyy-MM-ddTHH:mm:ss"));
            await PushAlertAsync(alert);

            _logger.LogInformation("PositionEntryFunction: {StrategyId} entered {Side} at {EntryPrice} (ST={StValue}, InitialSL={Sl})", strategyId, side, entryPrice, evt.Value, initialStopLoss);
        }

        // Finds whichever of LongEntry/ShortEntry's EntryRules references this exact Supertrend
        // instance and returns the corresponding side + the matching operand (for its Properties).
        private static (string? Side, Operand? Operand) MatchSide(TradingStrategy ts, IndicatorOutputEventDto evt)
        {
            var longOperand = FindMatchingOperand(ts.LongEntry?.EntryRules, evt);
            if (longOperand != null) return (evt.Direction == "Up" ? "Long" : "Short", longOperand);

            var shortOperand = FindMatchingOperand(ts.ShortEntry?.EntryRules, evt);
            if (shortOperand != null) return (evt.Direction == "Up" ? "Long" : "Short", shortOperand);

            return (null, null);
        }

        private static Operand? FindMatchingOperand(List<TradingRule>? rules, IndicatorOutputEventDto evt)
        {
            if (rules == null) return null;
            foreach (var rule in rules)
            {
                foreach (var operand in new[] { rule.LeftOperand, rule.RightOperand })
                {
                    if (operand?.Type != "Indicator" || operand.Properties == null) continue;
                    if (!string.Equals(operand.Value, evt.Reference, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(operand.Properties.Instrument, evt.Instrument, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(operand.Properties.Timeframe, evt.Timeframe, StringComparison.OrdinalIgnoreCase)) continue;
                    if (operand.Properties.Period != evt.Period || operand.Properties.Multiplier != evt.Multiplier) continue;
                    return operand;
                }
            }
            return null;
        }

        private async Task<decimal?> ReadLastMinuteCloseAsync(string ticker)
        {
            var hash = await _redisHelper.GetHashAsync($"Candle:Last1Min:{ticker}");
            if (hash.Count == 0 || !hash.TryGetValue("Close", out var raw)) return null;
            return decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var close) ? close : (decimal?)null;
        }

        private Task PushAlertAsync(PositionAlertRecord alert)
        {
            string key = $"Alert:Feed:{IstClock.TodayIso()}";
            string json = System.Text.Json.JsonSerializer.Serialize(alert);
            return _redisHelper.PushToListAsync(key, json, 5000, TimeSpan.FromDays(3));
        }

        private IndicatorOutputEventDto? Unwrap(string eventDataJson)
        {
            var jsonObj = JObject.Parse(eventDataJson);
            string eventDataValue = jsonObj?["Value"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(eventDataValue)) return null;

            try
            {
                return JsonSerializer.Deserialize<IndicatorOutputEventDto>(eventDataValue);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "PositionEntryFunction: failed to deserialize indicator event: {Raw}", eventDataValue);
                return null;
            }
        }
    }
}
