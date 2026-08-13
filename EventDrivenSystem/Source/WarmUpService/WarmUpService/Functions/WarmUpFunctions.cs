using System.Globalization;
using System.Net;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedLibrary.Enums.Exchange;
using SharedLibrary.Events.Exchange;
using StackExchange.Redis;
using WarmUpService.Common;
using WarmUpService.Indicators;
using WarmUpService.Manifest;
using WarmUpService.Ohlc;
using WarmUpService.RedisConfig;
using WarmUpService.Strategy;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace WarmUpService.Functions
{
    // See ../../../WARMUP_AND_INDICATOR_PLAN.md section 2b for the full design. React to NSE's
    // Init, fetch the day's data-requirements plan from strategy-live, then for each requirement:
    // if Redis already has valid state, leave it (continuous indicators carry forward correctly
    // from a healthy previous session — re-seeding isn't the normal case, confirming what's there
    // is). If it's genuinely missing, cold-start it from ohlc-live's historical data. Every
    // period-based instance (seeded fresh or already present) is written into today's manifest —
    // AggregationService's live calculators read that, not this service's logic, to know what to
    // keep updating through the day.
    public class WarmUpFunctions
    {
        private readonly ILogger<WarmUpFunctions> _logger;
        private readonly RedisHelper _redisHelper;
        private readonly StrategyServiceClient _strategyServiceClient;
        private readonly OhlcServiceClient _ohlcServiceClient;

        // Indicator state has no staleness/re-seed policy yet (WARMUP_AND_INDICATOR_PLAN.md section
        // 4 explicitly tables that pending a maintenance-taxonomy design) — this TTL exists purely
        // so an instrument dropped from every strategy eventually stops occupying Redis, not as a
        // freshness signal. Every write (this seed, and later AggregationService's live updates)
        // refreshes it, so it never actually expires while the instance stays active.
        private const int IndicatorStateTtlDays = 7;
        private const string ManifestKey = "Indicator:Manifest:Active";

        public WarmUpFunctions(ILogger<WarmUpFunctions> logger, RedisHelper redisHelper, StrategyServiceClient strategyServiceClient, OhlcServiceClient ohlcServiceClient)
        {
            _logger = logger;
            _redisHelper = redisHelper;
            _strategyServiceClient = strategyServiceClient;
            _ohlcServiceClient = ohlcServiceClient;
        }

        // NSE + Init only, for now — index/spot strategies, first cut (see plan doc). Every other
        // exchange event (NSE's PreOpen/Open/PreClose/Close, and anything from NFO) is ignored here.
        [Function("WarmUpOnExchangeInit")]
        public async Task Run(
            [KafkaTrigger("%KAFKA_BROKER_URL%",
                "live-exchange-workflow-topic",
                AuthenticationMode = BrokerAuthenticationMode.Plain,
                ConsumerGroup = "live-warmup-exchange-consumer")] string eventDataJson,
            FunctionContext context)
        {
            var logger = context.GetLogger("WarmUpOnExchangeInit");
            var eventDataValue = string.Empty;

            var jsonObj = JObject.Parse(eventDataJson);
            if (jsonObj?["Value"] != null)
                eventDataValue = jsonObj["Value"]?.ToString() ?? string.Empty;

            var exchangeEvent = JsonConvert.DeserializeObject<ExchangeEvent>(eventDataValue);
            if (exchangeEvent == null)
            {
                logger.LogWarning("Could not deserialize exchange event: {Raw}", eventDataValue);
                return;
            }

            if (!string.Equals(exchangeEvent.ExchangeName, "NSE", StringComparison.OrdinalIgnoreCase) ||
                exchangeEvent.ExchangeTimerAction != ExchangeActionEnum.Init)
            {
                logger.LogInformation("Ignoring exchange event: {Exchange} {Action}", exchangeEvent.ExchangeName, exchangeEvent.ExchangeTimerAction);
                return;
            }

            try
            {
                await RunWarmUp(logger, $"NSE Init @ {exchangeEvent.Date}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WarmUp failed for NSE Init @ {Date}", exchangeEvent.Date);
            }
        }

        // Manual trigger for testing/verification without waiting for the next real Init (which
        // only fires once a day at 09:00 IST) — runs the identical logic the Kafka trigger does.
        [Function("WarmUpManualTrigger")]
        public async Task<HttpResponseData> ManualTrigger(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "warmup/run")] HttpRequestData req)
        {
            var response = req.CreateResponse();
            try
            {
                await RunWarmUp(_logger, "manual trigger");
                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new { status = "completed", message = "See logs for per-requirement detail." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual warm-up trigger failed");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { error = ex.Message });
            }
            return response;
        }

        private async Task RunWarmUp(ILogger logger, string trigger)
        {
            logger.LogInformation("WarmUp starting (trigger: {Trigger})", trigger);

            var plan = await _strategyServiceClient.GetWarmUpPlanAsync();
            logger.LogInformation("Warm-up plan covers {Count} instrument(s)", plan.Count);

            var manifest = new List<ActiveIndicatorInstance>();

            foreach (var instrumentPlan in plan)
            {
                logger.LogInformation(
                    "Instrument {Instrument}: daysToFetch={Days}, needed by [{Strategies}]",
                    instrumentPlan.Instrument, instrumentPlan.DaysToFetch, string.Join(", ", instrumentPlan.StrategyIds));

                await ProcessInstrument(logger, instrumentPlan, manifest);
            }

            // A strategy can reference the same indicator instance from more than one rule (e.g.
            // Supertrend(20,4) on 5-min shows up in both an entry rule and an update-stop-loss rule
            // in the deployed second-income config) — CheckOneReason adds one manifest entry per
            // reason it processes, so the same instance can land here more than once. It's still
            // exactly one thing to compute, not N: dedupe before writing, or AggregationService's
            // dispatcher would redundantly recompute (and re-publish to Kafka) the same instance
            // once per duplicate entry, on every single candle.
            var dedupedManifest = manifest.Distinct().ToList();
            await WriteManifestAsync(logger, dedupedManifest);

            logger.LogInformation(
                "WarmUp complete (trigger: {Trigger}) — {Count} active indicator instance(s) in today's manifest ({RawCount} reason(s) referenced them)",
                trigger, dedupedManifest.Count, manifest.Count);
        }

        // One historical fetch per instrument (not per reason) — instrumentPlan.DaysToFetch is
        // already the max of every reason's own DaysNeeded under this instrument, so a single pull
        // covers all of them; each reason then builds whatever timeframe it needs from the same raw
        // 1-min series (cached per distinct timeframe, since e.g. two Supertrend/EMA reasons on the
        // same "5 Minutes" shouldn't rebuild that rollup twice).
        private async Task ProcessInstrument(ILogger logger, InstrumentWarmUpPlan instrumentPlan, List<ActiveIndicatorInstance> manifest)
        {
            bool needsHistory = instrumentPlan.Reasons.Any(r => r.DaysNeeded > 0);
            if (!needsHistory)
            {
                foreach (var r in instrumentPlan.Reasons)
                    logger.LogInformation("  {Instrument} {Timeframe} {Reference} — live-only, no warm-up needed", instrumentPlan.Instrument, r.Timeframe, r.Reference);
                return;
            }

            if (!InstrumentMapper.TryResolve(instrumentPlan.Instrument, out var resolved) || resolved == null)
            {
                logger.LogWarning(
                    "  {Instrument} — no known ticker/exchange mapping, skipping all {Count} reason(s) for this instrument. Add it to InstrumentMapper.",
                    instrumentPlan.Instrument, instrumentPlan.Reasons.Count);
                return;
            }

            var rawBars = await _ohlcServiceClient.FetchHistoricalCandlesAsync(resolved.Exchange, resolved.Ticker, instrumentPlan.DaysToFetch);
            if (rawBars == null)
            {
                logger.LogWarning(
                    "  {Instrument} ({Ticker}/{Exchange}) — could not fetch sufficient history ({Days} day(s) needed); all period-based reasons stay MISSING.",
                    instrumentPlan.Instrument, resolved.Ticker, resolved.Exchange, instrumentPlan.DaysToFetch);
                return;
            }
            logger.LogInformation(
                "  {Instrument} ({Ticker}/{Exchange}) — fetched {Count} raw 1-min bar(s) covering {Days} trading day(s).",
                instrumentPlan.Instrument, resolved.Ticker, resolved.Exchange, rawBars.Count, instrumentPlan.DaysToFetch);

            var timeframeCache = new Dictionary<int, List<HistoricalBar>>();
            List<HistoricalBar> BarsFor(int minutes) =>
                timeframeCache.TryGetValue(minutes, out var cached) ? cached : timeframeCache[minutes] = TimeframeBuilder.Build(rawBars, minutes);

            foreach (var reason in instrumentPlan.Reasons)
                await CheckOneReason(logger, instrumentPlan.Instrument, reason, resolved, rawBars, BarsFor, manifest);
        }

        // Redis key convention for seeded indicator state — see WARMUP_AND_INDICATOR_PLAN.md
        // section 2e. Only period-based Indicator references (EMA, Supertrend) have this shape of
        // state; Pivot Central Range (Indicator, Period == 0) and Expression references are handled
        // separately below since neither is "check a key, seed if missing" in the same way.
        private async Task CheckOneReason(
            ILogger logger, string instrument, WarmUpReason reason, InstrumentMapper.ResolvedInstrument resolved,
            List<RawCandle> rawBars, Func<int, List<HistoricalBar>> barsFor, List<ActiveIndicatorInstance> manifest)
        {
            if (reason.DaysNeeded == 0)
            {
                logger.LogInformation("  {Instrument} {Timeframe} {Reference} — live-only, no warm-up needed", instrument, reason.Timeframe, reason.Reference);
                return;
            }

            string key = $"Indicator:Running:{instrument}:{reason.Timeframe}:{reason.Reference}:{reason.Period}:{reason.Multiplier}";

            if (reason.Type == "Indicator" && reason.Period == 0)
            {
                // Pivot Central Range-shaped: no live phase at all, recomputed fresh every morning
                // regardless of what's already in Redis — see plan doc, there's nothing to "carry
                // forward" since it's inherently derived from yesterday's session.
                var dayBars = TimeframeBuilder.Build(rawBars, 375); // one "1 Day" bar per trading day present
                if (dayBars.Count == 0)
                {
                    logger.LogWarning("  {Instrument} {Timeframe} {Reference} — no prior-session data to compute from.", instrument, reason.Timeframe, reason.Reference);
                    return;
                }

                var priorSession = dayBars[^1]; // most recent complete trading day in the fetched window
                var pcr = PivotCentralRangeCalculator.Compute(priorSession.High, priorSession.Low, priorSession.Close, priorSession.WindowsStartTime);

                await _redisHelper.SetHashAsync(key, new HashEntry[]
                {
                    new("Pivot", pcr.Pivot.ToString(CultureInfo.InvariantCulture)),
                    new("TopCentral", pcr.TopCentral.ToString(CultureInfo.InvariantCulture)),
                    new("BottomCentral", pcr.BottomCentral.ToString(CultureInfo.InvariantCulture)),
                    new("Width", pcr.Width.ToString(CultureInfo.InvariantCulture)),
                    // Yesterday's raw close — computed right here to seed PCR itself, but previously
                    // discarded the moment this method returned. The deployed strategy's own
                    // TradingSessionRules gate compares PCR against "0.0038 * Closing Price
                    // (Previous)" — without persisting this, nothing downstream could ever evaluate
                    // that comparison for real. Piggybacking it on the PCR hash rather than a new key,
                    // since it's a byproduct of this exact computation, not an independent value.
                    new("PriorClose", priorSession.Close.ToString(CultureInfo.InvariantCulture)),
                    new("SessionDate", pcr.SessionDate.ToString("yyyy-MM-dd")),
                }, TimeSpan.FromDays(IndicatorStateTtlDays));

                logger.LogInformation(
                    "  {Instrument} {Timeframe} {Reference} — recomputed fresh (Width={Width}, session {Session:yyyy-MM-dd}). Key: {Key}",
                    instrument, reason.Timeframe, reason.Reference, pcr.Width, pcr.SessionDate, key);
                return;
            }

            if (reason.Type == "Indicator")
            {
                int timeframeMinutes = TimeframeParser.ParseMinutes(reason.Timeframe);

                // Goes into today's manifest either way — already-seeded or freshly-seeded, this
                // instance is active today and AggregationService needs to know to keep updating it.
                manifest.Add(new ActiveIndicatorInstance(instrument, resolved.Ticker, resolved.Exchange, reason.Timeframe, timeframeMinutes, reason.Reference, reason.Period, reason.Multiplier));

                bool exists = await _redisHelper.KeyExistsAsync(key);
                if (exists)
                {
                    logger.LogInformation(
                        "  {Instrument} {Timeframe} {Reference}({Period},{Multiplier}) — already seeded, carrying forward ({Key})",
                        instrument, reason.Timeframe, reason.Reference, reason.Period, reason.Multiplier, key);
                    return;
                }

                if (timeframeMinutes <= 0)
                {
                    logger.LogWarning("  {Instrument} {Timeframe} {Reference} — could not parse timeframe, cannot seed.", instrument, reason.Timeframe, reason.Reference);
                    return;
                }

                var bars = barsFor(timeframeMinutes);

                if (string.Equals(reason.Reference, "EMA", StringComparison.OrdinalIgnoreCase))
                {
                    await SeedEma(logger, instrument, reason, key, bars);
                }
                else if (string.Equals(reason.Reference, "Supertrend", StringComparison.OrdinalIgnoreCase))
                {
                    await SeedSupertrend(logger, instrument, reason, key, bars);
                }
                else if (string.Equals(reason.Reference, "Adaptive Supertrend", StringComparison.OrdinalIgnoreCase))
                {
                    await SeedAdaptiveSupertrend(logger, instrument, reason, key, bars);
                }
                else
                {
                    logger.LogWarning(
                        "  {Instrument} {Timeframe} {Reference}({Period},{Multiplier}) — unknown indicator type, no calculator registered. NOT seeded.",
                        instrument, reason.Timeframe, reason.Reference, reason.Period, reason.Multiplier);
                }
                return;
            }

            // Type == "Expression" with a RelativePosition (e.g. "Closing Price" / "Previous") — a
            // historical value lookup, not an ongoing indicator calculation with its own Redis state.
            // Not built this pass — the plan doc doesn't yet define a Redis key convention for it.
            logger.LogWarning(
                "  {Instrument} {Timeframe} {Reference} (RelativePosition={RelativePosition}) — needs a historical value from ohlc-live (NOT YET IMPLEMENTED)",
                instrument, reason.Timeframe, reason.Reference, reason.RelativePosition);
        }

        private async Task SeedEma(ILogger logger, string instrument, WarmUpReason reason, string key, List<HistoricalBar> bars)
        {
            var seed = EmaSeeder.Seed(bars, reason.Period);
            if (!seed.IsSeeded)
            {
                logger.LogWarning(
                    "  {Instrument} {Timeframe} EMA({Period}) — only {Seen} bar(s) available, need {Period}, cannot seed yet.",
                    instrument, reason.Timeframe, reason.Period, seed.SeedBarsSeenSoFar, reason.Period);
                return;
            }

            await _redisHelper.SetHashAsync(key, new HashEntry[]
            {
                new("LastEma", seed.LastEma.ToString(CultureInfo.InvariantCulture)),
                new("LastClose", seed.LastClose?.ToString(CultureInfo.InvariantCulture) ?? ""),
                new("SeedBarsSeenSoFar", seed.SeedBarsSeenSoFar.ToString(CultureInfo.InvariantCulture)),
                new("IsSeeded", "true"),
                new("LastBarWindowsStartTime", seed.LastBarWindowsStartTime?.ToString("yyyy-MM-ddTHH:mm:ss") ?? ""),
            }, TimeSpan.FromDays(IndicatorStateTtlDays));

            logger.LogInformation(
                "  {Instrument} {Timeframe} EMA({Period}) — seeded from {BarCount} bar(s), LastEma={LastEma} as of {AsOf}. Key: {Key}",
                instrument, reason.Timeframe, reason.Period, bars.Count, seed.LastEma, seed.LastBarWindowsStartTime, key);
        }

        private async Task SeedSupertrend(ILogger logger, string instrument, WarmUpReason reason, string key, List<HistoricalBar> bars)
        {
            var (state, window) = SupertrendSeeder.Seed(bars, reason.Period, reason.Multiplier);
            if (!state.IsSeeded)
            {
                logger.LogWarning(
                    "  {Instrument} {Timeframe} Supertrend({Period},{Multiplier}) — only {BarCount} bar(s) available, need at least {Needed}, cannot seed yet.",
                    instrument, reason.Timeframe, reason.Period, reason.Multiplier, bars.Count, reason.Period + 1);
                return;
            }

            await _redisHelper.SetHashAsync(key, new HashEntry[]
            {
                new("TrendDirection", state.TrendDirection),
                new("PrevUpperBand", state.PrevUpperBand.ToString(CultureInfo.InvariantCulture)),
                new("PrevLowerBand", state.PrevLowerBand.ToString(CultureInfo.InvariantCulture)),
                new("PrevClose", state.PrevClose.ToString(CultureInfo.InvariantCulture)),
                new("Atr", state.Atr.ToString(CultureInfo.InvariantCulture)),
                new("IsSeeded", "true"),
                new("LastBarWindowsStartTime", state.LastBarWindowsStartTime?.ToString("yyyy-MM-ddTHH:mm:ss") ?? ""),
            }, TimeSpan.FromDays(IndicatorStateTtlDays));

            // The rolling True-Range window — the "List" half of Supertrend's hybrid persistence
            // (plan doc section 2e). Cleared and rebuilt in chronological order rather than pushed
            // incrementally, since a seed run always has the full window available at once.
            string windowKey = $"Indicator:Window:{instrument}:{reason.Timeframe}:{reason.Reference}:{reason.Period}:{reason.Multiplier}";
            await _redisHelper.DeleteKeyAsync(windowKey);
            foreach (var entry in window)
            {
                string json = JsonSerializer.Serialize(entry);
                await _redisHelper.PushToListAsync(windowKey, json, reason.Period, TimeSpan.FromDays(IndicatorStateTtlDays));
            }

            logger.LogInformation(
                "  {Instrument} {Timeframe} Supertrend({Period},{Multiplier}) — seeded from {BarCount} bar(s), Trend={Trend}, Atr={Atr} as of {AsOf}. Key: {Key}, Window: {WindowKey}",
                instrument, reason.Timeframe, reason.Period, reason.Multiplier, bars.Count, state.TrendDirection, state.Atr, state.LastBarWindowsStartTime, key, windowKey);
        }

        private async Task SeedAdaptiveSupertrend(ILogger logger, string instrument, WarmUpReason reason, string key, List<HistoricalBar> bars)
        {
            var (state, window) = AdaptiveSupertrendSeeder.Seed(bars, reason.Period, reason.Multiplier);
            if (!state.IsSeeded)
            {
                logger.LogWarning(
                    "  {Instrument} {Timeframe} Adaptive Supertrend({Period},{Multiplier}) — only {BarCount} bar(s) available, need at least {Needed} (ATR length + {Window}-bar K-Means training window), cannot seed yet.",
                    instrument, reason.Timeframe, reason.Period, reason.Multiplier, bars.Count,
                    reason.Period + AdaptiveVolatilityClusterer.TrainingWindow, AdaptiveVolatilityClusterer.TrainingWindow);
                return;
            }

            await _redisHelper.SetHashAsync(key, new HashEntry[]
            {
                new("TrendDirection", state.TrendDirection),
                new("PrevUpperBand", state.PrevUpperBand.ToString(CultureInfo.InvariantCulture)),
                new("PrevLowerBand", state.PrevLowerBand.ToString(CultureInfo.InvariantCulture)),
                new("PrevClose", state.PrevClose.ToString(CultureInfo.InvariantCulture)),
                new("Atr", state.Atr.ToString(CultureInfo.InvariantCulture)),
                new("RawAtr", state.RawAtr.ToString(CultureInfo.InvariantCulture)),
                new("VolatilityCluster", state.VolatilityCluster),
                new("ClusterHigh", state.ClusterHigh.ToString(CultureInfo.InvariantCulture)),
                new("ClusterMedium", state.ClusterMedium.ToString(CultureInfo.InvariantCulture)),
                new("ClusterLow", state.ClusterLow.ToString(CultureInfo.InvariantCulture)),
                new("IsSeeded", "true"),
                new("LastBarWindowsStartTime", state.LastBarWindowsStartTime?.ToString("yyyy-MM-ddTHH:mm:ss") ?? ""),
            }, TimeSpan.FromDays(IndicatorStateTtlDays));

            // The rolling raw-ATR window — the K-Means training set, the "List" half of this
            // indicator's hybrid persistence (same mechanism as regular Supertrend's True-Range
            // window). Cleared and rebuilt in chronological order rather than pushed incrementally,
            // since a seed run always has the full window available at once.
            string windowKey = $"Indicator:Window:{instrument}:{reason.Timeframe}:{reason.Reference}:{reason.Period}:{reason.Multiplier}";
            await _redisHelper.DeleteKeyAsync(windowKey);
            foreach (var entry in window)
            {
                string json = JsonSerializer.Serialize(entry);
                await _redisHelper.PushToListAsync(windowKey, json, AdaptiveVolatilityClusterer.TrainingWindow, TimeSpan.FromDays(IndicatorStateTtlDays));
            }

            logger.LogInformation(
                "  {Instrument} {Timeframe} Adaptive Supertrend({Period},{Multiplier}) — seeded from {BarCount} bar(s), Trend={Trend}, Cluster={Cluster} (Atr={Atr}, raw={RawAtr}) as of {AsOf}. Key: {Key}, Window: {WindowKey}",
                instrument, reason.Timeframe, reason.Period, reason.Multiplier, bars.Count, state.TrendDirection, state.VolatilityCluster, state.Atr, state.RawAtr, state.LastBarWindowsStartTime, key, windowKey);
        }

        private async Task WriteManifestAsync(ILogger logger, List<ActiveIndicatorInstance> manifest)
        {
            string json = JsonSerializer.Serialize(manifest);
            await _redisHelper.SetStringAsync(ManifestKey, json, TimeSpan.FromDays(IndicatorStateTtlDays));
            logger.LogInformation("Wrote {Count} active indicator instance(s) to {Key} for AggregationService's live calculators to read.", manifest.Count, ManifestKey);
        }
    }
}
