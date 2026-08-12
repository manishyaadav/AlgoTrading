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
        //
        // Kind/Source/AsOf/Fields are the provenance the dashboard's evidence drawer shows: which
        // Redis key was actually read, which bar it belongs to, and the raw hash entries the value
        // was derived from. An unresolved operand still carries Source when we know which key we
        // looked for and simply didn't find (or found unseeded) — "we checked here and it wasn't
        // there" is a materially more useful answer than "no idea".
        // SourceValue/SourceDetail are what the *input* is, as opposed to what this rule does with
        // it. Usually the same, but not always, and the difference is load-bearing: a rule reading
        // "0.0038 × Closing Price (Previous)" displays the computed 93.42, while the input it
        // actually reads is a prior close of 24,583.80. The source card has to show the latter, or
        // it's captioning the wrong number. Both default to Display when there's no distinction.
        private record Resolved(
            string? Display,
            decimal? Numeric,
            string? Text,
            string? UnresolvedReason,
            string Kind = "unresolved",
            string? Source = null,
            string? AsOf = null,
            List<EvidenceField>? Fields = null,
            string? SourceValue = null,
            string? SourceDetail = null)
        {
            public bool IsResolved => UnresolvedReason == null;

            public static Resolved Unresolved(string reason, string? source = null) =>
                new(null, null, null, reason, "unresolved", source);

            public OperandEvidence ToEvidence() =>
                new(Display, Numeric, Kind, Source, AsOf, Fields ?? new List<EvidenceField>());

            public string? CardValue => SourceValue ?? Display;
        }

        // Which live input an operand reads, derived from the rule definition alone — deliberately
        // no Redis in here. This is the single source of truth for source identity: the resolvers
        // use it to name what they just read, and the never-evaluated branches use it to name what
        // they *would* read. Both land on the same card, which is what makes it visible that four
        // exit rules hang off an input nothing feeds.
        private record SourceRef(string NaturalId, string Label, string Scope, string Kind);

        private static readonly Regex MultiplierExpression = new(@"^\s*([\d.]+)\s*\*\s*Closing Price\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static SourceRef? ClassifySource(Operand? operand)
        {
            if (operand == null || operand.Type == "Literal") return null;

            string reference = operand.Value ?? "(empty)";
            var props = operand.Properties;
            string scope = props == null
                ? ""
                : string.Join(" · ", new[] { props.Timeframe, props.Instrument }.Where(s => !string.IsNullOrEmpty(s)));

            static SourceRef Unbacked(string id, string label, string scope) => new($"unbacked:{id}", label, scope, "unbacked");

            if (operand.Type == "Expression")
            {
                // The prior session's close is persisted on the Pivot Central Range hash, but which
                // PCR key holds it is only discoverable at runtime — so this one is identified by
                // instrument, letting the static and live paths agree without either guessing the
                // timeframe segment.
                if (MultiplierExpression.IsMatch(reference)
                    && string.Equals(props?.RelativePosition, "Previous", StringComparison.OrdinalIgnoreCase)
                    && props?.Instrument != null)
                    return new($"priorclose:{props.Instrument}", "Closing Price (prev. session)", props.Instrument, "indicator");

                return Unbacked(reference, reference, scope);
            }

            if (operand.Type != "Indicator") return Unbacked(reference, reference, scope);

            if (props?.Instrument == null || props.Timeframe == null)
                return Unbacked(reference, reference, scope);

            // A "Previous" reference is its own input, not the current one: only the running state
            // is kept, so nothing backs the prior bar even when the current bar is fully live.
            if (string.Equals(props.RelativePosition, "Previous", StringComparison.OrdinalIgnoreCase))
                return Unbacked($"{reference}:previous:{props.Timeframe}:{props.Instrument}", $"{reference} (previous bar)", scope);

            string key = LiveDataSnapshot.IndicatorKey(props.Instrument, props.Timeframe, reference, props.Period, props.Multiplier);

            if (string.Equals(reference, "EMA", StringComparison.OrdinalIgnoreCase))
                return new($"ema:{key}", $"EMA (P{props.Period})", scope, "indicator");

            if (string.Equals(reference, "Supertrend", StringComparison.OrdinalIgnoreCase))
                return new($"supertrend:{key}", $"Supertrend (P{props.Period} ×{props.Multiplier})", scope, "indicator");

            if (string.Equals(reference, "Adaptive Supertrend", StringComparison.OrdinalIgnoreCase))
                return new($"adaptive-supertrend:{key}", $"Adaptive Supertrend (P{props.Period} ×{props.Multiplier})", scope, "indicator");

            if (string.Equals(reference, "Pivot Central Range", StringComparison.OrdinalIgnoreCase))
                return new($"pcr:{key}", "Pivot Central Range", scope, "indicator");

            return Unbacked($"{reference}:{props.Timeframe}:{props.Instrument}", reference, scope);
        }

        // Names every input a rule depends on and returns the ids to link it by. Deduped within the
        // rule, so a rule comparing an input against itself counts as one dependent rather than two.
        private static List<string> TouchSources(TradingRule rule, SourceRegistry registry)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var operand in new[] { rule.LeftOperand, rule.RightOperand })
            {
                var source = ClassifySource(operand);
                if (source == null || !seen.Add(source.NaturalId)) continue;
                ids.Add(registry.Touch(source.NaturalId, source.Label, source.Scope, source.Kind));
            }

            return ids;
        }

        // The session/holiday gate is the same Redis "India" state no matter which strategy is
        // selected — it's not a fact about any one strategy's rule tree, it's a precondition every
        // deployed strategy shares. Evaluated once, standalone (StrategyFunctions.GetSessionStatus),
        // rather than duplicated inside every strategy's own rule-status response the way it used
        // to be. Strategy deployment itself (the old "Gate 1") isn't evaluated at all anymore: the
        // only way to reach BuildAsync is already through a deployed strategy (GetRuleStatus 404s
        // otherwise, and the dashboard's switcher only ever lists deployed ones), so a gate whose
        // answer is always "yes" had nothing left to tell you.
        public static async Task<GateNode> BuildSessionGateAsync(LiveDataSnapshot live)
        {
            string? sessionState = await live.GetSessionStateAsync();
            string sessionStatus = sessionState switch
            {
                null => "unknown",
                "Normal" => "pass",
                _ => "fail", // Holiday / Weekend
            };
            return new GateNode(
                "Session gate · common to every deployed strategy, read from Redis \"India\"", "Is there a session today?",
                sessionStatus,
                sessionState == null
                    ? "Session-state key not found in Redis — country-live/notification-live may not have run yet today."
                    : sessionState == "Normal"
                        ? "Not a holiday, not a weekend — the day is gated open."
                        : $"Today is a {sessionState} — no session, nothing below runs.",
                new List<ValueChip> { new("state", sessionState ?? "—", sessionStatus == "pass" ? "pass" : sessionStatus == "fail" ? "fail" : null) },
                new List<string>());
        }

        public static async Task<RuleStatusResponse> BuildAsync(string strategyId, Strategy.Strategy strategy, LiveDataSnapshot live)
        {
            var ts = strategy.Strategies?.FirstOrDefault();
            var registry = new SourceRegistry();

            var tradingSessionRules = ts == null
                ? new RuleGroup("Trading Session Rules", "unknown", true, new List<RuleEvaluation>())
                : await EvaluateGroupAsync(ts.TradingSessionRules, live, registry, "Trading Session Rules", isLive: true);

            // The one source that exists purely to be unbacked. It's registered rather than left
            // out because a card with a dashed border, no value, and a visible count of the rules
            // depending on it says what a paragraph of caveat text can't: this isn't one missing
            // number, it's the input the entire Exit branch hangs off.
            string positionSourceId = registry.Touch("unbacked:position", "Position / order state", "nothing writes this", "unbacked");
            registry.FillUnresolved("unbacked:position", "no PortfolioService, no Redis key, no topic — nothing tracks this anywhere in the stack", null);

            var positionGate = new GateNode(
                "Gate 2", "In a position?",
                "unknown",
                "No position/order tracking exists in this stack yet — this gate has nothing real to check. The branch below is drawn assuming not in position, since that's the only branch with anything live behind it today.",
                new List<ValueChip>(),
                new List<string> { positionSourceId });

            var longStatus = ts?.LongEntry == null
                ? EmptyEntryExit()
                : await EvaluateEntryExitAsync(ts.LongEntry, live, registry);

            var shortStatus = ts?.ShortEntry == null
                ? EmptyEntryExit()
                : await EvaluateEntryExitAsync(ts.ShortEntry, live, registry);

            return new RuleStatusResponse(
                strategyId,
                strategy.StrategyName ?? strategyId,
                strategy.Exchange,
                ts?.Instruments ?? new List<string>(),
                strategy.DeployedVersion,
                tradingSessionRules,
                positionGate,
                longStatus,
                shortStatus,
                registry.Build());
        }

        private static EntryExitStatus EmptyEntryExit() => new(
            new RuleGroup("Entry Rules", "unknown", true, new List<RuleEvaluation>()),
            new RuleGroup("Risk Management Rules", "unknown", false, new List<RuleEvaluation>()),
            new RuleGroup("Exit / Stop-Loss Rules", "unknown", false, new List<RuleEvaluation>()));

        private static async Task<EntryExitStatus> EvaluateEntryExitAsync(EntryExitRule rules, LiveDataSnapshot live, SourceRegistry registry)
        {
            var entry = await EvaluateGroupAsync(rules.EntryRules, live, registry, "Entry Rules", isLive: true);

            // Risk Management / Update-Stop-Loss / Exit all depend on account or position state
            // that doesn't exist anywhere in this codebase yet (see the Position gate above) — built
            // and shown structurally for reference, deliberately never evaluated (Live: false), so
            // the page never implies a real answer it doesn't have.
            var risk = await EvaluateGroupAsync(rules.RiskManagementRules, live, registry, "Risk Management Rules", isLive: false);

            var exitCombined = new List<TradingRule>();
            exitCombined.AddRange(rules.UpdateStopLossRules ?? new());
            exitCombined.AddRange(rules.ExitRules ?? new());
            var exit = await EvaluateGroupAsync(exitCombined, live, registry, "Exit / Stop-Loss Rules", isLive: false);

            return new EntryExitStatus(entry, risk, exit);
        }

        private static async Task<RuleGroup> EvaluateGroupAsync(List<TradingRule>? rules, LiveDataSnapshot live, SourceRegistry registry, string title, bool isLive)
        {
            var evaluations = new List<RuleEvaluation>();
            if (rules != null)
            {
                foreach (var rule in rules.OrderBy(r => r.Sequence))
                    evaluations.Add(isLive ? await EvaluateRuleAsync(rule, live, registry) : NotEvaluated(rule, registry));
            }

            string status = evaluations.Count == 0 ? "unknown" : (isLive ? AggregateStatus(evaluations) : "unknown");
            return new RuleGroup(title, status, isLive, evaluations);
        }

        // Deliberately resolves nothing at all — not even the operands. These groups depend on
        // position/account state that doesn't exist, so reading Redis for the half of each rule
        // that *is* backed would produce a drawer full of real-looking evidence for a rule that
        // was never evaluated. Empty evidence is the honest shape here.
        //
        // It still names its inputs, though: "this rule reads Supertrend" is a fact about the rule
        // definition, true whether or not anything ever evaluates it, and it's what makes the
        // dependency between a dead branch and a live input visible instead of implied.
        private static RuleEvaluation NotEvaluated(TradingRule rule, SourceRegistry registry) =>
            new(rule, "unknown", OperandEvidence.None(), OperandEvidence.None(),
                "not evaluated — depends on state this stack doesn't track yet",
                TouchSources(rule, registry));

        private static async Task<RuleEvaluation> EvaluateRuleAsync(TradingRule rule, LiveDataSnapshot live, SourceRegistry registry)
        {
            // Named before resolving, so the registry entry exists for the resolvers to fill.
            var sourceIds = TouchSources(rule, registry);

            var left = await ResolveAsync(rule.LeftOperand, live, registry);
            var right = await ResolveAsync(rule.RightOperand, live, registry);

            if (!left.IsResolved || !right.IsResolved)
            {
                string reason = !left.IsResolved && !right.IsResolved
                    ? $"{left.UnresolvedReason}; {right.UnresolvedReason}"
                    : (left.UnresolvedReason ?? right.UnresolvedReason)!;
                return new RuleEvaluation(rule, "unknown", left.ToEvidence(), right.ToEvidence(), reason, sourceIds);
            }

            bool? result = Compare(left, right, rule.Operator);
            if (result == null)
                return new RuleEvaluation(rule, "unknown", left.ToEvidence(), right.ToEvidence(),
                    $"operator '{rule.Operator}' not supported for these value types", sourceIds);

            return new RuleEvaluation(rule, result.Value ? "pass" : "fail", left.ToEvidence(), right.ToEvidence(), null, sourceIds);
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

        private static async Task<Resolved> ResolveAsync(Operand? operand, LiveDataSnapshot live, SourceRegistry registry)
        {
            if (operand == null) return Resolved.Unresolved("missing operand");

            var resolved = operand.Type switch
            {
                "Literal" => ResolveLiteral(operand.Value),
                "Indicator" => await ResolveIndicatorAsync(operand, live),
                "Expression" => await ResolveExpressionAsync(operand, live),
                _ => Resolved.Unresolved($"unknown operand type '{operand.Type}'"),
            };

            // One place where a reading becomes a filled-in source card, rather than every resolver
            // remembering to report itself. Literals classify to null and are skipped — they're
            // part of the rule, not an input the engine reads.
            var source = ClassifySource(operand);
            if (source != null)
            {
                if (resolved.IsResolved && resolved.CardValue != null)
                    registry.Fill(source.NaturalId, resolved.CardValue, resolved.SourceDetail, resolved.Source, resolved.AsOf, resolved.Fields ?? new List<EvidenceField>());
                else
                    registry.FillUnresolved(source.NaturalId, resolved.UnresolvedReason, resolved.Source);
            }

            return resolved;
        }

        // A literal has no source and no as-of — it's part of the rule definition, not live data.
        // The drawer says exactly that rather than leaving the side looking unexplained.
        private static Resolved ResolveLiteral(string? value)
        {
            if (value == null) return Resolved.Unresolved("literal has no value");
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                return new Resolved(value, num, null, null, Kind: "literal");
            return new Resolved(value, null, value, null, Kind: "literal");
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
                string key = LiveDataSnapshot.IndicatorKey(props.Instrument, props.Timeframe, "EMA", props.Period, props.Multiplier);
                var hash = await live.GetIndicatorHashAsync(props.Instrument, props.Timeframe, "EMA", props.Period, props.Multiplier);
                if (hash == null || hash.GetValueOrDefault("IsSeeded") != "true")
                    return Resolved.Unresolved($"EMA({props.Period}) on {props.Instrument} {props.Timeframe}: not seeded yet", key);
                if (!decimal.TryParse(hash.GetValueOrDefault("LastEma"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ema))
                    return Resolved.Unresolved($"EMA({props.Period}): value unreadable", key);

                return new Resolved(FormatNumber(ema), ema, null, null,
                    Kind: "indicator",
                    Source: key,
                    AsOf: hash.GetValueOrDefault("LastBarWindowsStartTime"),
                    Fields: Evidence(hash, "LastEma", "SeedBarsSeenSoFar"),
                    SourceDetail: $"{hash.GetValueOrDefault("SeedBarsSeenSoFar")} bars seeded");
            }

            if (string.Equals(reference, "Supertrend", StringComparison.OrdinalIgnoreCase))
            {
                string key = LiveDataSnapshot.IndicatorKey(props.Instrument, props.Timeframe, "Supertrend", props.Period, props.Multiplier);
                var hash = await live.GetIndicatorHashAsync(props.Instrument, props.Timeframe, "Supertrend", props.Period, props.Multiplier);
                if (hash == null || hash.GetValueOrDefault("IsSeeded") != "true")
                    return Resolved.Unresolved($"Supertrend({props.Period},{props.Multiplier}) on {props.Instrument} {props.Timeframe}: not seeded yet", key);

                string direction = hash.GetValueOrDefault("TrendDirection") ?? "";
                // Live Redis state only ever stores "Up"/"Down" (see SupertrendState's own doc
                // comment) — the strategy's rules compare against the literal "GREEN"/"RED", so this
                // is the one place that translation actually happens, applying the standard
                // TradingView convention (uptrend = green, downtrend = red).
                string? color = direction switch { "Up" => "GREEN", "Down" => "RED", _ => null };
                string bandField = direction == "Down" ? "PrevUpperBand" : "PrevLowerBand";
                if (!decimal.TryParse(hash.GetValueOrDefault(bandField), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    return Resolved.Unresolved($"Supertrend({props.Period},{props.Multiplier}): value unreadable", key);

                string display = color != null ? $"{FormatNumber(value)} ({direction}→{color})" : FormatNumber(value);

                // "band used" is the one synthetic field here, and it earns its place: which of the
                // two stored bands *is* the Supertrend line depends entirely on TrendDirection, and
                // that choice is invisible in the raw hash. Everything else is verbatim Redis.
                var fields = Evidence(hash, "TrendDirection", "PrevUpperBand", "PrevLowerBand", "Atr", "PrevClose");
                fields.Insert(1, new EvidenceField("band used", $"{bandField} (direction is {(direction == "" ? "—" : direction)})"));

                return new Resolved(display, value, color, null,
                    Kind: "indicator",
                    Source: key,
                    AsOf: hash.GetValueOrDefault("LastBarWindowsStartTime"),
                    Fields: fields,
                    // The card shows the band and the trend as separate facts; the rule row shows
                    // them fused because that's how the comparison uses them.
                    SourceValue: FormatNumber(value),
                    SourceDetail: color != null ? $"{direction} → {color}" : direction);
            }

            if (string.Equals(reference, "Adaptive Supertrend", StringComparison.OrdinalIgnoreCase))
            {
                string key = LiveDataSnapshot.IndicatorKey(props.Instrument, props.Timeframe, "Adaptive Supertrend", props.Period, props.Multiplier);
                var hash = await live.GetIndicatorHashAsync(props.Instrument, props.Timeframe, "Adaptive Supertrend", props.Period, props.Multiplier);
                if (hash == null || hash.GetValueOrDefault("IsSeeded") != "true")
                    return Resolved.Unresolved($"Adaptive Supertrend({props.Period},{props.Multiplier}) on {props.Instrument} {props.Timeframe}: not seeded yet", key);

                string direction = hash.GetValueOrDefault("TrendDirection") ?? "";
                string? color = direction switch { "Up" => "GREEN", "Down" => "RED", _ => null };
                string bandField = direction == "Down" ? "PrevUpperBand" : "PrevLowerBand";
                if (!decimal.TryParse(hash.GetValueOrDefault(bandField), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    return Resolved.Unresolved($"Adaptive Supertrend({props.Period},{props.Multiplier}): value unreadable", key);

                string display = color != null ? $"{FormatNumber(value)} ({direction}→{color})" : FormatNumber(value);

                // Atr here is the K-Means-ASSIGNED volatility centroid actually driving the bands,
                // same field name regular Supertrend uses (so the rule row's existing ATR-scaled
                // distance-to-flipping bar works for this indicator too, unchanged, on the frontend).
                // RawAtr/VolatilityCluster are included alongside it so the evidence trail also shows
                // what was actually measured before clustering smoothed it, and which regime it was
                // classified into.
                var fields = Evidence(hash, "TrendDirection", "PrevUpperBand", "PrevLowerBand", "Atr", "RawAtr", "VolatilityCluster", "PrevClose");
                fields.Insert(1, new EvidenceField("band used", $"{bandField} (direction is {(direction == "" ? "—" : direction)})"));

                return new Resolved(display, value, color, null,
                    Kind: "indicator",
                    Source: key,
                    AsOf: hash.GetValueOrDefault("LastBarWindowsStartTime"),
                    Fields: fields,
                    SourceValue: FormatNumber(value),
                    SourceDetail: color != null ? $"{direction} → {color}" : direction);
            }

            if (string.Equals(reference, "Pivot Central Range", StringComparison.OrdinalIgnoreCase))
            {
                string key = LiveDataSnapshot.IndicatorKey(props.Instrument, props.Timeframe, "Pivot Central Range", 0, 0);
                var hash = await live.GetIndicatorHashAsync(props.Instrument, props.Timeframe, "Pivot Central Range", 0, 0);
                if (hash == null)
                    return Resolved.Unresolved($"Pivot Central Range on {props.Instrument} {props.Timeframe}: not computed yet", key);
                if (!decimal.TryParse(hash.GetValueOrDefault("Width"), NumberStyles.Any, CultureInfo.InvariantCulture, out var width))
                    return Resolved.Unresolved("Pivot Central Range: value unreadable", key);

                return new Resolved(FormatNumber(width), width, null, null,
                    Kind: "indicator",
                    Source: key,
                    // PCR is a once-per-session computation, not a per-bar one — its as-of is the
                    // session it was computed for, not a bar window.
                    AsOf: hash.GetValueOrDefault("SessionDate"),
                    Fields: Evidence(hash, "Width", "Pivot", "TopCentral", "BottomCentral"),
                    SourceDetail: "width of the central range");
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

                decimal close = priorClose.Value.Value;
                decimal result = multiplier * close;

                return new Resolved($"{multiplier} × {FormatNumber(close)} = {FormatNumber(result)}", result, null, null,
                    Kind: "expression",
                    // PriorClose is persisted on the PCR hash, so that's genuinely where this came
                    // from — citing the PCR key rather than inventing a "Closing Price" one keeps
                    // the drawer pointing at a key that actually exists.
                    Source: priorClose.Value.SourceKey,
                    Fields: new List<EvidenceField>
                    {
                        new("PriorClose", close.ToString(CultureInfo.InvariantCulture)),
                        new("multiplier (from the rule)", multiplier.ToString(CultureInfo.InvariantCulture)),
                    },
                    // The input here is the prior close, NOT the multiplied result — the multiplier
                    // belongs to the rule, not to the data. A card captioned 93.42 would be
                    // labelling a number that exists nowhere in Redis.
                    SourceValue: FormatNumber(close),
                    SourceDetail: "previous session's close");
            }

            return Resolved.Unresolved($"no data source for '{value}' yet");
        }

        // Picks named fields out of a Redis hash in the order given, skipping any that aren't
        // there. Values stay exactly as stored — the drawer's job is to show what's in Redis, so
        // rounding or reformatting here would quietly undermine the one thing it's for.
        private static List<EvidenceField> Evidence(Dictionary<string, string> hash, params string[] names)
        {
            var fields = new List<EvidenceField>();
            foreach (var name in names)
            {
                if (hash.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                    fields.Add(new EvidenceField(name, value));
            }
            return fields;
        }

        private static string FormatNumber(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
    }
}
