using System.Globalization;
using System.Text.RegularExpressions;
using StrategyService.Strategy;

namespace StrategyService.Backtest
{
    // Walks a strategy's rule tree bar-by-bar over real historical data and simulates what it would
    // have done — read-only, same "visualization of what the rules would decide" spirit
    // Engine/RuleEvaluator.cs already has for live data, just replayed against history instead of
    // Redis's current state, with an actual simulated position (something the live page deliberately
    // never has — see RuleEvaluator.cs's Position gate comment) since a backtest's whole point is to
    // answer "would this have entered and exited, and when".
    //
    // Two honest simplifications, both because of what this schema does — and doesn't — actually
    // define, not laziness:
    //
    // 1. Risk Management rules never gate entry. They're evaluated and carried in each trade's audit
    //    trail, but don't block a signal — the exact same reason RuleEvaluator.cs never live-evaluates
    //    them either (see its own comment): they reference account/capital state
    //    ("Allocated Capital", "Risk in Trade") this schema has no defined numeric source for, live
    //    or historical. Requiring them to resolve+pass would mean every real deployed strategy
    //    backtests to zero trades, for a reason that has nothing to do with whether the strategy is
    //    actually any good.
    // 2. P&L is in raw price points, not currency. There's no lot size, position sizing, or capital
    //    model anywhere in the strategy schema (Risk per Trade / Allocated Capital are referenced as
    //    opaque Literals, not resolvable inputs) — inventing one to produce a rupee figure would be
    //    exactly the kind of guess this whole codebase's evidence-first design deliberately avoids
    //    everywhere else.
    //
    // ⚠️ Not cross-checked against a reference backtesting engine with known-good results — same
    // caveat every indicator seeder in this codebase already carries, now extended to the simulation
    // loop built on top of them.
    public static class BacktestEngine
    {
        private record SeriesPoint(decimal? Numeric, string? Text);

        public static async Task<BacktestResponse> RunAsync(Strategy.Strategy strategy, DateTime startDate, DateTime endDate, BacktestOhlcClient ohlc)
        {
            var ts = strategy.Strategies?.FirstOrDefault();
            if (ts == null)
                return Error("no-entry-rules", "This strategy has no trading rules defined yet — nothing to backtest.", startDate, endDate);

            bool hasLongEntry = ts.LongEntry?.EntryRules?.Count > 0;
            bool hasShortEntry = ts.ShortEntry?.EntryRules?.Count > 0;
            if (!hasLongEntry && !hasShortEntry)
                return Error("no-entry-rules", "This strategy has no Long or Short entry rules — nothing to simulate entering on.", startDate, endDate);

            var refs = CollectReferences(ts);
            string? instrument = refs.Select(r => r.Instrument).FirstOrDefault(i => i != null);
            if (instrument == null)
                return Error("no-instrument", "None of this strategy's rules reference an instrument yet — nothing to fetch historical data for.", startDate, endDate);

            string exchange = (strategy.Exchange ?? "NSE").ToLowerInvariant();

            var sufficiency = await ohlc.CheckRangeSufficiencyAsync(exchange, instrument, startDate, endDate);
            if (sufficiency == null)
                return Error("error", "Could not reach the historical data service (ohlc-live) to check data availability. Try again shortly.", startDate, endDate, instrument, exchange);

            if (!sufficiency.Sufficient)
                return new BacktestResponse("insufficient-data",
                    $"{sufficiency.DaysMissing} of {sufficiency.DaysChecked} trading day(s) in this period have no historical data in Azurite yet — pick a different range, or warm up that history first.",
                    instrument, exchange, null, startDate, endDate, null, null, sufficiency);

            var rawBars = await ohlc.FetchRangeAsync(exchange, instrument, startDate, endDate);
            if (rawBars == null || rawBars.Count == 0)
                // HistoricalSufficiency only confirms the monthly blob file EXISTS, not that it has
                // real rows for this specific range (see its own doc comment) — an existing-but-
                // empty/irrelevant blob reports "sufficient" and then genuinely returns nothing here.
                // A different range is the only thing that actually helps; a bare retry won't.
                return Error("error", "Historical data was reported available but the actual fetch came back empty — the underlying file may not have real rows for this range. Try a different date range.", startDate, endDate, instrument, exchange);

            // The finest timeframe any Entry/Exit/Risk/UpdateStopLoss rule references is the
            // simulation's own "tick" — coarser indicators (e.g. a 5-min Supertrend) just hold their
            // last-closed value across every finer tick in between, exactly like the live pipeline
            // only updates a 5-min indicator once every 5 minutes. TradingSessionRules (e.g. Pivot
            // Central Range, always "1 Day") are deliberately excluded from this — that's a once-a-
            // day gate, not a tick resolution; see its own handling below.
            var tickReferences = refs.Where(r => r.Group != RuleGroupKind.Session).ToList();
            int? finestMinutes = tickReferences.Select(r => r.TimeframeMinutes).Where(m => m.HasValue).Select(m => m!.Value).DefaultIfEmpty().Min();
            if (finestMinutes is null or <= 0)
                return Error("error", "This strategy's entry/exit rules don't reference a resolvable timeframe (e.g. \"5 Minutes\") — nothing to tick the simulation on.", startDate, endDate, instrument, exchange);

            var barsByTimeframe = new Dictionary<int, List<HistoricalBar>>();
            List<HistoricalBar> BarsFor(int minutes) =>
                barsByTimeframe.TryGetValue(minutes, out var cached) ? cached : barsByTimeframe[minutes] = TimeframeBuilder.Build(rawBars, minutes);

            var finestBars = BarsFor(finestMinutes.Value);
            if (finestBars.Count == 0)
                return Error("error", "No bars could be built from the fetched historical data for this period.", startDate, endDate, instrument, exchange);

            var dayBars = BarsFor(375); // one "1 Day" bar per trading day — Pivot Central Range's own unit, same as WarmUpService
            var pcrByDay = PivotCentralRangeSeries.ComputeByDay(dayBars);

            // One coarse-to-finest index map per distinct timeframe referenced — built once, reused
            // for every indicator/price lookup at that timeframe. O(n) two-pointer walk, not a
            // binary search per bar per rule.
            var coarseIndexMaps = new Dictionary<int, int[]>();
            int[] MapFor(int coarseMinutes) =>
                coarseIndexMaps.TryGetValue(coarseMinutes, out var cached) ? cached : coarseIndexMaps[coarseMinutes] = MapFinestToCoarse(finestBars, finestMinutes.Value, BarsFor(coarseMinutes), coarseMinutes);

            // Every Indicator reference (EMA/Supertrend/Adaptive Supertrend), normalized into one
            // shape (numeric value + GREEN/RED text where applicable) so the resolver below doesn't
            // need to know which indicator it's reading — same translation RuleEvaluator.cs does
            // live, done once here per series instead of per rule evaluation.
            var seriesByKey = new Dictionary<string, SeriesPoint?[]>();
            foreach (var r in tickReferences.Where(r => r.Kind == "Indicator" && r.TimeframeMinutes.HasValue))
            {
                string key = SeriesKey(r);
                if (seriesByKey.ContainsKey(key)) continue;
                var bars = BarsFor(r.TimeframeMinutes!.Value);
                seriesByKey[key] = BuildIndicatorSeries(r.Reference, bars, r.Period, r.Multiplier);
            }

            var registry = new RuleAudit();

            (decimal? Numeric, string? Text) Resolve(Operand? operand, int finestIdx)
            {
                if (operand == null) return (null, null);

                if (operand.Type == "Literal")
                {
                    if (decimal.TryParse(operand.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)) return (num, null);
                    return (null, operand.Value);
                }

                var props = operand.Properties;

                if (operand.Type == "Indicator")
                {
                    string? reference = operand.Value;
                    if (reference == null || props?.Timeframe == null) return (null, null);

                    if (string.Equals(reference, "Pivot Central Range", StringComparison.OrdinalIgnoreCase))
                    {
                        var date = finestBars[finestIdx].WindowsStartTime.Date;
                        return pcrByDay.TryGetValue(date, out var pcr) ? (pcr.Width, (string?)null) : (null, null);
                    }

                    int? tfMinutes = ParseTimeframeMinutes(props.Timeframe);
                    if (tfMinutes == null || !seriesByKey.TryGetValue($"{reference}:{props.Period}:{props.Multiplier}:{props.Timeframe}", out var series))
                        return (null, null);

                    var map = MapFor(tfMinutes.Value);
                    int coarseIdx = map[finestIdx];
                    bool wantPrevious = string.Equals(props.RelativePosition, "Previous", StringComparison.OrdinalIgnoreCase);
                    int lookupIdx = wantPrevious ? coarseIdx - 1 : coarseIdx;
                    if (lookupIdx < 0 || lookupIdx >= series.Length || series[lookupIdx] == null) return (null, null);

                    return (series[lookupIdx]!.Numeric, series[lookupIdx]!.Text);
                }

                if (operand.Type == "Expression")
                {
                    string value = operand.Value ?? "";
                    var multiplierMatch = MultiplierExpression.Match(value);

                    if (multiplierMatch.Success && string.Equals(props?.RelativePosition, "Previous", StringComparison.OrdinalIgnoreCase))
                    {
                        var date = finestBars[finestIdx].WindowsStartTime.Date;
                        if (!pcrByDay.TryGetValue(date, out var pcr)) return (null, null);
                        decimal mult = decimal.Parse(multiplierMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                        return (mult * pcr.PriorClose, null);
                    }

                    if (props?.Timeframe != null)
                    {
                        int? tfMinutes = ParseTimeframeMinutes(props.Timeframe);
                        if (tfMinutes != null)
                        {
                            var bars = BarsFor(tfMinutes.Value);
                            var map = MapFor(tfMinutes.Value);
                            int coarseIdx = map[finestIdx];
                            bool wantPrevious = string.Equals(props.RelativePosition, "Previous", StringComparison.OrdinalIgnoreCase);
                            int lookupIdx = wantPrevious ? coarseIdx - 1 : coarseIdx;
                            if (lookupIdx < 0 || lookupIdx >= bars.Count) return (null, null);
                            var bar = bars[lookupIdx];

                            return value switch
                            {
                                "Candle High" => (bar.High, null),
                                "Candle Low" => (bar.Low, null),
                                "Closing Price" => (bar.Close, null),
                                "Opening Price" => (bar.Open, null),
                                _ => (null, null),
                            };
                        }
                    }

                    // Everything else (Current Profit, Risk in Trade, Time in Trade, Trading Session
                    // State, ...) references account/position state this schema has no source for —
                    // same honest "unresolved" the live page gives these.
                    return (null, null);
                }

                return (null, null);
            }

            bool? Compare((decimal? Numeric, string? Text) left, (decimal? Numeric, string? Text) right, string? op)
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

            // An unresolved condition collapses to "didn't trigger" (false), never blocks the chain
            // as some third state — a signal this stack can't verify shouldn't cause a simulated
            // entry, and shouldn't force a simulated exit either. Same AND-fails-dominant/
            // OR-passes-dominant folding RuleEvaluator.AggregateStatus uses live, just boolean
            // instead of tri-state since a backtest has to make an actual yes/no decision every bar.
            (bool Result, TradingRule? FiredRule) EvaluateChain(List<TradingRule>? rules, int finestIdx, bool failDominant)
            {
                if (rules == null || rules.Count == 0) return (false, null);

                bool result = false;
                TradingRule? firedRule = null;
                for (int i = 0; i < rules.Count; i++)
                {
                    bool thisPass = Compare(Resolve(rules[i].LeftOperand, finestIdx), Resolve(rules[i].RightOperand, finestIdx), rules[i].Operator) == true;
                    if (thisPass && firedRule == null) firedRule = rules[i];

                    if (i == 0) { result = thisPass; continue; }
                    string link = (rules[i - 1].Link ?? "").Trim().ToUpperInvariant();
                    result = link switch
                    {
                        "AND" => failDominant ? (result && thisPass) : (result || thisPass),
                        "OR" => failDominant ? (result && thisPass) : (result || thisPass),
                        _ => thisPass,
                    };
                }
                return (result, firedRule);
            }

            var trades = new List<BacktestTrade>();
            string? positionSide = null;
            decimal entryPrice = 0;
            DateTime entryTime = default;

            for (int i = 0; i < finestBars.Count; i++)
            {
                var bar = finestBars[i];

                // The session gate: skip new entries entirely on a day whose Trading Session Rules
                // don't pass (mirrors what a real execution engine would do — the live Rule Engine
                // page doesn't cascade this into its display, but a backtest has to actually decide,
                // not just show a status; see RuleEvaluator.cs's own "no cascading dim" note for why
                // the two pages differ here on purpose).
                bool sessionOk = ts.TradingSessionRules == null || ts.TradingSessionRules.Count == 0
                    || EvaluateChain(ts.TradingSessionRules, i, failDominant: true).Result;

                if (positionSide == null)
                {
                    if (!sessionOk) continue;

                    if (hasLongEntry && EvaluateChain(ts.LongEntry!.EntryRules, i, failDominant: true).Result)
                    {
                        positionSide = "Long"; entryPrice = bar.Close; entryTime = bar.WindowsStartTime;
                    }
                    else if (hasShortEntry && EvaluateChain(ts.ShortEntry!.EntryRules, i, failDominant: true).Result)
                    {
                        positionSide = "Short"; entryPrice = bar.Close; entryTime = bar.WindowsStartTime;
                    }
                    continue;
                }

                var activeSide = positionSide == "Long" ? ts.LongEntry : ts.ShortEntry;
                var exit = EvaluateChain(activeSide?.ExitRules, i, failDominant: false);
                if (exit.Result)
                {
                    decimal exitPrice = bar.Close;
                    decimal pnl = positionSide == "Long" ? exitPrice - entryPrice : entryPrice - exitPrice;
                    trades.Add(new BacktestTrade(positionSide, entryTime, entryPrice, bar.WindowsStartTime, exitPrice, pnl,
                        exit.FiredRule != null ? DescribeRule(exit.FiredRule) : "Exit condition met", false));
                    positionSide = null;
                }
            }

            // Still in a position when the data runs out — force-close at the last bar rather than
            // silently dropping an open trade from the stats (that would understate exposure and,
            // worse, could make an actually-losing open position invisible to the numbers).
            if (positionSide != null)
            {
                var lastBar = finestBars[^1];
                decimal exitPrice = lastBar.Close;
                decimal pnl = positionSide == "Long" ? exitPrice - entryPrice : entryPrice - exitPrice;
                trades.Add(new BacktestTrade(positionSide, entryTime, entryPrice, lastBar.WindowsStartTime, exitPrice, pnl,
                    "Period end (forced close)", true));
            }

            var stats = ComputeStats(trades);
            string timeframeLabel = finestMinutes.Value % 375 == 0 ? $"{finestMinutes.Value / 375} Day" : $"{finestMinutes.Value} Minutes";

            return new BacktestResponse("completed",
                trades.Count == 0 ? "Ran successfully — the strategy's entry conditions never triggered in this period." : $"Simulated {trades.Count} trade(s).",
                instrument, exchange, timeframeLabel, startDate, endDate, trades, stats, null);
        }

        private static readonly Regex MultiplierExpression = new(@"^\s*([\d.]+)\s*\*\s*Closing Price\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static BacktestResponse Error(string status, string message, DateTime start, DateTime end, string? instrument = null, string? exchange = null) =>
            new(status, message, instrument, exchange, null, start, end, null, null, null);

        private enum RuleGroupKind { Session, Entry, Exit, Risk }

        private record IndicatorRef(string Kind, string? Reference, string? Instrument, string? Timeframe, int? TimeframeMinutes, int Period, int Multiplier, RuleGroupKind Group);

        private static string SeriesKey(IndicatorRef r) => $"{r.Reference}:{r.Period}:{r.Multiplier}:{r.Timeframe}";

        // Walks every operand across TradingSessionRules + both sides' Entry/Risk/UpdateStopLoss/Exit
        // rules — same tree shape StrategyMaker.AllRulesOf/CollectDataRequirement already walk for
        // the warm-up manifest, duplicated here (not shared — this needs the RuleGroupKind tag those
        // don't) rather than bent to serve two different callers.
        private static List<IndicatorRef> CollectReferences(TradingStrategy ts)
        {
            var result = new List<IndicatorRef>();

            void CollectFrom(Operand? operand, RuleGroupKind group)
            {
                if (operand?.Properties?.Instrument == null) return;
                var props = operand.Properties;
                result.Add(new IndicatorRef(operand.Type ?? "Unknown", operand.Value, props.Instrument, props.Timeframe,
                    props.Timeframe != null ? ParseTimeframeMinutes(props.Timeframe) : null, props.Period, props.Multiplier, group));
            }

            void CollectRules(List<TradingRule>? rules, RuleGroupKind group)
            {
                if (rules == null) return;
                foreach (var rule in rules)
                {
                    CollectFrom(rule.LeftOperand, group);
                    CollectFrom(rule.RightOperand, group);
                }
            }

            CollectRules(ts.TradingSessionRules, RuleGroupKind.Session);
            foreach (var side in new[] { ts.LongEntry, ts.ShortEntry })
            {
                if (side == null) continue;
                CollectRules(side.EntryRules, RuleGroupKind.Entry);
                CollectRules(side.RiskManagementRules, RuleGroupKind.Risk);
                CollectRules(side.UpdateStopLossRules, RuleGroupKind.Exit);
                CollectRules(side.ExitRules, RuleGroupKind.Exit);
            }

            return result;
        }

        private static SeriesPoint?[] BuildIndicatorSeries(string? reference, List<HistoricalBar> bars, int period, int multiplier)
        {
            if (string.Equals(reference, "EMA", StringComparison.OrdinalIgnoreCase))
                return EmaSeries.Compute(bars, period).Select(v => v.HasValue ? new SeriesPoint(v, null) : null).ToArray();

            if (string.Equals(reference, "Supertrend", StringComparison.OrdinalIgnoreCase))
                return SupertrendSeries.Compute(bars, period, multiplier)
                    .Select(v => v.HasValue ? new SeriesPoint(v.Value.Value, DirectionToColor(v.Value.Direction)) : null).ToArray();

            if (string.Equals(reference, "Adaptive Supertrend", StringComparison.OrdinalIgnoreCase))
                return AdaptiveSupertrendSeries.Compute(bars, period, multiplier)
                    .Select(v => v.HasValue ? new SeriesPoint(v.Value.Value, DirectionToColor(v.Value.Direction)) : null).ToArray();

            return new SeriesPoint?[bars.Count];
        }

        private static string? DirectionToColor(string direction) => direction switch { "Up" => "GREEN", "Down" => "RED", _ => null };

        // Two-pointer walk: for each finest bar, the most recently CLOSED coarse bar as of that
        // finest bar's own close time — a coarse indicator only reflects a bar once it's actually
        // closed, same as the live pipeline only updates a 5-min value once every 5 minutes, not on
        // every 1-min tick in between. -1 means no coarse bar has closed yet.
        private static int[] MapFinestToCoarse(List<HistoricalBar> finestBars, int finestMinutes, List<HistoricalBar> coarseBars, int coarseMinutes)
        {
            var map = new int[finestBars.Count];
            int coarseIdx = -1, nextCoarseIdx = 0;
            for (int i = 0; i < finestBars.Count; i++)
            {
                var finestCloseTime = finestBars[i].WindowsStartTime.AddMinutes(finestMinutes);
                while (nextCoarseIdx < coarseBars.Count && coarseBars[nextCoarseIdx].WindowsStartTime.AddMinutes(coarseMinutes) <= finestCloseTime)
                {
                    coarseIdx = nextCoarseIdx;
                    nextCoarseIdx++;
                }
                map[i] = coarseIdx;
            }
            return map;
        }

        // "5 Minutes" -> 5, "1 Day" -> 375. Mirrors StrategyMaker.ParseTimeframeToMinutes exactly
        // (duplicated — see that method's own doc comment on the convention).
        private static int? ParseTimeframeMinutes(string timeframe)
        {
            var match = Regex.Match(timeframe.Trim(), @"^(\d+)\s*(Minute|Day)s?$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            int count = int.Parse(match.Groups[1].Value);
            return string.Equals(match.Groups[2].Value, "Day", StringComparison.OrdinalIgnoreCase) ? count * 375 : count;
        }

        private static string DescribeOperand(Operand? o)
        {
            if (o == null) return "—";
            string s = o.Value ?? "(empty)";
            if (o.Type != "Literal" && o.Properties != null)
            {
                var bits = new List<string>();
                if (o.Properties.Period != 0) bits.Add($"P{o.Properties.Period}");
                if (o.Properties.Multiplier != 0) bits.Add($"x{o.Properties.Multiplier}");
                if (o.Properties.Timeframe != null) bits.Add(o.Properties.Timeframe);
                if (bits.Count > 0) s += $" ({string.Join(", ", bits)})";
            }
            return s;
        }

        private static string DescribeRule(TradingRule r) => $"{DescribeOperand(r.LeftOperand)} {r.Operator} {DescribeOperand(r.RightOperand)}";

        // Not carried in a helper class — this only ever needs to exist long enough to build the
        // final response's Trades list, which already carries everything each trade needs (side,
        // prices, times, exit reason). Reserved for a future "why didn't this bar enter" trace if
        // that's ever worth the payload size; unused today.
        private class RuleAudit { }

        private static BacktestStats ComputeStats(List<BacktestTrade> trades)
        {
            if (trades.Count == 0)
                return new BacktestStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            var wins = trades.Where(t => t.PointsPnl > 0).ToList();
            var losses = trades.Where(t => t.PointsPnl < 0).ToList();
            decimal totalPoints = trades.Sum(t => t.PointsPnl);
            decimal sumWins = wins.Sum(t => t.PointsPnl);
            decimal sumLosses = losses.Sum(t => t.PointsPnl); // negative

            decimal profitFactor = sumLosses != 0 ? sumWins / Math.Abs(sumLosses) : (sumWins > 0 ? decimal.MaxValue : 0);

            // Running equity curve in points, peak-to-trough — the standard drawdown definition,
            // computed over the trade sequence (not intraday/mark-to-market, since this only ever
            // knows prices at entry/exit).
            decimal equity = 0, peak = 0, maxDrawdown = 0;
            int winStreak = 0, lossStreak = 0, longestWinStreak = 0, longestLossStreak = 0;
            foreach (var t in trades)
            {
                equity += t.PointsPnl;
                if (equity > peak) peak = equity;
                maxDrawdown = Math.Max(maxDrawdown, peak - equity);

                if (t.PointsPnl > 0) { winStreak++; lossStreak = 0; } else if (t.PointsPnl < 0) { lossStreak++; winStreak = 0; } else { winStreak = 0; lossStreak = 0; }
                longestWinStreak = Math.Max(longestWinStreak, winStreak);
                longestLossStreak = Math.Max(longestLossStreak, lossStreak);
            }

            return new BacktestStats(
                TotalTrades: trades.Count,
                Wins: wins.Count,
                Losses: losses.Count,
                WinRatePct: Math.Round(100m * wins.Count / trades.Count, 1),
                TotalPoints: Math.Round(totalPoints, 2),
                AveragePointsPerTrade: Math.Round(totalPoints / trades.Count, 2),
                AverageWinPoints: wins.Count > 0 ? Math.Round(sumWins / wins.Count, 2) : 0,
                AverageLossPoints: losses.Count > 0 ? Math.Round(sumLosses / losses.Count, 2) : 0,
                LargestWinPoints: wins.Count > 0 ? wins.Max(t => t.PointsPnl) : 0,
                LargestLossPoints: losses.Count > 0 ? losses.Min(t => t.PointsPnl) : 0,
                ProfitFactor: profitFactor == decimal.MaxValue ? profitFactor : Math.Round(profitFactor, 2),
                MaxDrawdownPoints: Math.Round(maxDrawdown, 2),
                LongestWinStreak: longestWinStreak,
                LongestLossStreak: longestLossStreak);
        }
    }
}
