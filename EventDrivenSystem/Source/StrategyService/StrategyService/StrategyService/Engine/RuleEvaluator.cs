using System.Globalization;
using System.Text.RegularExpressions;
using StrategyService.Strategy;

namespace StrategyService.Engine
{
    // Evaluates what CAN be evaluated from real live data today, and honestly marks everything
    // else "unknown" rather than guessing. See WARMUP_AND_INDICATOR_PLAN.md section 4 — no
    // execution engine exists in this codebase; this is read-only visualization of what the rules
    // WOULD currently evaluate to, not a trigger-generating engine. No side effects, no order
    // placement, nothing written back to Redis.
    public static class RuleEvaluator
    {
        // One resolved operand: a numeric value, a text value, or both (Supertrend needs both —
        // its band value compares numerically against EMA, its GREEN/RED compares as text against
        // a Literal). Null Numeric/Text with a non-null UnresolvedReason means nothing backs this
        // operand today.
        private record Resolved(string? Display, decimal? Numeric, string? Text, string? UnresolvedReason)
        {
            public bool IsResolved => UnresolvedReason == null;
            public static Resolved Unresolved(string reason) => new(null, null, null, reason);
        }

        private static readonly Regex MultiplierExpression = new(@"^\s*([\d.]+)\s*\*\s*Closing Price\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<RuleStatusResponse> BuildAsync(string strategyId, Strategy.Strategy strategy, LiveDataSnapshot live)
        {
            var ts = strategy.Strategies?.FirstOrDefault();

            bool isDeployed = !string.IsNullOrEmpty(strategy.DeployedVersion);
            var deployedGate = new GateNode(
                "Gate 1", "Strategy deployed?",
                isDeployed ? "pass" : "fail",
                isDeployed
                    ? $"{strategy.StrategyName} has a deployed version (v{strategy.DeployedVersion})."
                    : $"{strategy.StrategyName} has no deployed version — nothing below applies until it's deployed.",
                new List<ValueChip>());

            string? sessionState = await live.GetSessionStateAsync();
            string sessionStatus = sessionState switch
            {
                null => "unknown",
                "Normal" => "pass",
                _ => "fail", // Holiday / Weekend
            };
            var sessionGate = new GateNode(
                "Gate 2 · Country gate, read from Redis \"India\"", "Is there a session today?",
                sessionStatus,
                sessionState == null
                    ? "Session-state key not found in Redis — country-live/notification-live may not have run yet today."
                    : sessionState == "Normal"
                        ? "Not a holiday, not a weekend — the day is gated open."
                        : $"Today is a {sessionState} — no session, nothing below runs.",
                new List<ValueChip> { new("state", sessionState ?? "—", sessionStatus == "pass" ? "pass" : sessionStatus == "fail" ? "fail" : null) });

            var tradingSessionRules = ts == null
                ? new RuleGroup("Trading Session Rules", "unknown", true, new List<RuleEvaluation>())
                : await EvaluateGroupAsync(ts.TradingSessionRules, live, "Trading Session Rules", isLive: true);

            var positionGate = new GateNode(
                "Gate 4", "In a position?",
                "unknown",
                "No position/order tracking exists in this stack yet — this gate has nothing real to check. The branch below is drawn assuming not in position, since that's the only branch with anything live behind it today.",
                new List<ValueChip>());

            var longStatus = ts?.LongEntry == null
                ? EmptyEntryExit()
                : await EvaluateEntryExitAsync(ts.LongEntry, live);

            var shortStatus = ts?.ShortEntry == null
                ? EmptyEntryExit()
                : await EvaluateEntryExitAsync(ts.ShortEntry, live);

            return new RuleStatusResponse(
                strategyId,
                strategy.StrategyName ?? strategyId,
                strategy.Exchange,
                ts?.Instruments ?? new List<string>(),
                strategy.DeployedVersion,
                deployedGate,
                sessionGate,
                tradingSessionRules,
                positionGate,
                longStatus,
                shortStatus);
        }

        private static EntryExitStatus EmptyEntryExit() => new(
            new RuleGroup("Entry Rules", "unknown", true, new List<RuleEvaluation>()),
            new RuleGroup("Risk Management Rules", "unknown", false, new List<RuleEvaluation>()),
            new RuleGroup("Exit / Stop-Loss Rules", "unknown", false, new List<RuleEvaluation>()));

        private static async Task<EntryExitStatus> EvaluateEntryExitAsync(EntryExitRule rules, LiveDataSnapshot live)
        {
            var entry = await EvaluateGroupAsync(rules.EntryRules, live, "Entry Rules", isLive: true);

            // Risk Management / Update-Stop-Loss / Exit all depend on account or position state
            // that doesn't exist anywhere in this codebase yet (see the Position gate above) — built
            // and shown structurally for reference, deliberately never evaluated (Live: false), so
            // the page never implies a real answer it doesn't have.
            var risk = await EvaluateGroupAsync(rules.RiskManagementRules, live, "Risk Management Rules", isLive: false);

            var exitCombined = new List<TradingRule>();
            exitCombined.AddRange(rules.UpdateStopLossRules ?? new());
            exitCombined.AddRange(rules.ExitRules ?? new());
            var exit = await EvaluateGroupAsync(exitCombined, live, "Exit / Stop-Loss Rules", isLive: false);

            return new EntryExitStatus(entry, risk, exit);
        }

        private static async Task<RuleGroup> EvaluateGroupAsync(List<TradingRule>? rules, LiveDataSnapshot live, string title, bool isLive)
        {
            var evaluations = new List<RuleEvaluation>();
            if (rules != null)
            {
                foreach (var rule in rules.OrderBy(r => r.Sequence))
                    evaluations.Add(isLive ? await EvaluateRuleAsync(rule, live) : NotEvaluated(rule));
            }

            string status = evaluations.Count == 0 ? "unknown" : (isLive ? AggregateStatus(evaluations) : "unknown");
            return new RuleGroup(title, status, isLive, evaluations);
        }

        private static RuleEvaluation NotEvaluated(TradingRule rule) =>
            new(rule, "unknown", null, null, "not evaluated — depends on state this stack doesn't track yet");

        private static async Task<RuleEvaluation> EvaluateRuleAsync(TradingRule rule, LiveDataSnapshot live)
        {
            var left = await ResolveAsync(rule.LeftOperand, live);
            var right = await ResolveAsync(rule.RightOperand, live);

            if (!left.IsResolved || !right.IsResolved)
            {
                string reason = !left.IsResolved && !right.IsResolved
                    ? $"{left.UnresolvedReason}; {right.UnresolvedReason}"
                    : (left.UnresolvedReason ?? right.UnresolvedReason)!;
                return new RuleEvaluation(rule, "unknown", left.Display, right.Display, reason);
            }

            bool? result = Compare(left, right, rule.Operator);
            if (result == null)
                return new RuleEvaluation(rule, "unknown", left.Display, right.Display, $"operator '{rule.Operator}' not supported for these value types");

            return new RuleEvaluation(rule, result.Value ? "pass" : "fail", left.Display, right.Display, null);
        }

        private static bool? Compare(Resolved left, Resolved right, string? op)
        {
            if (string.IsNullOrEmpty(op)) return null;

            // Numeric wins when both sides have it (e.g. Supertrend > EMA) — text is the fallback
            // (e.g. Supertrend == "GREEN", where the Literal side has no numeric form at all).
            if (left.Numeric.HasValue && right.Numeric.HasValue)
            {
                decimal l = left.Numeric.Value, r = right.Numeric.Value;
                return op switch
                {
                    "<" => l < r,
                    ">" => l > r,
                    "<=" => l <= r,
                    ">=" => l >= r,
                    "==" => l == r,
                    "!=" => l != r,
                    _ => (bool?)null,
                };
            }

            if (left.Text != null && right.Text != null)
            {
                bool equal = string.Equals(left.Text, right.Text, StringComparison.OrdinalIgnoreCase);
                return op switch
                {
                    "==" => equal,
                    "!=" => !equal,
                    _ => (bool?)null,
                };
            }

            return null;
        }

        // AND short-circuits on any fail, OR short-circuits on any pass; otherwise an unknown
        // anywhere in the chain propagates. Correct for this codebase's actual rule chains (each
        // one uses a single link type throughout, never mixed AND/OR needing real precedence).
        private static string AggregateStatus(List<RuleEvaluation> rules)
        {
            string result = rules[0].Status;
            for (int i = 1; i < rules.Count; i++)
            {
                string link = (rules[i - 1].Rule.Link ?? "").Trim().ToUpperInvariant();
                string next = rules[i].Status;
                result = link switch
                {
                    "AND" => Combine(result, next, failDominant: true),
                    "OR" => Combine(result, next, failDominant: false),
                    _ => next,
                };
            }
            return result;

            static string Combine(string a, string b, bool failDominant)
            {
                string dominant = failDominant ? "fail" : "pass";
                string recessive = failDominant ? "pass" : "fail";
                if (a == dominant || b == dominant) return dominant;
                if (a == "unknown" || b == "unknown") return "unknown";
                return recessive;
            }
        }

        private static async Task<Resolved> ResolveAsync(Operand? operand, LiveDataSnapshot live)
        {
            if (operand == null) return Resolved.Unresolved("missing operand");

            return operand.Type switch
            {
                "Literal" => ResolveLiteral(operand.Value),
                "Indicator" => await ResolveIndicatorAsync(operand, live),
                "Expression" => await ResolveExpressionAsync(operand, live),
                _ => Resolved.Unresolved($"unknown operand type '{operand.Type}'"),
            };
        }

        private static Resolved ResolveLiteral(string? value)
        {
            if (value == null) return Resolved.Unresolved("literal has no value");
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                return new Resolved(value, num, null, null);
            return new Resolved(value, null, value, null);
        }

        private static async Task<Resolved> ResolveIndicatorAsync(Operand operand, LiveDataSnapshot live)
        {
            string reference = operand.Value ?? "";
            var props = operand.Properties;
            if (props?.Instrument == null || props.Timeframe == null)
                return Resolved.Unresolved($"{reference}: missing instrument/timeframe on the rule");

            if (string.Equals(props.RelativePosition, "Previous", StringComparison.OrdinalIgnoreCase))
                return Resolved.Unresolved($"{reference}(Previous): no prior-bar snapshot is kept, only the current running state");

            if (string.Equals(reference, "EMA", StringComparison.OrdinalIgnoreCase))
            {
                var hash = await live.GetIndicatorHashAsync(props.Instrument, props.Timeframe, "EMA", props.Period, props.Multiplier);
                if (hash == null || hash.GetValueOrDefault("IsSeeded") != "true")
                    return Resolved.Unresolved($"EMA({props.Period}) on {props.Instrument} {props.Timeframe}: not seeded yet");
                if (!decimal.TryParse(hash.GetValueOrDefault("LastEma"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ema))
                    return Resolved.Unresolved($"EMA({props.Period}): value unreadable");
                return new Resolved(FormatNumber(ema), ema, null, null);
            }

            if (string.Equals(reference, "Supertrend", StringComparison.OrdinalIgnoreCase))
            {
                var hash = await live.GetIndicatorHashAsync(props.Instrument, props.Timeframe, "Supertrend", props.Period, props.Multiplier);
                if (hash == null || hash.GetValueOrDefault("IsSeeded") != "true")
                    return Resolved.Unresolved($"Supertrend({props.Period},{props.Multiplier}) on {props.Instrument} {props.Timeframe}: not seeded yet");

                string direction = hash.GetValueOrDefault("TrendDirection") ?? "";
                // Live Redis state only ever stores "Up"/"Down" (see SupertrendState's own doc
                // comment) — the strategy's rules compare against the literal "GREEN"/"RED", so this
                // is the one place that translation actually happens, applying the standard
                // TradingView convention (uptrend = green, downtrend = red).
                string? color = direction switch { "Up" => "GREEN", "Down" => "RED", _ => null };
                string bandField = direction == "Down" ? "PrevUpperBand" : "PrevLowerBand";
                if (!decimal.TryParse(hash.GetValueOrDefault(bandField), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    return Resolved.Unresolved($"Supertrend({props.Period},{props.Multiplier}): value unreadable");

                string display = color != null ? $"{FormatNumber(value)} ({direction}→{color})" : FormatNumber(value);
                return new Resolved(display, value, color, null);
            }

            if (string.Equals(reference, "Pivot Central Range", StringComparison.OrdinalIgnoreCase))
            {
                var hash = await live.GetIndicatorHashAsync(props.Instrument, props.Timeframe, "Pivot Central Range", 0, 0);
                if (hash == null)
                    return Resolved.Unresolved($"Pivot Central Range on {props.Instrument} {props.Timeframe}: not computed yet");
                if (!decimal.TryParse(hash.GetValueOrDefault("Width"), NumberStyles.Any, CultureInfo.InvariantCulture, out var width))
                    return Resolved.Unresolved("Pivot Central Range: value unreadable");
                return new Resolved(FormatNumber(width), width, null, null);
            }

            return Resolved.Unresolved($"no evaluator for indicator '{reference}' yet");
        }

        // Deliberately narrow: only resolves the one Expression shape the deployed strategy
        // actually needs ("N * Closing Price", RelativePosition Previous), sourced from the
        // PriorClose WarmUpService now persists alongside Pivot Central Range (see
        // WarmUpFunctions.cs — it computed this value already to seed PCR itself, just never kept
        // it before). Everything else an Expression could name (Candle High/Low, Current Profit,
        // Time in Trade, Trading Session State, ...) has no live source yet and stays unresolved —
        // those only appear in the Risk Management / Exit branches anyway, which are never
        // evaluated (see EvaluateEntryExitAsync).
        private static async Task<Resolved> ResolveExpressionAsync(Operand operand, LiveDataSnapshot live)
        {
            string value = operand.Value ?? "";
            var props = operand.Properties;

            var match = MultiplierExpression.Match(value);
            if (match.Success && string.Equals(props?.RelativePosition, "Previous", StringComparison.OrdinalIgnoreCase) && props?.Instrument != null)
            {
                decimal multiplier = decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var priorClose = await live.GetPriorCloseAsync(props.Instrument);
                if (priorClose == null)
                    return Resolved.Unresolved($"Closing Price (Previous) for {props.Instrument}: not available — needs Pivot Central Range to have run at least once");

                decimal result = multiplier * priorClose.Value;
                return new Resolved($"{multiplier} × {FormatNumber(priorClose.Value)} = {FormatNumber(result)}", result, null, null);
            }

            return Resolved.Unresolved($"no data source for '{value}' yet");
        }

        private static string FormatNumber(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
    }
}
