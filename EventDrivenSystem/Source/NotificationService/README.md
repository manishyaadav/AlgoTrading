# Notification Service

Kafka-triggered Azure Function. Sits at the end of the pipeline: consumes domain events from Kafka, updates a Redis cache, and forwards a notification to the matching SignalR hub on `signalr-live`. **No HTTP routes** — the container's port only serves the Functions host default page.

## Kafka topics → SignalR hubs

| Function | Consumes topic | Redis key pattern | SignalR hub |
|---|---|---|---|
| `DataFeedNotificationFunctions` | `live-tradingview-ohlc-topic` | `DataFeed:...` | `/datafeedHub` |
| `DataIngestionNotificationFunctions` | `live-dataingestion-ohlc-topic` | `DataIngestion:{Provider}:{Ticker}` (latest snapshot) + `Ingestion:Count:{Provider}:{Ticker}:1min:{yyyy-MM-dd}` (today's count, see below) | `/dataIngestionHub` |
| `AlertNotificationFunction` | `live-tradingview-alert-topic` | — | `/alertIngestionHub` |
| `CountryNotificationFunctions` | `live-country-workflow-topic` | `India` (bare key, no prefix — not `Country:India`, an inconsistency worth knowing) | `/countryHub` |
| `ExchangeNotificationFunctions` | `live-exchange-workflow-topic` | `Exchange:{ExchangeName}` (e.g. `Exchange:NSE`, `Exchange:NFO`) | `/exchangeHub` |
| `DataAggregationNotificationFunctions` | `live-aggregation-ohlc-5min-topic`, `-10min-`, `-15min-`, `-30min-`, `-60min-`, `-75min-` | `Aggregation:OHLC:{Ticker}:{Timeframe}:Min` (latest snapshot) + `Aggregation:Count:{Ticker}:{Timeframe}min:{yyyy-MM-dd}` (today's count, see below) | `/aggregationHub` |

⚠️ **`DataFeed:{Ticker}` (from `DataFeedNotificationFunctions`) still shows `WindowsStartTime` in raw UTC** (e.g. `06:08:00`, no `Z` but still the UTC digits), unlike `DataIngestion:{Provider}:{Ticker}` and every `Aggregation:*` key, which are genuine IST now (see [DataIngestionService/ReadMe.md](../DataIngestionService/ReadMe.md#where-windowsstarttime-becomes-ist)). This is a known, deliberately out-of-scope gap: `DataFeedNotificationFunctions` consumes `live-tradingview-ohlc-topic` directly — the raw feed, upstream of where the IST conversion happens (`DataIngestionFunctions.CreateDataForIngestion`, which republishes onto `live-dataingestion-ohlc-topic`) — so it never sees the converted value. Worth fixing the same way if this cache is ever relied on for anything beyond raw feed inspection.
### Per-day candle counts (for the dashboard's Data page)

The snapshot caches above only ever hold the *latest* candle — they can't answer "how many candles have landed today." A second Redis structure exists just for that: a **SET** per provider/ticker/timeframe/day (`Ingestion:Count:{Provider}:{Ticker}:1min:{date}`, `Aggregation:Count:{Ticker}:{Timeframe}min:{date}`), where each member is that candle's `WindowsStartTime`. `SADD` naturally de-dupes a re-delivered candle instead of over-counting it, and `SCARD` gives the current count. Written by `RedisHelper.AddToCountSetAsync`, called from the same `UpdateRedisCache` methods that already write the snapshot caches — one extra Redis write per message, no new Kafka consumption. Each SET gets a 3-day TTL (refreshed on every write) so it doesn't grow forever; there's no scheduled cleanup job, the TTL is the cleanup.

### Data provider is discovered, not hardcoded

`DataIngestionNotificationFunctions` used to hardcode the literal `"TradingView"` into every ingestion Redis key — a leftover from when TradingView was the only source. It now reads `dataEvent.DataSource` (a `DataFeedSourceEnum`, see `SharedLibrary/Enums/DataFeed/DataFeedSourceEnum.cs`) off the deserialized event and uses that as the provider segment in both the snapshot key (`DataIngestion:{Provider}:{Ticker}`) and the count key (`Ingestion:Count:{Provider}:{Ticker}:1min:{date}`). A future provider (Zerodha, Kite, NSE direct, ...) shows up automatically the moment it starts publishing `DataIngestionMinDataEvent` messages with the right `DataSource` — no code change needed here or on the dashboard. `ResolveDataSource` falls back to `"Unknown"` rather than crashing if a message ever carries an undefined enum value (e.g. a stale message from before the fix below). Aggregation's Redis keys have no provider segment — once candles are aggregated, the concept isn't tracked post-ingestion, so this only applies to the Data page's Ingestion section.

### Count-key dates are host-timezone-independent

Both count keys' `{date}` segment used to come from `DateTime.Now`, which is only IST because this container's `TZ=Asia/Kolkata` env var happens to be set — on a host where that assumption doesn't hold (a different machine, or a future cloud platform that resets/ignores container `TZ`), the date would silently shift to whatever timezone the host actually has, with no error to signal it. Both now compute the date via `DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow)` instead — `DateTime.UtcNow` is always correct regardless of host timezone config, so the explicit conversion is what's actually doing the work, not an assumption about the container. Same fix applied in `AggregationService` (bucket alignment) and `DashboardService` (today/session-time calculations) — see those services' READMEs.

This only works because of the fix below — `DataEventBase.DataSource` was hitting the exact same JSON contract bug as `ExchangeEvent.ExchangeTimerAction`.

## ⚠️ Cross-service JSON contract — a real bug already happened here

Every producer service (`country-live`, `exchange-live`, etc.) serializes with **`System.Text.Json`**. This service deserializes incoming Kafka messages with **`Newtonsoft.Json`** (`JsonConvert`), which does not understand `System.Text.Json`'s `[JsonPropertyName]` attribute at all — it falls back to matching the plain C# member name, case-insensitively.

That mismatch actually broke `ExchangeEvent`: the producer-side model renamed `ExchangeTimerAction` to `"action"` on the wire via `[JsonPropertyName("action")]`. Newtonsoft couldn't match `"action"` to `ExchangeTimerAction` (not even case-insensitively — it's a different word), so the property silently deserialized to its C# default (enum value `0`), which isn't a valid `ExchangeActionEnum` member — `ExchangeNotificationFunctions` threw `ArgumentException: Invalid ExchangeActionEnum value` on **every single exchange event**, and the Redis cache write never happened. `CountryEvent` happened to survive by coincidence (its renames — `"name"`, `"date"`, `"state"` — are just lowercased versions of the real property names, which Newtonsoft's case-insensitive default *does* still match).

**Fix applied**: removed the `[JsonPropertyName]` overrides from `CimplifyBase`, `EventBase`, `ExchangeEvent`, and — found proactively while wiring up dynamic data-provider discovery — `DataEventBase` (`DataSource`/`DataType`, renamed to `"sourceName"`/`"dataType"` on the wire) on both the producer and consumer copies, so serialization just uses the plain C# member names on both sides — which both `System.Text.Json` and `Newtonsoft.Json` agree on by default. `DataEventBase.DataSource` had been silently deserializing to enum default `0` (not a valid `DataFeedSourceEnum` member) on this service ever since it was added — nothing crashed because nothing read the field back out of the deserialized event until the dynamic-provider work above started doing so, and the code just hardcoded the literal `"TradingView"` string past it instead.

**If you add a new event type or a new field crossing this Kafka boundary**: either don't rename properties via `[JsonPropertyName]` at all, or if you do, add a matching Newtonsoft-compatible `[JsonProperty(...)]` attribute on this service's copy of the model — otherwise the same silent-default-value failure mode will happen again, and it won't throw a compile error or an obvious runtime error at the point of the actual mistake.

## Operations

### Compose

Service key: `notification-live` in [docker-compose-live.yml](../../../docker-compose-live.yml). Container: `notification-service-live-container`. Host port `9098`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d notification-live
docker-compose -f docker-compose-live.yml -p live logs -f notification-live
```

### Build

```bash
cd EventDrivenSystem/Source/NotificationService
docker build -t notification-service-live-image:v1 -f Dockerfile .
docker-compose -f docker-compose-live.yml -p live up -d notification-live   # recreate with the new image
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `AzureWebJobsStorage` | points at `azurite-live` |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `ASPNETCORE_ENVIRONMENT` | `docker` |
| `KAFKA_BROKER_URL` | `kafka-live:29092` |
| `RedisConnectionString` | `redis-live:6379` (internal container port — from the host it's `localhost:6382`) |
| `SignalRServiceUrl` | `http://signalr-live:5091` |

### Testing

No HTTP endpoint to poke directly. Trigger `dataingestion`'s webhook route (see its README) and watch this service's logs — you should see `Updating Redis Cache...` and a hub broadcast follow within a couple of seconds. To inspect Redis directly:

```bash
docker exec -it redis-live redis-cli GET "DataIngestion:TradingView:NIFTY"
docker exec -it redis-live redis-cli SMEMBERS "Ingestion:Count:TradingView:NIFTY:1min:2026-08-05"
```
