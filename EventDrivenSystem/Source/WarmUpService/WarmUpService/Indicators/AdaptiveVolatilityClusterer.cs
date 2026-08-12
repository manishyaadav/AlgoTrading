namespace WarmUpService.Indicators
{
    // Ports the "Adaptive SuperTrend" Pine Script's inline K-Means volatility model (3 clusters:
    // High/Medium/Low) — trains on a trailing window of raw ATR values, then reports which cluster
    // the LATEST value in that window lands in. The Pine source re-runs this full clustering fresh
    // on every single bar (not an incremental/running approximation), so this port does the same:
    // callers pass the whole trailing window each time, not just the newest point.
    //
    // TrainingWindow/HighPct/MidPct/LowPct mirror the source's own KM_TRAIN_LEN/KM_HIGH_PCT/
    // KM_MID_PCT/KM_LOW_PCT — fixed constants, not configurable. The Pine source itself comments
    // them "DO NOT EXPOSE AS INPUTS", and this keeps that boundary.
    public static class AdaptiveVolatilityClusterer
    {
        public const int TrainingWindow = 100;
        private const decimal HighPct = 0.75m, MidPct = 0.50m, LowPct = 0.25m;

        // The Pine source's `while` loop has no iteration cap and terminates on exact float equality
        // between successive centroid sets — real market data converges in a handful of iterations,
        // so this cap only guards against a pathological non-converging case that decimal's extra
        // precision (vs. Pine's float) could in theory produce.
        private const int MaxIterations = 50;

        public record Result(
            decimal AssignedCentroid, string Cluster, int ClusterSize,
            decimal HighCentroid, decimal MediumCentroid, decimal LowCentroid, int Iterations);

        // `window`: the trailing raw ATR values used to train the 3 centroids — must include
        // `current` as one of its elements (the Pine source always classifies a bar's own ATR
        // against centroids trained on a window that includes that same value). Order doesn't
        // matter for clustering.
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
                    // Three independent checks, not if/else-if — matches the Pine source exactly,
                    // including its tie-breaking: a point exactly equidistant from two or more
                    // centroids joins none of them that round, rather than arbitrarily picking one.
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

            // Same order the Pine source pushes distances in (high, medium, low) — ties broken by
            // whichever comes first in that order, matching array.indexof(array.min())'s behavior.
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
