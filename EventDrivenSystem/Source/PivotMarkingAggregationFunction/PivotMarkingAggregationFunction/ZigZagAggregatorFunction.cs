using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace PivotMarkingAggregationFunction
{
    public class ZigZagAggregatorFunction
    {
        private readonly ILogger<ZigZagAggregatorFunction> _logger;

        public ZigZagAggregatorFunction(ILogger<ZigZagAggregatorFunction> logger)
        {
            _logger = logger;
        }

        [Function("ZigZagAggregatorFunction")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult("Welcome to Azure Functions!");
        }
    }
}
