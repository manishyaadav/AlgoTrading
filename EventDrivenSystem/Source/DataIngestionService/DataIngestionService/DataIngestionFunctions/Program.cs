using Confluent.Kafka;
using DataIngestionFunctions.RedisConfig;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        // Singleton, not the Transient other services register their own RedisHelper copy as — see
        // RedisHelper.cs for why (SessionCloseGapFillFunction calls it every minute all day).
        services.AddSingleton<RedisHelper>();

        services.AddSingleton<IProducer<string, string>>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var kafkaBrokerUrl = configuration["KAFKA_BROKER_URL"];

            var config = new ProducerConfig
            {
                BootstrapServers = kafkaBrokerUrl,
                // ...other configuration options (acks, retries, etc.)
            };
            return new ProducerBuilder<string, string>(config).Build();
        });
    })
    .Build();

host.Run();
