using System.Net;
using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedLibrary.Enums.Exchange;
using SharedLibrary.Events.Exchange;
using WarmUpService.RedisConfig;
using WarmUpService.Strategy;

namespace WarmUpService.Functions
{
    // See ../../../WARMUP_AND_INDICATOR_PLAN.md section 2b for the full design. This first cut is
    // deliberately scoped to the orchestration/visibility piece: react to NSE's Init, fetch the
    // day's data-requirements plan from strategy-live, and report per-requirement whether Redis
    // already has what's needed. The actual cold-start seeding (pulling from ohlc-live when
    // something's missing) isn't built yet — that needs ohlc-live's new validation/fetch capability
    // (plan section 2d) and AggregationService's indicator calculators (section 2e) to exist first,
    // so those paths are clearly logged as not-yet-implemented rather than silently doing nothing.
    public class WarmUpFunctions
    {
        private readonly ILogger<WarmUpFunctions> _logger;
        private readonly RedisHelper _redisHelper;
        private readonly StrategyServiceClient _strategyServiceClient;

        public WarmUpFunctions(ILogger<WarmUpFunctions> logger, RedisHelper redisHelper, StrategyServiceClient strategyServiceClient)
        {
            _logger = logger;
            _redisHelper = redisHelper;
            _strategyServiceClient = strategyServiceClient;
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

            foreach (var instrumentPlan in plan)
            {
                logger.LogInformation(
                    "Instrument {Instrument}: daysToFetch={Days}, needed by [{Strategies}]",
                    instrumentPlan.Instrument, instrumentPlan.DaysToFetch, string.Join(", ", instrumentPlan.StrategyIds));

                foreach (var reason in instrumentPlan.Reasons)
                {
                    await CheckOneReason(logger, instrumentPlan.Instrument, reason);
                }
            }

            logger.LogInformation("WarmUp check complete (trigger: {Trigger})", trigger);
        }

        // Redis key convention for seeded indicator state — see WARMUP_AND_INDICATOR_PLAN.md
        // section 2e. Only period-based Indicator references (EMA, Supertrend) have this shape of
        // state; Pivot Central Range (Indicator, Period == 0) and Expression references are handled
        // separately below since neither is "check a key, seed if missing" in the same way.
        private async Task CheckOneReason(ILogger logger, string instrument, WarmUpReason reason)
        {
            if (reason.DaysNeeded == 0)
            {
                logger.LogInformation("  {Instrument} {Timeframe} {Reference} — live-only, no warm-up needed", instrument, reason.Timeframe, reason.Reference);
                return;
            }

            if (reason.Type == "Indicator" && reason.Period == 0)
            {
                // Pivot Central Range-shaped: recomputed fresh every morning regardless of what's in
                // Redis (see plan doc — it has no live phase, so there's nothing to "already have").
                logger.LogWarning(
                    "  {Instrument} {Timeframe} {Reference} — parameterless indicator, needs fresh daily computation from ohlc-live (NOT YET IMPLEMENTED — see plan section 2d/2e)",
                    instrument, reason.Timeframe, reason.Reference);
                return;
            }

            if (reason.Type == "Indicator")
            {
                string key = $"Indicator:Running:{instrument}:{reason.Timeframe}:{reason.Reference}:{reason.Period}:{reason.Multiplier}";
                bool exists = await _redisHelper.KeyExistsAsync(key);

                if (exists)
                {
                    logger.LogInformation(
                        "  {Instrument} {Timeframe} {Reference}({Period},{Multiplier}) — already seeded ({Key})",
                        instrument, reason.Timeframe, reason.Reference, reason.Period, reason.Multiplier, key);
                }
                else
                {
                    logger.LogWarning(
                        "  {Instrument} {Timeframe} {Reference}({Period},{Multiplier}) — MISSING, needs cold-start seed from ohlc-live (NOT YET IMPLEMENTED). Key: {Key}",
                        instrument, reason.Timeframe, reason.Reference, reason.Period, reason.Multiplier, key);
                }
                return;
            }

            // Type == "Expression" with a RelativePosition (e.g. "Closing Price" / "Previous") — a
            // historical value lookup, not an ongoing indicator calculation with its own Redis state.
            logger.LogWarning(
                "  {Instrument} {Timeframe} {Reference} (RelativePosition={RelativePosition}) — needs a historical value from ohlc-live (NOT YET IMPLEMENTED)",
                instrument, reason.Timeframe, reason.Reference, reason.RelativePosition);
        }
    }
}
