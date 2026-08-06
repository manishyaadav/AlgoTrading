using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WarmUpService.Strategy
{
    // Thin HTTP wrapper around strategy-live's warm-up-plan endpoint. Kept separate from the
    // function that consumes it so the HTTP details (base URL, JSON options) live in one place.
    public class StrategyServiceClient
    {
        private readonly ILogger<StrategyServiceClient> _logger;
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public StrategyServiceClient(ILoggerFactory loggerFactory, IConfiguration configuration, HttpClient httpClient)
        {
            _logger = loggerFactory.CreateLogger<StrategyServiceClient>();

            string baseUrl = configuration["StrategyServiceUrl"] ?? "http://strategy-live";
            httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient = httpClient;
        }

        public async Task<List<InstrumentWarmUpPlan>> GetWarmUpPlanAsync()
        {
            _logger.LogInformation("Fetching warm-up plan from {BaseAddress}", new Uri(_httpClient.BaseAddress!, "/api/strategies/warm-up-plan"));

            var response = await _httpClient.GetAsync("/api/strategies/warm-up-plan");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var plan = JsonSerializer.Deserialize<List<InstrumentWarmUpPlan>>(json, JsonOptions);

            return plan ?? new List<InstrumentWarmUpPlan>();
        }
    }
}
