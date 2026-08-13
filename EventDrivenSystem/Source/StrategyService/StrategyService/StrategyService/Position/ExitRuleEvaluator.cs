using System.Text.RegularExpressions;
using StrategyService.Strategy;

namespace StrategyService.Position
{
    // Live evaluation of a deployed strategy's real ExitRules — the one piece of live rule
    // resolution this codebase didn't have before the Alerts feature (RuleEvaluator.cs's own
    // EvaluateEntryExitAsync explicitly evaluates these with isLive:false, since "Current
    // Profit"/"Initial Stop Loss"/"Time in Trade"/"Trading Session State" all depend on position
    // state that didn't exist anywhere until PositionEntryFunction started tracking it). Deliberately
    // does NOT evaluate RiskManagementRules or UpdateStopLossRules — same precedent
    // BacktestEngine.cs already established (RiskManagementRules references "Allocated Capital",
    // a number with zero backing anywhere in this codebase; UpdateStopLossRules is a trailing-SL
    // concept outside what was asked for here).
    public static class ExitRuleEvaluator
    {
        public record LiveExitContext(
            string Side, decimal EntryPrice, decimal InitialStopLoss, DateTime EntryTime, DateTime AsOf,
            decimal Candle1MinClose, decimal Candle1MinLow, decimal Candle1MinHigh,
            decimal? SupertrendValue, string? TradingSessionState);

        public record ExitDecision(bool ShouldExit, string? FiredRuleDescription);

        private static readonly Regex MultipleOfInitialStopLoss =
            new(@"^\s*([\d.]+)\s*\*\s*Initial Stop Loss\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ExitDecision Evaluate(List<TradingRule>? rules, LiveExitContext ctx)
        {
            if (rules == null || rules.Count == 0) return new ExitDecision(false, null);

            bool result = false;
            TradingRule? firedRule = null;

            for (int i = 0; i < rules.Count; i++)
            {
                bool thisPass = Compare(Resolve(rules[i].LeftOperand, ctx), Resolve(rules[i].RightOperand, ctx), rules[i].Operator) == true;
                if (thisPass && firedRule == null) firedRule = rules[i];

                if (i == 0) { result = thisPass; continue; }

                string link = (rules[i - 1].Link ?? "").Trim().ToUpperInvariant();
                result = link switch
                {
                    "AND" => result && thisPass,
                    "OR" => result || thisPass,
                    _ => result || thisPass, // both deployed strategies' ExitRules are entirely OR-linked; default to OR (exit-friendly) for anything unrecognized rather than silently gating an exit shut
                };
            }

            return new ExitDecision(result, firedRule == null ? null : Describe(firedRule));
        }

        private static (decimal? Numeric, string? Text) Resolve(Operand? operand, LiveExitContext ctx)
        {
            if (operand?.Type == null || operand.Value == null) return (null, null);

            switch (operand.Type)
            {
                case "Literal":
                    return decimal.TryParse(operand.Value, out var literalNum) ? (literalNum, null) : (null, operand.Value);

                case "Indicator":
                    // Both deployed strategies' ExitRules only ever reference their own strategy's
                    // Supertrend instance here — ctx.SupertrendValue is already that instance's
                    // freshly-read current value, resolved by PositionExitFunction before calling in.
                    if (string.Equals(operand.Value, "Supertrend", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(operand.Value, "Adaptive Supertrend", StringComparison.OrdinalIgnoreCase))
                        return (ctx.SupertrendValue, null);
                    return (null, null);

                case "Expression":
                    if (string.Equals(operand.Value, "Current Profit", StringComparison.OrdinalIgnoreCase))
                    {
                        decimal profit = ctx.Side == "Long" ? ctx.Candle1MinClose - ctx.EntryPrice : ctx.EntryPrice - ctx.Candle1MinClose;
                        return (profit, null);
                    }
                    if (string.Equals(operand.Value, "Initial Stop Loss", StringComparison.OrdinalIgnoreCase))
                        return (ctx.InitialStopLoss, null);

                    var multMatch = MultipleOfInitialStopLoss.Match(operand.Value);
                    if (multMatch.Success && decimal.TryParse(multMatch.Groups[1].Value, out var mult))
                        return (mult * ctx.InitialStopLoss, null);

                    if (string.Equals(operand.Value, "Candle Low", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(operand.Properties?.Timeframe, "1 Minute", StringComparison.OrdinalIgnoreCase))
                        return (ctx.Candle1MinLow, null);
                    if (string.Equals(operand.Value, "Candle High", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(operand.Properties?.Timeframe, "1 Minute", StringComparison.OrdinalIgnoreCase))
                        return (ctx.Candle1MinHigh, null);

                    if (string.Equals(operand.Value, "Trading Session State", StringComparison.OrdinalIgnoreCase))
                        return (null, ctx.TradingSessionState);

                    if (string.Equals(operand.Value, "Time in Trade", StringComparison.OrdinalIgnoreCase))
                        return ((decimal)(ctx.AsOf - ctx.EntryTime).TotalMinutes, null);

                    // Everything else this schema's Expression vocabulary allows (Risk in Trade,
                    // Allocated Capital, ...) has no live source here either — same honest
                    // "unresolved" RuleEvaluator/BacktestEngine give these.
                    return (null, null);

                default:
                    return (null, null);
            }
        }

        // Same shape as BacktestEngine.Compare — numeric-vs-numeric or text-vs-text only; a
        // numeric-vs-text mismatch (one side unresolved) never compares true.
        private static bool? Compare((decimal? Numeric, string? Text) left, (decimal? Numeric, string? Text) right, string? op)
        {
            if (string.IsNullOrEmpty(op)) return null;
            if (left.Numeric.HasValue && right.Numeric.HasValue)
            {
                decimal l = left.Numeric.Value, r = right.Numeric.Value;
                return op switch { "<" => l < r, ">" => l > r, "<=" => l <= r, ">=" => l >= r, "==" => l == r, "!=" => l != r, _ => (bool?)null };
            }
            if (left.Text != null && right.Text != null)
            {
                bool equal = string.Equals(left.Text, right.Text, StringComparison.OrdinalIgnoreCase);
                return op switch { "==" => equal, "!=" => !equal, _ => (bool?)null };
            }
            return null;
        }

        private static string Describe(TradingRule rule) =>
            $"{rule.LeftOperand?.Value} {rule.Operator} {rule.RightOperand?.Value}";
    }
}
