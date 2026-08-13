using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StrategyService.Common;
using StrategyService.Position;
using StrategyService.RedisConfig;
using StrategyService.Strategy;

namespace StrategyService.Alerts
{
    // GET /api/alerts — bundles today's per-strategy virtual position summaries plus the attributed,
    // reverse-chronological alert log in one response, same "bundle related concerns" precedent
    // GetRuleStatus's response already sets (StrategySources/PositionGate/Long/Short together).
    public class AlertFunctions
    {
        private readonly ILogger<AlertFunctions> _logger;
        private readonly RedisHelper _redisHelper;

        private const int MaxAlertsReturned = 200;

        public AlertFunctions(ILogger<AlertFunctions> logger, RedisHelper redisHelper)
        {
            _logger = logger;
            _redisHelper = redisHelper;
        }

        [Function(nameof(GetAlerts))]
        public async Task<HttpResponseData> GetAlerts(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "alerts")] HttpRequestData req)
        {
            string today = IstClock.TodayIso();
            var deployed = StrategyMaker.GetAllDeployed().ToList();

            var positions = new List<PositionSummary>();
            foreach (var (id, strategy) in deployed)
            {
                var ts = strategy.Strategies?.FirstOrDefault();
                if (ts == null) continue;
                positions.Add(await BuildPositionSummaryAsync(id, strategy.StrategyName ?? id, ts));
            }

            var alerts = await BuildAlertLogAsync(today, deployed);

            var response = new AlertsResponse(today, positions, alerts, IstClock.Now().ToString("yyyy-MM-ddTHH:mm:ss"));
            return await JsonResponse(req, HttpStatusCode.OK, response);
        }

        private async Task<PositionSummary> BuildPositionSummaryAsync(string strategyId, string strategyName, TradingStrategy ts)
        {
            var stOperand = FindAnySupertrendOperand(ts);
            if (stOperand?.Properties?.Instrument == null || !InstrumentMapper.TryResolve(stOperand.Properties.Instrument, out var resolved) || resolved == null)
            {
                return new PositionSummary(strategyId, strategyName, "", "", 0, "NotTrackable",
                    null, null, null, null, null, null, null, null, null);
            }

            var hash = await _redisHelper.GetHashAsync(PositionState.KeyFor(strategyId));
            var position = PositionState.FromHash(hash);

            if (position == null)
            {
                return new PositionSummary(strategyId, strategyName, stOperand.Properties.Instrument, resolved.Ticker, resolved.LotSize,
                    "NotYetEntered", null, null, null, null, null, null, null, null, null);
            }

            decimal? currentProfit = null;
            if (position.Status == "Open" && position.EntryPrice.HasValue)
            {
                var candleHash = await _redisHelper.GetHashAsync($"Candle:Last1Min:{position.Ticker}");
                if (candleHash.TryGetValue("Close", out var closeRaw) && decimal.TryParse(closeRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                {
                    currentProfit = position.Side == "Long" ? close - position.EntryPrice.Value : position.EntryPrice.Value - close;
                }
            }

            string? timeInTrade = null;
            if (position.Status == "Open" && position.EntryTime.HasValue)
            {
                var span = IstClock.Now() - position.EntryTime.Value;
                timeInTrade = span.TotalMinutes < 1 ? "<1m" : $"{(int)span.TotalMinutes}m";
            }

            return new PositionSummary(
                strategyId, strategyName, position.Instrument, position.Ticker, position.LotSize,
                position.Status, position.Side, position.EntryPrice,
                position.EntryTime?.ToString("yyyy-MM-ddTHH:mm:ss"), position.InitialStopLoss,
                currentProfit, timeInTrade,
                position.ExitPrice, position.ExitTime?.ToString("yyyy-MM-ddTHH:mm:ss"), position.ExitReason);
        }

        private static Operand? FindAnySupertrendOperand(TradingStrategy ts)
        {
            foreach (var rules in new[] { ts.LongEntry?.EntryRules, ts.ShortEntry?.EntryRules })
            {
                if (rules == null) continue;
                foreach (var rule in rules)
                {
                    foreach (var operand in new[] { rule.LeftOperand, rule.RightOperand })
                    {
                        if (operand?.Type == "Indicator" &&
                            (string.Equals(operand.Value, "Supertrend", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(operand.Value, "Adaptive Supertrend", StringComparison.OrdinalIgnoreCase)))
                            return operand;
                    }
                }
            }
            return null;
        }

        private async Task<List<AlertLogEntry>> BuildAlertLogAsync(string today, List<(string Id, Strategy.Strategy Strategy)> deployed)
        {
            var requirements = StrategyMaker.GetDeployedDataRequirements();
            var namesById = deployed.ToDictionary(d => d.Id, d => d.Strategy.StrategyName ?? d.Id);

            string key = $"Alert:Feed:{today}";
            var rawEntries = await _redisHelper.GetListRangeAsync(key);

            var log = new List<AlertLogEntry>();
            foreach (var raw in rawEntries)
            {
                RawAlertFeedRecord? record;
                try
                {
                    record = JsonSerializer.Deserialize<RawAlertFeedRecord>((string)raw!);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "GetAlerts: skipping unparseable alert feed entry: {Raw}", (string)raw!);
                    continue;
                }
                if (record?.Kind == null) continue;

                if (record.Kind == "PositionEvent")
                {
                    log.Add(new AlertLogEntry(
                        record.Kind, record.AlertType ?? "", record.Instrument ?? "", record.Ticker ?? "", "",
                        null, null, null, null, null, null, null, null, null,
                        record.StrategyId != null ? new List<string> { record.StrategyId } : new List<string>(),
                        record.StrategyName != null ? new List<string> { record.StrategyName } : new List<string>(),
                        record.Side, record.EntryPrice, record.ExitPrice, record.Reason,
                        record.WindowsStartTime.ToString("yyyy-MM-ddTHH:mm:ss"), record.ProducedAt));
                }
                else if (record.Kind == "IndicatorSignal" && record.Instrument != null && record.Timeframe != null && record.Reference != null)
                {
                    var (ids, names) = AlertAttributionResolver.Resolve(
                        record.Instrument, record.Timeframe, record.Reference, record.Period ?? 0, record.Multiplier ?? 0,
                        requirements, namesById);

                    log.Add(new AlertLogEntry(
                        record.Kind, record.AlertType ?? "", record.Instrument, record.Ticker ?? "", record.Timeframe,
                        record.Reference, record.Period, record.Multiplier, record.Value, record.PreviousValue,
                        record.Direction, record.PreviousDirection, record.PenetratedPoints, record.Close,
                        ids, names,
                        null, null, null, null,
                        record.WindowsStartTime.ToString("yyyy-MM-ddTHH:mm:ss"), record.ProducedAt));
                }
            }

            return log
                .OrderByDescending(e => e.WindowsStartTime)
                .ThenByDescending(e => e.ProducedAt)
                .Take(MaxAlertsReturned)
                .ToList();
        }

        [Function(nameof(AlertsOptions))]
        public HttpResponseData AlertsOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "alerts")] HttpRequestData req)
        {
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCors(response);
            return response;
        }

        // Same convention StrategyFunctions.cs's own private copy of these establishes — camelCase
        // on the wire, CORS-open (dashboard calls this cross-origin).
        private static readonly JsonSerializerOptions HttpJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private static async Task<HttpResponseData> JsonResponse(HttpRequestData req, HttpStatusCode status, object payload)
        {
            var response = req.CreateResponse(status);
            AddCors(response);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(JsonSerializer.Serialize(payload, HttpJsonOptions));
            return response;
        }

        private static void AddCors(HttpResponseData response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }
    }
}
