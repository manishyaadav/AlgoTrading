using StrategyService.Strategy;

namespace StrategyService.Alerts
{
    // Resolves "which deployed strategy/strategies does this indicator-sourced alert belong to" —
    // reuses StrategyMaker.GetDeployedDataRequirements()'s existing (Instrument, Timeframe) ->
    // References grouping rather than re-walking every deployed strategy's rule tree a second time.
    // Only indicator-sourced alerts (AggregationService's IndicatorAlertRecord) need this;
    // position-sourced alerts (StrategyService's own PositionAlertRecord) already carry their
    // StrategyId/StrategyName at write time.
    public static class AlertAttributionResolver
    {
        public static (List<string> Ids, List<string> Names) Resolve(
            string instrument, string timeframe, string reference, int period, int multiplier,
            List<DataRequirement> requirements, Dictionary<string, string> namesById)
        {
            var ids = new List<string>();

            foreach (var requirement in requirements)
            {
                if (!string.Equals(requirement.Instrument, instrument, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(requirement.Timeframe, timeframe, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var usage in requirement.References)
                {
                    if (!string.Equals(usage.Indicator, reference, StringComparison.OrdinalIgnoreCase)) continue;
                    if (usage.Period != period || usage.Multiplier != multiplier) continue;

                    foreach (var id in usage.StrategyIds)
                        if (!ids.Contains(id)) ids.Add(id);
                }
            }

            var names = ids.Select(id => namesById.TryGetValue(id, out var name) ? name : id).ToList();
            return (ids, names);
        }
    }
}
