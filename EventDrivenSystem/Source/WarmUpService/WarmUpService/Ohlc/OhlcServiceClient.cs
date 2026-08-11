using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WarmUpService.Ohlc
{
    // Mirrors ohlc-live's GetOHLCByYearAndMonth/GetOHLCDataByDate response shape exactly, including
    // the "Recods" typo in the real field name (see OHLCFunctionApp/GetOHLCByYearAndMonth.cs) —
    // matching the typo here is required, not optional, since PropertyNameCaseInsensitive only
    // handles case, not a genuinely different spelling.
    public record RawCandle(
        [property: JsonPropertyName("ContractName")] string ContractName,
        [property: JsonPropertyName("Timeframe")] int Timeframe,
        [property: JsonPropertyName("Date")] DateTime Date,
        [property: JsonPropertyName("Open")] double Open,
        [property: JsonPropertyName("Low")] double Low,
        [property: JsonPropertyName("High")] double High,
        [property: JsonPropertyName("Close")] double Close,
        [property: JsonPropertyName("Volume")] int Volume);

    public record OhlcMonthResponse(
        [property: JsonPropertyName("TotalRecords")] int TotalRecords,
        [property: JsonPropertyName("FullPath")] string FullPath,
        [property: JsonPropertyName("Recods")] List<RawCandle> Recods);

    public record TradingDayAvailability(string Date, bool Exists, string Path);

    public record HistoricalSufficiencyResponse(
        string Exchange, string InstrumentName, int DaysNeeded, int DaysChecked,
        int DaysAvailable, int DaysMissing, bool Sufficient, List<TradingDayAvailability> Days);

    // Thin HTTP wrapper around ohlc-live's HTTP routes — the warm-up cold-start fetch path
    // (WARMUP_AND_INDICATOR_PLAN.md section 2b step 3) pulling raw 1-min historical data via
    // ohlc-live rather than touching Azurite directly, same as every other consumer of historical
    // data in this codebase.
    public class OhlcServiceClient
    {
        private readonly ILogger<OhlcServiceClient> _logger;
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public OhlcServiceClient(ILoggerFactory loggerFactory, IConfiguration configuration, HttpClient httpClient)
        {
            _logger = loggerFactory.CreateLogger<OhlcServiceClient>();

            string baseUrl = configuration["OhlcApiBase"] ?? "http://ohlc-live";
            httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient = httpClient;
        }

        // Reuses 2d's already-shipped capability rather than re-deriving "which trading days do we
        // need" here — HistoricalSufficiency already walks backward from asOf skipping weekends and
        // confirms each day's monthly rollup blob actually exists.
        public async Task<HistoricalSufficiencyResponse?> CheckSufficiencyAsync(string exchange, string instrumentName, int daysNeeded, DateTime? asOf = null)
        {
            string url = $"/api/HistoricalSufficiency?exchange={Uri.EscapeDataString(exchange)}&instrumentName={Uri.EscapeDataString(instrumentName)}&daysNeeded={daysNeeded}";
            if (asOf.HasValue) url += $"&asOf={asOf.Value:yyyy-MM-dd}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HistoricalSufficiency call failed ({Status}) for {Exchange}/{Instrument}", response.StatusCode, exchange, instrumentName);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<HistoricalSufficiencyResponse>(json, JsonOptions);
        }

        // Full monthly rollup — every 1-min row ohlc-live has for that month, not coarsened. Called
        // once per distinct (year, month) the required trading days span, not once per day, since a
        // month's worth of 1-min rows is small (~650KB per WARMUP_AND_INDICATOR_PLAN.md figures) and
        // most lookback windows (a handful of trading days) land inside one or two calls.
        public async Task<List<RawCandle>> GetMonthAsync(int year, int month, string exchange, string instrumentName)
        {
            string url = $"/api/GetOHLCByYearAndMonth?year={year}&month={month}&exchange={Uri.EscapeDataString(exchange)}&instrumentName={Uri.EscapeDataString(instrumentName)}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GetOHLCByYearAndMonth call failed ({Status}) for {Exchange}/{Instrument} {Year}-{Month}", response.StatusCode, exchange, instrumentName, year, month);
                return new List<RawCandle>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<OhlcMonthResponse>(json, JsonOptions);
            return parsed?.Recods ?? new List<RawCandle>();
        }

        // Composes the two calls above into the actual fetch WarmUpService needs: confirm the
        // required trading days exist (2d), then pull only the distinct months those days span
        // (one HTTP call per month, not per day) and filter down to exactly the confirmed dates —
        // so a lookback window landing near a month boundary doesn't pull in extra days it didn't
        // ask for. Returns null (not an empty list) when HistoricalSufficiency itself is unreachable
        // or the confirmed days aren't actually sufficient, so callers can tell "no data available"
        // apart from "data available, but genuinely zero rows".
        //
        // NSE-index-only assumption baked in here: instrumentName is passed through unchanged across
        // every month called. That's fine for the index/spot instruments this whole effort is scoped
        // to (see plan doc — futures/options explicitly out of scope), but would be wrong for NFO
        // futures, where the blob name is contract-specific and changes across a monthly roll — a
        // lookback spanning a roll would need a different instrumentName per month. Not handled here;
        // revisit if this ever needs to cover futures.
        public async Task<List<RawCandle>?> FetchHistoricalCandlesAsync(string exchange, string instrumentName, int daysNeeded, DateTime? asOf = null)
        {
            var sufficiency = await CheckSufficiencyAsync(exchange, instrumentName, daysNeeded, asOf);
            if (sufficiency == null)
                return null;

            if (!sufficiency.Sufficient)
            {
                _logger.LogWarning(
                    "Insufficient history for {Exchange}/{Instrument}: needed {Needed} trading day(s), only {Available} available ({Missing} missing).",
                    exchange, instrumentName, sufficiency.DaysNeeded, sufficiency.DaysAvailable, sufficiency.DaysMissing);
                return null;
            }

            var neededDates = sufficiency.Days.Where(d => d.Exists).Select(d => DateTime.Parse(d.Date)).ToList();
            var months = neededDates.Select(d => (d.Year, d.Month)).Distinct();

            var allRows = new List<RawCandle>();
            foreach (var (year, month) in months)
                allRows.AddRange(await GetMonthAsync(year, month, exchange, instrumentName));

            var neededDateSet = neededDates.Select(d => d.Date).ToHashSet();
            return allRows.Where(r => neededDateSet.Contains(r.Date.Date)).OrderBy(r => r.Date).ToList();
        }
    }
}
