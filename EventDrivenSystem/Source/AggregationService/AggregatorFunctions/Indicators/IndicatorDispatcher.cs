using System.Text.Json;
using AggregatorFunctions.RedisConfig;
using AggregatorFunctions.SharedLibrary.Events.Aggregation.TimeFrame;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using SharedLibrary.Helpers;

namespace AggregatorFunctions.Indicators
{
    // Shared dispatch logic behind all 6 IndicatorDispatcher{N}MinFunction wrappers — one calculator
    // per indicator *type* (EmaCalculator, SupertrendCalculator), driven at runtime by whatever
    // instances today's manifest says are active for this (ticker, timeframe), exactly the pattern
    // WARMUP_AND_INDICATOR_PLAN.md section 2e describes. Adding a new indicator type later means
    // adding one branch here and one new calculator class — the Kafka wiring (the 6 thin per-
    // timeframe functions) never has to change.
    public class IndicatorDispatcher
    {
        private readonly ILogger _logger;
        private readonly IProducer<string, string> _producer;
        private readonly RedisHelper _redisHelper;

        // Matches WarmUpService's own IndicatorStateTtlDays constant — both sides need to agree,
        // since either one can be the last writer of the same Indicator:Running:* key.
        private const int IndicatorStateTtlDays = 7;

        private const string EmaTopic = "live-indicator-ema-topic";
        private const string SupertrendTopic = "live-indicator-supertrend-topic";

        public IndicatorDispatcher(ILogger logger, IProducer<string, string> producer, RedisHelper redisHelper)
        {
            _logger = logger;
            _producer = producer;
            _redisHelper = redisHelper;
        }

        public async Task DispatchAsync(TimeFrameAggregationEvent bar, int timeframeMinutes)
        {
            var manifest = await IndicatorManifestReader.GetActiveAsync(_redisHelper);
            var matches = manifest
                .Where(m => string.Equals(m.Ticker, bar.Ticker, StringComparison.OrdinalIgnoreCase) && m.TimeframeMinutes == timeframeMinutes)
                .ToList();

            if (matches.Count == 0) return; // nothing active for this ticker/timeframe today

            foreach (var instance in matches)
            {
                if (string.Equals(instance.Reference, "EMA", StringComparison.OrdinalIgnoreCase))
                {
                    var value = await EmaCalculator.UpdateAsync(_redisHelper, RunningKey(instance), instance.Period, bar.Close, bar.WindowsStartTime, IndicatorStateTtlDays);
                    if (value == null)
                    {
                        _logger.LogInformation("EMA {Instrument} {Timeframe}({Period}) — not seeded yet, skipping live update.", instance.Instrument, instance.Timeframe, instance.Period);
                        continue;
                    }
                    await PublishAsync(EmaTopic, instance, value.Value, null, bar.WindowsStartTime);
                }
                else if (string.Equals(instance.Reference, "Supertrend", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await SupertrendCalculator.UpdateAsync(
                        _redisHelper, RunningKey(instance), WindowKey(instance), instance.Period, instance.Multiplier,
                        bar.High, bar.Low, bar.Close, bar.WindowsStartTime, IndicatorStateTtlDays);
                    if (result == null)
                    {
                        _logger.LogInformation("Supertrend {Instrument} {Timeframe}({Period},{Multiplier}) — not seeded yet, skipping live update.", instance.Instrument, instance.Timeframe, instance.Period, instance.Multiplier);
                        continue;
                    }
                    await PublishAsync(SupertrendTopic, instance, result.Value.Value, result.Value.Direction, bar.WindowsStartTime);
                }
                // Pivot Central Range never appears in the manifest — WarmUpService deliberately
                // excludes it (no live phase per the plan doc), so no branch is needed for it here.
            }
        }

        private static string RunningKey(ActiveIndicatorInstance i) => $"Indicator:Running:{i.Instrument}:{i.Timeframe}:{i.Reference}:{i.Period}:{i.Multiplier}";
        private static string WindowKey(ActiveIndicatorInstance i) => $"Indicator:Window:{i.Instrument}:{i.Timeframe}:{i.Reference}:{i.Period}:{i.Multiplier}";

        private async Task PublishAsync(string topic, ActiveIndicatorInstance instance, decimal value, string? direction, DateTime windowsStartTime)
        {
            var indianNow = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);
            var evt = new IndicatorOutputEvent
            {
                Instrument = instance.Instrument,
                Ticker = instance.Ticker,
                Timeframe = instance.Timeframe,
                TimeframeMinutes = instance.TimeframeMinutes,
                Reference = instance.Reference,
                Period = instance.Period,
                Multiplier = instance.Multiplier,
                Value = value,
                Direction = direction,
                WindowsStartTime = windowsStartTime,
                Producer = "aggregator.indicator.service",
                ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianNow),
            };

            // {Instrument}:{Timeframe}:{Period}:{Multiplier} — not just {Instrument} — so two
            // different instances of the same indicator on the same ticker (e.g. two EMA periods)
            // still get correct per-instance partition ordering, per plan doc section 2e.
            string key = $"{instance.Instrument}:{instance.Timeframe}:{instance.Period}:{instance.Multiplier}";
            string json = JsonSerializer.Serialize(evt);

            try
            {
                var report = await _producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = json });
                _logger.LogInformation(
                    "Published {Reference} for {Instrument} {Timeframe} to {Topic} @ {TopicPartitionOffset}",
                    instance.Reference, instance.Instrument, instance.Timeframe, topic, report.TopicPartitionOffset);
            }
            catch (ProduceException<string, string> e)
            {
                _logger.LogError(e, "Failed to publish {Reference} for {Instrument} {Timeframe} to {Topic}: {Reason}", instance.Reference, instance.Instrument, instance.Timeframe, topic, e.Error.Reason);
            }
        }
    }
}
