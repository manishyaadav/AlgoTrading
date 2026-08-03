using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using StrategyService.Strategy;

namespace StrategyService
{
    public class StrategyFunctions
    {
        private readonly ILogger<StrategyFunctions> _logger;

        public StrategyFunctions(ILogger<StrategyFunctions> logger)
        {
            _logger = logger;
        }

        [Function(nameof(ListStrategies))]
        public async Task<HttpResponseData> ListStrategies(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "strategies")] HttpRequestData req)
        {
            var summaries = StrategyMaker.LoadAll().Select(s => new
            {
                Id = s.Id,
                s.Strategy.StrategyName,
                s.Strategy.Exchange,
                s.Strategy.Broker,
                s.Strategy.Version,
                s.Strategy.DeployedVersion,
                s.Strategy.Goals,
                SubStrategyCount = s.Strategy.Strategies?.Count ?? 0,
                Risk = s.Strategy.Strategies?.FirstOrDefault()?.Risk,
                TradeType = s.Strategy.Strategies?.FirstOrDefault()?.TradeType,
                Instruments = s.Strategy.Strategies?.SelectMany(ts => ts.Instruments ?? new()).Distinct().ToList() ?? new(),
                InstrumentTimeframes = StrategyMaker.ExtractInstrumentTimeframeDictionary(s.Strategy),
            }).ToList();

            return await JsonResponse(req, HttpStatusCode.OK, summaries);
        }

        [Function(nameof(GetStrategy))]
        public async Task<HttpResponseData> GetStrategy(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "strategies/{id}")] HttpRequestData req, string id)
        {
            var strategy = StrategyMaker.GetById(id);
            if (strategy == null)
                return await JsonResponse(req, HttpStatusCode.NotFound, new { error = $"No strategy with id '{id}'" });

            return await JsonResponse(req, HttpStatusCode.OK, strategy);
        }

        [Function(nameof(SaveStrategy))]
        public async Task<HttpResponseData> SaveStrategy(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", "post", Route = "strategies/{id}")] HttpRequestData req, string id)
        {
            string body;
            using (var reader = new StreamReader(req.Body))
                body = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(body))
                return await JsonResponse(req, HttpStatusCode.BadRequest, new { error = "Request body is empty" });

            try
            {
                var saved = StrategyMaker.SaveById(id, body);
                _logger.LogInformation("Saved strategy '{Id}' ({Name})", id, saved.StrategyName);
                return await JsonResponse(req, HttpStatusCode.OK, saved);
            }
            catch (JsonException ex)
            {
                return await JsonResponse(req, HttpStatusCode.BadRequest, new { error = $"Invalid strategy JSON: {ex.Message}" });
            }
        }

        [Function(nameof(DeployStrategy))]
        public async Task<HttpResponseData> DeployStrategy(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "strategies/{id}/deploy")] HttpRequestData req, string id)
        {
            // Marks the current Version as deployed. Deliberately does NOT do anything about a
            // previously-deployed version yet (no cleanup) — that behavior is still to be defined.
            var strategy = StrategyMaker.DeployById(id);
            if (strategy == null)
                return await JsonResponse(req, HttpStatusCode.NotFound, new { error = $"No strategy with id '{id}'" });

            _logger.LogInformation("Deployed strategy '{Id}' at version {Version}", id, strategy.Version);
            return await JsonResponse(req, HttpStatusCode.OK, strategy);
        }

        [Function(nameof(DeleteStrategy))]
        public async Task<HttpResponseData> DeleteStrategy(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "strategies/{id}")] HttpRequestData req, string id)
        {
            var deleted = StrategyMaker.DeleteById(id);
            return await JsonResponse(req, deleted ? HttpStatusCode.OK : HttpStatusCode.NotFound,
                deleted ? new { status = "deleted", id } : new { error = $"No strategy with id '{id}'" });
        }

        [Function(nameof(StrategiesOptions))]
        public HttpResponseData StrategiesOptions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "strategies/{*rest}")] HttpRequestData req)
        {
            // CORS preflight — the dashboard's browser JS calls this API cross-origin (different published port)
            var response = req.CreateResponse(HttpStatusCode.NoContent);
            AddCors(response);
            return response;
        }

        // camelCase on the wire (matches the convention dashboard-live's own API already uses),
        // even though the on-disk config files stay PascalCase (matches the C# model, easier to hand-author).
        private static readonly JsonSerializerOptions HttpJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private static async Task<HttpResponseData> JsonResponse(HttpRequestData req, HttpStatusCode status, object payload)
        {
            var response = req.CreateResponse(status);
            AddCors(response);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(JsonSerializer.Serialize(payload, HttpJsonOptions));
            return response;
        }

        private static void AddCors(HttpResponseData response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, PUT, POST, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        }
    }
}
