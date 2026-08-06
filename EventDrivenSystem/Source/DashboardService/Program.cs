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

string ohlcApiBase = builder.Configuration["OhlcApiBase"] ?? "http://ohlc-live";

// "Now" is always computed explicitly from DateTime.UtcNow + an explicit IST conversion — never
// DateTime.Now — so every "today"/staleness/session-time calculation below stays correct regardless
// of the container's TZ env var (or a future cloud host that doesn't set one the same way). See
// IstNow() near the bottom of this file.

var redisLazy = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(redisConnectionString));
var dockerClient = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();
var ohlcHttpClient = new HttpClient { BaseAddress = new Uri(ohlcApiBase), Timeout = TimeSpan.FromSeconds(5) };

// "/" serves the console entry page; the dashboard itself stays at /index.html,
// which is where every card on the console links to.
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "home.html", "index.html" }
});
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

            double? ageSeconds = updatedOn.HasValue ? (IstNow() - updatedOn.Value).TotalSeconds : null;
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

app.MapGet("/api/country", async () =>
{
    try
    {
        var db = redisLazy.Value.GetDatabase();
        RedisValue value = await db.StringGetAsync("India");
        if (value.IsNullOrEmpty)
            return Results.Ok(new CountryStatus(false, null, null, null, null, null, null, null, false));

        using var doc = JsonDocument.Parse((string)value!);
        var root = doc.RootElement;
        var date = GetField(root, "Date");

        return Results.Ok(new CountryStatus(
            Found: true,
            Name: GetField(root, "Name"),
            Date: date,
            State: GetField(root, "State"),
            Holiday: GetHolidayInfo(root, "Holiday"),
            NextHoliday: GetHolidayInfo(root, "NextHoliday"),
            UpdatedOn: GetField(root, "UpdatedOn"),
            LastUpdateOn: GetField(root, "LastUpdateOn"),
            IsToday: date == IstNow().ToString("yyyy-MM-dd")
        ));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unable to reach Redis: {ex.Message}");
    }
});

app.MapGet("/api/exchanges", async () =>
{
    try
    {
        var redis = redisLazy.Value;
        var db = redis.GetDatabase();
        var server = redis.GetServer(redis.GetEndPoints().First());
        var today = IstNow().ToString("yyyy-MM-dd");

        var results = new List<ExchangeStatus>();
        await foreach (var key in server.KeysAsync(pattern: "Exchange:*"))
        {
            if (await db.KeyTypeAsync(key) != RedisType.String) continue;

            RedisValue value = await db.StringGetAsync(key);
            if (value.IsNullOrEmpty) continue;

            using var doc = JsonDocument.Parse((string)value!);
            var root = doc.RootElement;
            var exchangeName = key.ToString().Split(':', 2).ElementAtOrDefault(1) ?? key.ToString();
            var date = GetField(root, "Date");

            // Exchange's Redis cache stores State as the raw enum int (1-5), unlike Country's
            // which stores it pre-converted to a string — map it here for display.
            var stateName = GetField(root, "State") switch
            {
                "1" => "Initiated",
                "2" => "PreOpened",
                "3" => "Opened",
                "4" => "PreClosed",
                "5" => "Closed",
                var other => other,
            };

            results.Add(new ExchangeStatus(
                ExchangeName: exchangeName,
                Found: true,
                Date: date,
                State: stateName,
                UpdatedOn: GetField(root, "UpdatedOn"),
                LastUpdateOn: GetField(root, "LastUpdateOn"),
                IsToday: date == today
            ));
        }

        return Results.Ok(results.OrderBy(e => e.ExchangeName));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unable to reach Redis: {ex.Message}");
    }
});

app.MapGet("/api/data-ingestion", async () =>
{
    try
    {
        var results = new List<CandleCountStatus>();
        // Provider isn't hardcoded — "DataIngestion:{Provider}:{Ticker}" keys are scanned and both
        // segments are pulled out, so a future provider (Zerodha, Kite, NSE direct, ...) shows up
        // automatically the moment it starts writing to Redis, no code change needed here.
        foreach (var (provider, ticker) in await DiscoverProviderTickers(redisLazy.Value, "DataIngestion:*", providerSegmentIndex: 1, tickerSegmentIndex: 2))
        {
            results.Add(await BuildCandleCountStatus(
                redisLazy.Value, ohlcHttpClient, ticker, timeframeMinutes: 1,
                countKeyPrefix: $"Ingestion:Count:{provider}",
                snapshotKeyBuilder: t => $"DataIngestion:{provider}:{t}",
                provider: provider));
        }
        return Results.Ok(results.OrderBy(r => r.Contract));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unable to build data-ingestion status: {ex.Message}");
    }
});

app.MapGet("/api/aggregation", async () =>
{
    try
    {
        // Starting with 5-min only for now — same shape extends to 10/15/30/60/75 later by adding
        // more entries here, no other code changes needed.
        int[] timeframes = { 5 };
        var results = new List<CandleCountStatus>();

        foreach (var tf in timeframes)
        {
            foreach (var ticker in await DiscoverTickers(redisLazy.Value, $"Aggregation:OHLC:*:{tf}:Min", 2))
            {
                results.Add(await BuildCandleCountStatus(
                    redisLazy.Value, ohlcHttpClient, ticker, timeframeMinutes: tf,
                    countKeyPrefix: "Aggregation:Count",
                    snapshotKeyBuilder: t => $"Aggregation:OHLC:{t}:{tf}:Min"));
            }
        }

        return Results.Ok(results.OrderBy(r => r.Timeframe).ThenBy(r => r.Contract));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Unable to build aggregation status: {ex.Message}");
    }
});

app.Run();

// Scans for keys matching `pattern` and pulls the ticker out of segment index `tickerSegmentIndex`
// (colon-delimited) — e.g. "DataIngestion:TradingView:NIFTY" -> segment 2 -> "NIFTY". This is how
// the Data page "makes room" for more contracts without hardcoding a ticker list: whatever's
// actually in Redis today drives what shows up, the same discovery approach already used for the
// Services and Exchanges pages.
static async Task<List<string>> DiscoverTickers(ConnectionMultiplexer redis, string pattern, int tickerSegmentIndex)
{
    var server = redis.GetServer(redis.GetEndPoints().First());
    var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await foreach (var key in server.KeysAsync(pattern: pattern))
    {
        var segments = key.ToString().Split(':');
        if (segments.Length > tickerSegmentIndex)
            tickers.Add(segments[tickerSegmentIndex]);
    }

    return tickers.ToList();
}

// Same discovery idea as DiscoverTickers, but also pulls out the provider segment (e.g.
// "DataIngestion:TradingView:NIFTY" -> ("TradingView", "NIFTY")) instead of assuming a fixed
// provider name — this is what makes the ingestion side provider-agnostic.
static async Task<List<(string Provider, string Ticker)>> DiscoverProviderTickers(
    ConnectionMultiplexer redis, string pattern, int providerSegmentIndex, int tickerSegmentIndex)
{
    var server = redis.GetServer(redis.GetEndPoints().First());
    var pairs = new HashSet<(string, string)>();

    await foreach (var key in server.KeysAsync(pattern: pattern))
    {
        var segments = key.ToString().Split(':');
        if (segments.Length > Math.Max(providerSegmentIndex, tickerSegmentIndex))
            pairs.Add((segments[providerSegmentIndex], segments[tickerSegmentIndex]));
    }

    return pairs.ToList();
}

static async Task<CandleCountStatus> BuildCandleCountStatus(
    ConnectionMultiplexer redis, HttpClient ohlcClient, string ticker, int timeframeMinutes,
    string countKeyPrefix, Func<string, string> snapshotKeyBuilder, string? provider = null)
{
    var db = redis.GetDatabase();
    var today = IstNow().ToString("yyyy-MM-dd");

    var countKey = $"{countKeyPrefix}:{ticker}:{timeframeMinutes}min:{today}";
    long count = await db.SetLengthAsync(countKey);
    bool inRedis = count > 0;

    int expectedTotal = 375 / timeframeMinutes; // 375 one-minute bars in a 9:15-3:30 session
    int expectedSoFar = ExpectedSoFar(timeframeMinutes, expectedTotal);
    string status = ComputeStatus(count, expectedSoFar);

    // Latest snapshot, for display — separate key from the count SET above.
    RedisValue snapshot = await db.StringGetAsync(snapshotKeyBuilder(ticker));
    string? latestWindowStartTime = null, updatedOn = null;
    if (!snapshot.IsNullOrEmpty)
    {
        try
        {
            using var doc = JsonDocument.Parse((string)snapshot!);
            latestWindowStartTime = GetField(doc.RootElement, "WindowsStartTime") ?? GetField(doc.RootElement, "WindowStartTime");
            updatedOn = GetField(doc.RootElement, "UpdatedOn");
        }
        catch (JsonException) { /* leave blank rather than fail the whole request */ }
    }

    bool inAzurite = await CheckAzurite(ohlcClient, ticker, today);

    return new CandleCountStatus(ticker, timeframeMinutes, (int)count, expectedTotal, expectedSoFar, status, inRedis, inAzurite, latestWindowStartTime, updatedOn, provider);
}

static int ExpectedSoFar(int intervalMinutes, int expectedTotal)
{
    var now = IstNow();
    var open = now.Date.AddHours(9).AddMinutes(15);
    var close = now.Date.AddHours(15).AddMinutes(30);

    if (now < open) return 0;
    if (now >= close) return expectedTotal;

    var elapsedMinutes = (now - open).TotalMinutes;
    return Math.Min(expectedTotal, (int)(elapsedMinutes / intervalMinutes));
}

static string ComputeStatus(long count, int expectedSoFar)
{
    // Before market open there's nothing to be behind on yet — that's "pending", not a warning.
    if (expectedSoFar <= 0) return count > 0 ? "green" : "pending";

    var ratio = (double)count / expectedSoFar;
    if (ratio >= 0.9) return "green";
    if (ratio >= 0.5) return "amber";
    return "red";
}

static async Task<bool> CheckAzurite(HttpClient ohlcClient, string ticker, string date)
{
    try
    {
        // ohlc-live resolves instrumentName to a blob purely by whether it contains "bank"+"nifty" —
        // passing the ticker straight through works for both, see OHLCFunctionApp/GetOHLCDataByDate.cs.
        var res = await ohlcClient.GetAsync($"/api/GetOHLCDataByDate?date={date}&exchange=nse&instrumentName={Uri.EscapeDataString(ticker)}");
        if (!res.IsSuccessStatusCode) return false;

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("TotalRecords", out var total) && total.GetInt32() > 0;
    }
    catch
    {
        // ohlc-live unreachable, or no blob for today (during live market hours this is the honest,
        // expected answer — the historical blob upload is a manual, after-hours process today).
        return false;
    }
}

// The one and only place "current IST time" gets computed in this file — every "today"/staleness/
// session-time calculation above calls this instead of DateTime.Now, so none of them silently break
// if this container's TZ env var is ever missing or wrong (e.g. a future cloud host that doesn't set
// it the same way docker-compose does today). DateTime.UtcNow is always correct regardless of host
// timezone config; TimeZoneInfo does the IST conversion explicitly from there. (Top-level statements
// can't declare a static readonly field to cache the TimeZoneInfo lookup, but TimeZoneInfo caches
// resolved IDs internally after the first call, so this isn't a real per-request cost.)
static DateTime IstNow() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));

static HolidayInfo? GetHolidayInfo(JsonElement root, string propertyName)
{
    if (root.ValueKind != JsonValueKind.Object) return null;

    foreach (var prop in root.EnumerateObject())
    {
        if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Object)
            return new HolidayInfo(GetField(prop.Value, "Date"), GetField(prop.Value, "Reason"));
    }
    return null;
}

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

record HolidayInfo(string? Date, string? Reason);

record CountryStatus(
    bool Found,
    string? Name,
    string? Date,
    string? State,
    HolidayInfo? Holiday,
    HolidayInfo? NextHoliday,
    string? UpdatedOn,
    string? LastUpdateOn,
    bool IsToday);

record ExchangeStatus(
    string ExchangeName,
    bool Found,
    string? Date,
    string? State,
    string? UpdatedOn,
    string? LastUpdateOn,
    bool IsToday);

record CandleCountStatus(
    string Contract,
    int Timeframe,
    int Count,
    int ExpectedTotal,
    int ExpectedSoFar,
    string Status, // "green" | "amber" | "red" | "pending"
    bool InRedis,
    bool InAzurite,
    string? LatestWindowStartTime,
    string? UpdatedOn,
    string? Provider); // null for aggregation entries — the concept only exists on the ingestion side today
