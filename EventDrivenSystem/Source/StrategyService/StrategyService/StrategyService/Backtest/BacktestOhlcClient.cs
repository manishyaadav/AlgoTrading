using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StrategyService.Backtest
{
    // Thin HTTP wrapper around ohlc-live, scoped to what a backtest needs: "is there data for this
    // whole date range" and "give me every raw 1-min row in it". Mirrors WarmUpService/Ohlc/
    // OhlcServiceClient.cs's shape/conventions (same duplication reasoning as BacktestOhlc.cs), with
    // one real difference: WarmUpService always asks "the last N trading days back from today";
    // backtest asks about an explicit, arbitrary [startDate, endDate] the user picked, which is a
    // genuinely different question HistoricalSufficiency still answers correctly — see
    // CheckRangeSufficiencyAsync's comment for how the existing "N days back from asOf" endpoint
    // gets reused for that without changing anything on the ohlc-live side.
    public class BacktestOhlcClient
    {
        private readonly ILogger<BacktestOhlcClient> _logger;
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public BacktestOhlcClient(ILoggerFactory loggerFactory, IConfiguration configuration, HttpClient httpClient)
        {
            _logger = loggerFactory.CreateLogger<BacktestOhlcClient>();

            string baseUrl = configuration["OhlcApiBase"] ?? "http://ohlc-live";
            httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient = httpClient;
        }

        // HistoricalSufficiency only ever answers "the last N weekdays counting back from asOf" —
        // there's no separate "between these two dates" endpoint on ohlc-live, and none is needed:
        // walking backward from (endDate + 1 day) for exactly as many weekdays as fall inside
        // [startDate, endDate] checks precisely that range, no more and no less. Same reused
        // capability WarmUpService's own CheckSufficiencyAsync calls — see
        // OHLCFunctionApp/HistoricalSufficiency.cs's own doc comment, which names a backtest
        // date-range check as one of the reasons this endpoint exists as a standalone capability.
        public async Task<HistoricalSufficiencyResponse?> CheckRangeSufficiencyAsync(string exchange, string instrumentName, DateTime startDate, DateTime endDate)
        {
            int weekdays = 0;
            for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1))
                if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                    weekdays++;

            if (weekdays == 0) return new HistoricalSufficiencyResponse(exchange, instrumentName, 0, 0, 0, 0, true, new List<TradingDayAvailability>());

            string url = $"/api/HistoricalSufficiency?exchange={Uri.EscapeDataString(exchange)}&instrumentName={Uri.EscapeDataString(instrumentName)}&daysNeeded={weekdays}&asOf={endDate.Date.AddDays(1):yyyy-MM-dd}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HistoricalSufficiency call failed ({Status}) for {Exchange}/{Instrument}", response.StatusCode, exchange, instrumentName);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<HistoricalSufficiencyResponse>(json, JsonOptions);
        }

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

        // Composes the two calls above: confirm every weekday in [startDate, endDate] actually has
        // data, then pull only the distinct months the range spans (one call per month, not per
        // day) and filter down to exactly that range — so a range landing near a month boundary
        // doesn't silently pull in extra days outside what was asked for. Returns null when the
        // sufficiency check itself couldn't be reached; the caller distinguishes that from "checked,
        // and it's genuinely insufficient" via the HistoricalSufficiencyResponse it already has.
        public async Task<List<RawCandle>?> FetchRangeAsync(string exchange, string instrumentName, DateTime startDate, DateTime endDate)
        {
            var sufficiency = await CheckRangeSufficiencyAsync(exchange, instrumentName, startDate, endDate);
            if (sufficiency == null || !sufficiency.Sufficient) return null;

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
