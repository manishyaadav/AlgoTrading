using Confluent.Kafka;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotificationService.RedisConfig;

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

        services.AddTransient<RedisHelper>();
    })
    .Build();

host.Run();
