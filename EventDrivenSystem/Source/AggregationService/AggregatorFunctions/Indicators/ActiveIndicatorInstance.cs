using System.Text.Json;
using AggregatorFunctions.RedisConfig;

namespace AggregatorFunctions.Indicators
{
    // Mirrors WarmUpService's Manifest/ActiveIndicatorInstance.cs field-for-field — this side only
    // reads the JSON WarmUpService writes, never writes it, so this is a read-only copy of that
    // shape (see BlobPathHelper.cs for the established precedent of this exact kind of duplication:
    // no shared project reference exists between these two services).
    public record ActiveIndicatorInstance(
        string Instrument, string Ticker, string Exchange,
        string Timeframe, int TimeframeMinutes,
        string Reference, int Period, int Multiplier);

    // Reads Indicator:Manifest:Active fresh on every call — deliberately no in-process caching.
    // WarmUpService only rewrites this once a day (at NSE's Init), so a plain Redis GET per candle
    // is cheap and side-steps any cache-invalidation question entirely (when does a calculator
    // notice a new day's manifest without a container restart?). Consistent with the rest of this
    // codebase's style — RunningBucket reads/writes Redis on every single candle too.
    public static class IndicatorManifestReader
    {
        private const string ManifestKey = "Indicator:Manifest:Active";

        public static async Task<List<ActiveIndicatorInstance>> GetActiveAsync(RedisHelper redisHelper)
        {
            string? json = await redisHelper.GetStringAsync(ManifestKey);
            if (string.IsNullOrEmpty(json)) return new List<ActiveIndicatorInstance>();

            try
            {
                return JsonSerializer.Deserialize<List<ActiveIndicatorInstance>>(json) ?? new List<ActiveIndicatorInstance>();
            }
            catch (JsonException)
            {
                return new List<ActiveIndicatorInstance>();
            }
        }
    }
}
