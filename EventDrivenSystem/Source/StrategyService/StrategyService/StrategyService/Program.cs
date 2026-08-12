using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StrategyService.Backtest;
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

        // First HTTP-out dependency this service has ever had — added for the Backtest engine,
        // which needs ohlc-live's historical data the same way WarmUpService's OhlcServiceClient
        // already does (see BacktestOhlcClient.cs for why that's a duplicated client, not a shared
        // one). 30s timeout matches WarmUpService's own — a month's worth of 1-min rows is a real
        // payload, the default HttpClient timeout has bitten this codebase before (see
        // WarmUpService/README.md's "Verified live" section).
        services.AddHttpClient<BacktestOhlcClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
    })
    .Build();

host.Run();
