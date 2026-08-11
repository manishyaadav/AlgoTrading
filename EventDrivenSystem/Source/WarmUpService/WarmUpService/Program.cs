using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WarmUpService.Ohlc;
using WarmUpService.RedisConfig;
using WarmUpService.Strategy;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration(builder =>
    {
        builder.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
               .AddEnvironmentVariables();
    })
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddSingleton<RedisHelper>();
        services.AddHttpClient<StrategyServiceClient>();
        // 30s, not the HttpClient default of 100s — a month's worth of 1-min rows is a bigger
        // payload than DashboardService's quick freshness checks (which use a tighter 5s), but a
        // genuinely hung ohlc-live call still shouldn't leave a warm-up run hanging for a minute
        // and a half per instrument before failing.
        services.AddHttpClient<OhlcServiceClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
    })
    .Build();

host.Run();
