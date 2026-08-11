using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrategyService.RedisConfig;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // First Redis dependency this service has ever had — added for the Rule Engine page
        // (GetRuleStatus), which needs to read live indicator/session state to evaluate rules
        // against. Still read-only: this service never writes to Redis.
        services.AddSingleton<RedisHelper>();
    })
    .Build();

host.Run();
