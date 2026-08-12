namespace AggregatorFunctions.Indicators
{
    // Mirrors WarmUpService/Indicators/AdaptiveVolatilityClusterer.cs field-for-field/logic-for-logic
    // — no shared project reference exists between these two services (same established pattern as
    // ActiveIndicatorInstance.cs's duplication), so this is a deliberate copy, not drift. See that
    // file for the full derivation/rationale; keep both in sync if the algorithm ever changes.
    public static class AdaptiveVolatilityClusterer
    {
        public const int TrainingWindow = 100;
        private const decimal HighPct = 0.75m, MidPct = 0.50m, LowPct = 0.25m;
        private const int MaxIterations = 50;

        public record Result(
            decimal AssignedCentroid, string Cluster, int ClusterSize,
            decimal HighCentroid, decimal MediumCentroid, decimal LowCentroid, int Iterations);

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
                hv.Clear();
                mv.Clear();
                lv.Clear();
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

            var candidates = new (decimal Dist, decimal Centroid, string Label, int Size)[]
            {
                (Math.Abs(current - high), high, "High", hv.Count),
                (Math.Abs(current - mid), mid, "Medium", mv.Count),
                (Math.Abs(current - low), low, "Low", lv.Count),
            };
            var best = candidates.OrderBy(c => c.Dist).First();

            return new Result(best.Centroid, best.Label, best.Size, high, mid, low, iterations);
        }
    }
}
