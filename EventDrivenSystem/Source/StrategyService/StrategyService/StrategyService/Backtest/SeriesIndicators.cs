namespace StrategyService.Backtest
{
    // Full per-bar series for every indicator this stack computes — the backtest engine needs "what
    // was this indicator's value at bar i" for every bar across the whole run, not just the current
    // running state WarmUpService/AggregationService maintain for live evaluation. Same formulas as
    // WarmUpService/Indicators/*Seeder.cs (duplicated, not shared — no project reference exists
    // between WarmUpService and StrategyService, same established convention as BacktestOhlc.cs);
    // each Compute() here is that seeder's exact walk-forward loop, just capturing every bar's
    // result into an array instead of discarding all but the last.
    //
    // Array index i is null until the indicator has enough history to produce a value there — a
    // caller reading result[i] for i before that point gets an honest "nothing yet", not a
    // zero-filled guess.

    public static class EmaSeries
    {
        public static decimal?[] Compute(List<HistoricalBar> bars, int period)
        {
            var result = new decimal?[bars.Count];
            if (period <= 0 || bars.Count < period) return result;

            decimal ema = bars.Take(period).Average(b => b.Close);
            result[period - 1] = ema;

            decimal multiplier = 2m / (period + 1);
            for (int i = period; i < bars.Count; i++)
            {
                ema = (bars[i].Close - ema) * multiplier + ema;
                result[i] = ema;
            }
            return result;
        }
    }

    public static class SupertrendSeries
    {
        public static (string Direction, decimal Value)?[] Compute(List<HistoricalBar> bars, int period, decimal multiplier)
        {
            var result = new (string Direction, decimal Value)?[bars.Count];
            if (period <= 0 || bars.Count < period + 1) return result;

            var trueRange = new decimal[bars.Count];
            for (int i = 1; i < bars.Count; i++)
            {
                var prevClose = bars[i - 1].Close;
                trueRange[i] = Math.Max(bars[i].High - bars[i].Low,
                    Math.Max(Math.Abs(bars[i].High - prevClose), Math.Abs(bars[i].Low - prevClose)));
            }

            decimal Atr(int i) => Enumerable.Range(i - period + 1, period).Select(idx => trueRange[idx]).Average();

            decimal finalUpper = 0, finalLower = 0;
            bool trackingUpper = true;
            string direction = "Up";

            for (int i = period; i < bars.Count; i++)
            {
                decimal atr = Atr(i);
                decimal mid = (bars[i].High + bars[i].Low) / 2m;
                decimal basicUpper = mid + multiplier * atr;
                decimal basicLower = mid - multiplier * atr;

                if (i == period)
                {
                    finalUpper = basicUpper;
                    finalLower = basicLower;
                    trackingUpper = bars[i].Close <= finalUpper;
                    direction = trackingUpper ? "Down" : "Up";
                    result[i] = (direction, trackingUpper ? finalUpper : finalLower);
                    continue;
                }

                var prevClose = bars[i - 1].Close;
                finalUpper = (basicUpper < finalUpper || prevClose > finalUpper) ? basicUpper : finalUpper;
                finalLower = (basicLower > finalLower || prevClose < finalLower) ? basicLower : finalLower;

                if (trackingUpper && bars[i].Close <= finalUpper) trackingUpper = true;
                else if (trackingUpper && bars[i].Close > finalUpper) trackingUpper = false;
                else if (!trackingUpper && bars[i].Close >= finalLower) trackingUpper = false;
                else trackingUpper = true;

                direction = trackingUpper ? "Down" : "Up";
                result[i] = (direction, trackingUpper ? finalUpper : finalLower);
            }
            return result;
        }
    }

    // Mirrors WarmUpService/Indicators/AdaptiveVolatilityClusterer.cs and AggregationService's own
    // copy of the same — a third independent duplicate, same reasoning as everywhere else this
    // constant/algorithm gets copied rather than shared across services.
    public static class AdaptiveVolatilityClusterer
    {
        public const int TrainingWindow = 100;
        private const decimal HighPct = 0.75m, MidPct = 0.50m, LowPct = 0.25m;
        private const int MaxIterations = 50;

        public record Result(decimal AssignedCentroid, string Cluster);

        public static Result Assign(IReadOnlyList<decimal> window, decimal current)
        {
            decimal lo = window.Min(), hi = window.Max();
            decimal high = lo + (hi - lo) * HighPct;
            decimal mid = lo + (hi - lo) * MidPct;
            decimal low = lo + (hi - lo) * LowPct;

            var hv = new List<decimal>();
            var mv = new List<decimal>();
            var lv = new List<decimal>();
            decimal prevHigh = decimal.MinValue, prevMid = decimal.MinValue, prevLow = decimal.MinValue;
            int iterations = 0;

            while ((high != prevHigh || mid != prevMid || low != prevLow) && iterations < MaxIterations)
            {
                hv.Clear(); mv.Clear(); lv.Clear();
                foreach (var v in window)
                {
                    decimal d1 = Math.Abs(v - high), d2 = Math.Abs(v - mid), d3 = Math.Abs(v - low);
                    if (d1 < d2 && d1 < d3) hv.Add(v);
                    if (d2 < d1 && d2 < d3) mv.Add(v);
                    if (d3 < d1 && d3 < d2) lv.Add(v);
                }
                prevHigh = high; prevMid = mid; prevLow = low;
                if (hv.Count > 0) high = hv.Average();
                if (mv.Count > 0) mid = mv.Average();
                if (lv.Count > 0) low = lv.Average();
                iterations++;
            }

            var candidates = new (decimal Dist, decimal Centroid, string Label)[]
            {
                (Math.Abs(current - high), high, "High"),
                (Math.Abs(current - mid), mid, "Medium"),
                (Math.Abs(current - low), low, "Low"),
            };
            var best = candidates.OrderBy(c => c.Dist).First();
            return new Result(best.Centroid, best.Label);
        }
    }

    public static class AdaptiveSupertrendSeries
    {
        public static (string Direction, decimal Value)?[] Compute(List<HistoricalBar> bars, int atrLength, decimal factor)
        {
            var result = new (string Direction, decimal Value)?[bars.Count];
            int minBarsNeeded = atrLength + AdaptiveVolatilityClusterer.TrainingWindow;
            if (atrLength <= 0 || bars.Count < minBarsNeeded) return result;

            var trueRange = new decimal[bars.Count];
            for (int i = 1; i < bars.Count; i++)
            {
                var prevClose = bars[i - 1].Close;
                trueRange[i] = Math.Max(bars[i].High - bars[i].Low,
                    Math.Max(Math.Abs(bars[i].High - prevClose), Math.Abs(bars[i].Low - prevClose)));
            }

            var atr = new decimal[bars.Count];
            atr[atrLength] = Enumerable.Range(1, atrLength).Select(i => trueRange[i]).Average();
            for (int i = atrLength + 1; i < bars.Count; i++)
                atr[i] = (atr[i - 1] * (atrLength - 1) + trueRange[i]) / atrLength;

            int firstClusterableBar = atrLength + AdaptiveVolatilityClusterer.TrainingWindow - 1;
            decimal finalUpper = 0, finalLower = 0;
            string direction = "Up";

            for (int i = firstClusterableBar; i < bars.Count; i++)
            {
                var window = Enumerable.Range(i - AdaptiveVolatilityClusterer.TrainingWindow + 1, AdaptiveVolatilityClusterer.TrainingWindow)
                    .Select(idx => atr[idx]).ToList();
                var assignment = AdaptiveVolatilityClusterer.Assign(window, atr[i]);
                decimal centroidAtr = assignment.AssignedCentroid;

                decimal mid = (bars[i].High + bars[i].Low) / 2m;
                decimal basicUpper = mid + factor * centroidAtr;
                decimal basicLower = mid - factor * centroidAtr;

                if (i == firstClusterableBar)
                {
                    finalUpper = basicUpper;
                    finalLower = basicLower;
                    direction = "Down";
                    result[i] = (direction, finalUpper);
                    continue;
                }

                var prevClose = bars[i - 1].Close;
                finalUpper = (basicUpper < finalUpper || prevClose > finalUpper) ? basicUpper : finalUpper;
                finalLower = (basicLower > finalLower || prevClose < finalLower) ? basicLower : finalLower;

                bool trackingUpper = direction == "Down";
                bool nowTrackingUpper =
                    trackingUpper && bars[i].Close <= finalUpper ? true :
                    trackingUpper && bars[i].Close > finalUpper ? false :
                    !trackingUpper && bars[i].Close >= finalLower ? false :
                    true;

                direction = nowTrackingUpper ? "Down" : "Up";
                result[i] = (direction, nowTrackingUpper ? finalUpper : finalLower);
            }
            return result;
        }
    }

    public static class PivotCentralRangeSeries
    {
        public record State(decimal Pivot, decimal TopCentral, decimal BottomCentral, decimal Width, decimal PriorClose);

        // Pivot Central Range is never seeded-then-carried-forward like EMA/Supertrend (see
        // WarmUpService/Indicators/PivotCentralRangeCalculator.cs) — it's recomputed fresh every
        // trading day from the PRIOR day's H/L/C. `dayBars` is one "1 Day" bar per trading day
        // (TimeframeBuilder.Build(raw1MinBars, 375 minutes)); the value that applies THROUGHOUT
        // trading day i is keyed by day i's own date but computed from day i-1's bar — the same
        // "yesterday's close, today's gate" relationship WarmUpFunctions.cs's CheckOneReason uses
        // live. Day 0 has no prior day, so it's never a key in the result.
        public static Dictionary<DateTime, State> ComputeByDay(List<HistoricalBar> dayBars)
        {
            var result = new Dictionary<DateTime, State>();
            for (int i = 1; i < dayBars.Count; i++)
            {
                var prior = dayBars[i - 1];
                decimal pivot = (prior.High + prior.Low + prior.Close) / 3m;
                decimal bottomCentral = (prior.High + prior.Low) / 2m;
                decimal topCentral = 2m * pivot - bottomCentral;
                decimal width = topCentral - bottomCentral;

                result[dayBars[i].WindowsStartTime.Date] = new State(pivot, topCentral, bottomCentral, width, prior.Close);
            }
            return result;
        }
    }
}
