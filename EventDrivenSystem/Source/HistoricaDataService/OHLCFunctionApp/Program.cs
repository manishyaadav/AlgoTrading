using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OHLCFunctionApp.Persistence;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddSingleton<BlobServiceClient>((s) => {
            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            return new BlobServiceClient(connectionString);
        });

        // BLOB_APPEND_STRATEGY: "Simple" (default — local/Azurite) or "BlockList" (meant for real
        // Azure; see BlockListAppendStrategy's own doc comment before switching this in production).
        services.AddSingleton<IBlobAppendStrategy>((s) =>
        {
            string strategy = Environment.GetEnvironmentVariable("BLOB_APPEND_STRATEGY") ?? "Simple";
            return strategy.Equals("BlockList", StringComparison.OrdinalIgnoreCase)
                ? new BlockListAppendStrategy()
                : new SimpleReuploadAppendStrategy();
        });

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
    .Build();

host.Run();
