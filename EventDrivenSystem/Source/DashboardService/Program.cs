using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string redisConnectionString = builder.Configuration["RedisConnectionString"] ?? "localhost:6382";
string dockerHost = builder.Configuration["DOCKER_HOST_URI"]
    ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
string composeProject = builder.Configuration["COMPOSE_PROJECT_NAME"] ?? "live";

var redisLazy = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(redisConnectionString));
var dockerClient = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // this dashboard's static files change often during development — force browsers to
    // always revalidate (fast, ETag-based) instead of trusting a cached copy blindly,
    // so a UI change here is never masked by a stale cache on the viewing device.
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
});

app.MapGet("/api/services", async () =>
{
    try
    {
        var containers = await dockerClient.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [$"com.docker.compose.project={composeProject}"] = true }
            }
        });

        var result = containers
            .Select(c => new ServiceStatus(
                Name: c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID[..12],
                ComposeService: c.Labels.TryGetValue("com.docker.compose.service", out var svc) ? svc : "",
                Image: c.Image,
                State: c.State,
                Status: c.Status,
                Ports: c.Ports?
                    .Where(p => p.PublicPort > 0)
                    .Select(p => $"{p.PublicPort}->{p.PrivatePort}")
                    .Distinct()
                    .ToArray() ?? Array.Empty<string>(),
                DependsOn: ParseDependsOn(c.Labels)
            ))
            .OrderBy(s => s.ComposeService)
            .ToList();

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unable to reach Docker: {ex.Message}");
    }
});

app.MapGet("/api/freshness", async () =>
{
    try
    {
        var redis = redisLazy.Value;
        var db = redis.GetDatabase();
        var server = redis.GetServer(redis.GetEndPoints().First());

        var items = new List<FreshnessItem>();

        await foreach (var key in server.KeysAsync(pattern: "*"))
        {
            if (await db.KeyTypeAsync(key) != RedisType.String) continue; // skip non-string keys (e.g. RedisJSON/other module types on redis-stack)

            RedisValue value = await db.StringGetAsync(key);
            if (value.IsNullOrEmpty) continue;

            string category = key.ToString().Split(':').FirstOrDefault() ?? "Unknown";
            string? ticker = null, dataType = null, timeframe = null, updatedOnRaw = null, lastUpdateOnRaw = null;

            try
            {
                using var doc = JsonDocument.Parse((string)value!);
                var root = doc.RootElement;
                ticker = GetField(root, "Ticker") ?? GetField(root, "SourceToken");
                dataType = GetField(root, "DataType");
                timeframe = GetField(root, "Timeframe");
                updatedOnRaw = GetField(root, "UpdatedOn");
                lastUpdateOnRaw = GetField(root, "LastUpdateOn");
            }
            catch (JsonException)
            {
                // non-JSON value in this key — show it with fields blank rather than failing the whole request
            }

            DateTime? updatedOn = DateTime.TryParse(updatedOnRaw, out var uo) ? uo : null;
            DateTime? lastUpdateOn = DateTime.TryParse(lastUpdateOnRaw, out var lu) ? lu : null;

            double? ageSeconds = updatedOn.HasValue ? (DateTime.Now - updatedOn.Value).TotalSeconds : null;
            int expectedIntervalMinutes = int.TryParse(timeframe, out var tf) && tf > 0 ? tf : 1;
            bool isStale = ageSeconds is > 0 && ageSeconds.Value > expectedIntervalMinutes * 60 * 2;

            items.Add(new FreshnessItem(
                Key: key.ToString(),
                Category: category,
                Ticker: ticker,
                DataType: dataType,
                Timeframe: timeframe,
                UpdatedOn: updatedOn,
                LastUpdateOn: lastUpdateOn,
                AgeSeconds: ageSeconds,
                IsStale: isStale
            ));
        }

        var ordered = items.OrderBy(i => i.Category).ThenBy(i => i.Ticker).ThenBy(i => int.TryParse(i.Timeframe, out var t) ? t : 0);
        return Results.Ok(ordered);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unable to reach Redis: {ex.Message}");
    }
});

app.Run();

static string[] ParseDependsOn(IDictionary<string, string> labels)
{
    // Compose sets this as "service-a:service_started:false,service-b:service_healthy:true"
    if (!labels.TryGetValue("com.docker.compose.depends_on", out var raw) || string.IsNullOrWhiteSpace(raw))
        return Array.Empty<string>();

    return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(entry => entry.Split(':')[0])
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct()
        .ToArray();
}

static string? GetField(JsonElement root, string propertyName)
{
    if (root.ValueKind != JsonValueKind.Object) return null;

    foreach (var prop in root.EnumerateObject())
    {
        if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
        {
            return prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString()
                : prop.Value.GetRawText();
        }
    }
    return null;
}

record ServiceStatus(string Name, string ComposeService, string Image, string State, string Status, string[] Ports, string[] DependsOn);

record FreshnessItem(
    string Key,
    string Category,
    string? Ticker,
    string? DataType,
    string? Timeframe,
    DateTime? UpdatedOn,
    DateTime? LastUpdateOn,
    double? AgeSeconds,
    bool IsStale);
