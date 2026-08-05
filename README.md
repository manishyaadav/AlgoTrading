# AlgoTrading — Event-Driven System

An event-driven pipeline that ingests TradingView alerts (and scheduled exchange/holiday/country data), pushes everything through Kafka, aggregates OHLC candles into multiple timeframes, and fans out notifications over SignalR with Redis as the read cache.

```
TradingView ─▶ ngrok ─▶ dataingestion (8080) ─▶ Kafka ─┬─▶ aggregation-live (8095) ─▶ Kafka
                                                        └─▶ notification-live (9098) ─▶ Redis + signalr-live (8098)

country-live (8093) ─┐
exchange-live (8094) ┼─▶ Kafka ─▶ notification-live ─▶ Redis + signalr-live
holiday-live (8091) ─┘ (called by country-live over HTTP)

ohlc-live (8092) — historical OHLC query API, not wired into the Kafka flow
strategy-live (8096) — CRUD API over strategy JSON configs, not wired into the Kafka flow (yet)
dashboard-live (8099) — reads Docker + Redis + strategy-live directly; not part of the Kafka pipeline
```

All services run under one Docker Compose stack, defined in [docker-compose-live.yml](docker-compose-live.yml), project name `live`.

## Prerequisites

- Docker Desktop (Windows, with the `live-network` bridge created automatically by Compose)
- .NET 8 SDK — only needed if you rebuild a service image from source
- [ngrok](https://ngrok.com/) — needed only for `dataingestion`, to receive TradingView webhooks

## Quick start

```bash
# from the repo root, next to docker-compose-live.yml
docker-compose -f docker-compose-live.yml -p live up -d      # first time / after adding services
docker-compose -f docker-compose-live.yml -p live start      # resume previously-created containers
docker-compose -f docker-compose-live.yml -p live ps         # check status
docker-compose -f docker-compose-live.yml -p live logs -f <service>
docker-compose -f docker-compose-live.yml -p live stop       # stop, keep containers/volumes
docker-compose -f docker-compose-live.yml -p live down       # remove containers (volumes survive)
```

`start` only works on containers that already exist — if a service was never created, use `up -d` instead (this trips people up on a fresh clone).

## Services

| Service (compose key) | Container | Host port | Purpose | Docs |
|---|---|---|---|---|
| `redis-live` | redis-live | 6382→6379 | Cache used by `notification-live` | — |
| `dashboard-live` | dashboard-live | 8099→8080 | Status dashboard — service health, data freshness, exchange session timeline, ingestion/aggregation candle-count status, strategy management: http://localhost:8099 | [Source/DashboardService/README.md](EventDrivenSystem/Source/DashboardService/README.md) |
| `azurite-live` | azurite-live | 10000-10002 | Local Azure Storage emulator (all Functions apps use it for `AzureWebJobsStorage`) | — |
| `zookeeper-live` | zookeeper-live | internal (2182) | Kafka coordination | — |
| `kafka-live` | kafka-live | 9092 (host) / 29092 (internal) | Message broker — the backbone of the whole pipeline | — |
| `kafdrop-live` | kafdrop-live | 9000 | Web UI to browse Kafka topics: http://localhost:9000 | — |
| `dataingestion` | dataingestion-service | 8080 | Receives TradingView webhooks via ngrok, publishes OHLC to Kafka | [Source/DataIngestionService/ReadMe.md](EventDrivenSystem/Source/DataIngestionService/ReadMe.md) |
| `holiday-live` | holiday-service-live-container | 8091 | Holiday-calendar query API, called by `country-live` | [Source/HistoricaDataService/HolidayFunctionApp/README.md](EventDrivenSystem/Source/HistoricaDataService/HolidayFunctionApp/README.md) |
| `ohlc-live` | ohlc-service-live-container | 8092 | Historical OHLC query API (blob-backed) | [Source/HistoricaDataService/OHLCFunctionApp/README.md](EventDrivenSystem/Source/HistoricaDataService/OHLCFunctionApp/README.md) |
| `country-live` | country-service-live-container | 8093 | Daily timer: computes country/weekend/holiday state, publishes to Kafka | [Source/CountryService/ReadMe.md](EventDrivenSystem/Source/CountryService/ReadMe.md) |
| `exchange-live` | exchange-service-live-container | 8094 | 5 daily timers: exchange session state (init/pre-open/open/pre-close/close), publishes to Kafka | [Source/ExchangeService/readme.md](EventDrivenSystem/Source/ExchangeService/readme.md) |
| `aggregation-live` | aggregation-service-live-container | 8095 | Kafka-triggered: rolls 1-min candles into 5/10/15/30/60/75-min candles | [Source/AggregationService/README.md](EventDrivenSystem/Source/AggregationService/README.md) |
| `strategy-live` | strategy-service-live-container | 8096 | CRUD API over strategy JSON configs; schema only, no rule execution yet | [Source/StrategyService/README.md](EventDrivenSystem/Source/StrategyService/README.md) |
| `signalr-live` | signalr-server-live | 8098→5091 | Self-hosted SignalR hub server — real-time push to UI clients | [Source/Helpers/SignalRServer/ReadMe.md](EventDrivenSystem/Source/Helpers/SignalRServer/ReadMe.md) |
| `notification-live` | notification-service-live-container | 9098 | Kafka-triggered: writes to Redis, forwards to SignalR hubs | [Source/NotificationService/README.md](EventDrivenSystem/Source/NotificationService/README.md) |

## Kafka topic map

| Topic | Producer | Consumer(s) |
|---|---|---|
| `live-tradingview-ohlc-topic` | `dataingestion` (`TradingViewMinDataFeedFunction`) | `dataingestion` (`DataIngestionTradingViewFunction`, re-publishes onward), `notification-live` (`DataFeedNotificationFunctions`) |
| `live-tradingview-alert-topic` | `dataingestion` (alert webhook, currently disabled/commented) | `notification-live` (`AlertNotificationFunction`) |
| `live-dataingestion-ohlc-topic` | `dataingestion` (`DataIngestionTradingViewFunction`) | `aggregation-live` (`MockCandleAggregator20PeriodFunction`, `Aggregation5MinutesFunction`), `notification-live` (`DataIngestionNotificationFunctions`) |
| `live-aggregation-ohlc-5min-topic` | `aggregation-live` (`Aggregation5MinutesFunction`) | `aggregation-live` (`Aggregation10MinutesFunction`, `Aggregation15MinutesFunction`), `notification-live` (`Aggregation5MNotification`) |
| `live-aggregation-ohlc-10min-topic` | `aggregation-live` (`Aggregation10MinutesFunction`) | `notification-live` (`Aggregation10MNotification`) |
| `live-aggregation-ohlc-15min-topic` | `aggregation-live` (`Aggregation15MinutesFunction`) | `aggregation-live` (`Aggregation30MinutesFunction`, `Aggregation60MinutesFunction`, `Aggregation75MinutesFunction`), `notification-live` (`Aggregation15MNotification`) |
| `live-aggregation-ohlc-30min-topic` | `aggregation-live` (`Aggregation30MinutesFunction`) | `notification-live` (`Aggregation30MNotification`) |
| `live-aggregation-ohlc-60min-topic` | `aggregation-live` (`Aggregation60MinutesFunction`) | `notification-live` (`Aggregation60MNotification`) |
| `live-aggregation-ohlc-75min-topic` | `aggregation-live` (`Aggregation75MinutesFunction`) | `notification-live` (`Aggregation75MNotification`) |
| `live-country-workflow-topic` | `country-live` | `notification-live` (`CountryNotificationFunctions`) |
| `live-exchange-workflow-topic` | `exchange-live` | `notification-live` (`ExchangeNotificationFunctions`) |

Browse all of this live at **Kafdrop: http://localhost:9000**.

## ngrok (TradingView webhook tunnel)

`dataingestion` needs a public URL for TradingView to call. Current reserved domain: `colt-harmless-buffalo.ngrok-free.app` (free plan).

```bash
# start the tunnel (foreground)
ngrok http --domain=colt-harmless-buffalo.ngrok-free.app http://localhost:8080

# check tunnel status / inspect requests
# open http://127.0.0.1:4040 in a browser

# stop ngrok
tasklist | findstr ngrok
taskkill /IM ngrok.exe /F
```

**Webhook URL to paste into a TradingView alert:**
```
https://colt-harmless-buffalo.ngrok-free.app/api/dataingestion/tradingview/funcTradingViewDataFeed
```

TradingView alert message body:
```json
{
    "Ticker": "{{ticker}}",
    "Message": "{{ticker}} Stop Loss on Call at {{close}}",
    "EventTime": "{{timenow}}",
    "Time": "{{time}}"
}
```

There used to be an `AdminServices` Azure Function that auto-restarted ngrok on a timer/Windows Task Scheduler — it was removed as unused. If you want that automation back, start ngrok manually as above, or re-introduce a scheduled task calling the same command.

## Rebuilding an image

Every service's Dockerfile builds from its own project folder as the build context. General pattern:

```bash
cd EventDrivenSystem/Source/<ServiceFolder>
docker build -t <image-name>:<tag> -f Dockerfile .
docker-compose -f docker-compose-live.yml -p live up -d <compose-service-name>   # recreate just that one
```

Exact image name, tag, and build path per service are in each service's own README (linked in the table above).

## Known issues

1. ~~`country-live` and `exchange-live` publish to the wrong topic names.~~ **Fixed** — `ProducerTopicName` for both now includes the `live-` prefix (`live-country-workflow-topic` / `live-exchange-workflow-topic`), matching what `notification-live` subscribes to.
2. ~~`notification-live` crashed on every exchange event.~~ **Fixed** — a `System.Text.Json` (producer) vs. `Newtonsoft.Json` (consumer) property-renaming mismatch meant `ExchangeEvent.ExchangeTimerAction` silently deserialized to an invalid enum value, throwing on every message and never writing to Redis. See [NotificationService/README.md](EventDrivenSystem/Source/NotificationService/README.md#-cross-service-json-contract--a-real-bug-already-happened-here) for the full explanation and what to avoid when adding a new event type.
3. ~~The ingestion data provider was hardcoded to `"TradingView"` in Redis keys.~~ **Fixed** — `DataEventBase.DataSource` had the same JSON-contract mismatch as issue #2 (silently deserialized to enum `0` on `notification-live`), which is why the literal string was hardcoded past it instead of being read from the event. Both are now fixed; the provider is discovered dynamically end-to-end (producer → Redis key → dashboard). See [NotificationService/README.md](EventDrivenSystem/Source/NotificationService/README.md#data-provider-is-discovered-not-hardcoded).
4. ~~`DataIngestionService/DataIngestionFunctions/TradingViewFunctions.cs` didn't compile.~~ **Fixed** — found while rebuilding the image for issue #3: the file contained a dead, half-pasted second copy of the class (a constructor and `TradingViewMinDataFeedFunction` referencing `ITradingViewService`/`IKafkaProducerService`/`KafkaSettings`/`RateLimitSettings` — types that don't exist anywhere in this project, confirmed against `Program.cs`'s DI registrations) with malformed brace nesting on top. Rewritten using the same `IProducer<string, string>` pattern already used by every other file in this service.
5. **`signalr-live`'s `/pivotMarkingHub` is mapped to `StrategyHub`** instead of a dedicated hub class ([Program.cs](EventDrivenSystem/Source/Helpers/SignalRServer/Program.cs) — `app.MapHub<StrategyHub>("/pivotMarkingHub")`), so anything sent to that hub name currently lands in the strategy hub group instead.
6. **`dataingestion`'s `TradingViewAlertToSQLFunction`** (mapped at `api/dataingestion/tradingview/funcTradingViewAlertTestSQL`) writes to SQL only — there's no SQL Server in this compose stack, so calling it will fail unless one is provisioned separately.

## Folders that can likely be removed

These exist under `EventDrivenSystem/Source` but aren't referenced by `docker-compose-live.yml`, aren't built into any running image, and show signs of being scratch/test/dead code. Nothing here has been deleted — just flagged for your call:

| Folder | Why it looks removable |
|---|---|
| `GoalMonitorService` | Completely empty — no files at all |
| `Python` | Just a notebook + a committed `venv/` (should be `.gitignore`d rather than kept, at minimum) |
| `Helpers/ReceiverSignalRApp` | Blazor WASM demo client, still has unmodified default template pages (`Counter.razor`, `Weather.razor`) alongside the one real demo page |
| `TestEventAggregation` | Unmodified Azure Functions Durable-Function scaffold ("SayHello" template), name says "Test" |
| `MockStreamService` | Name says "Mock"; one of its three sub-apps (`FunctionApp1`) is still the unmodified scaffold |

Lower-confidence candidates — these have real, non-trivial code but are **not wired into the compose stack today**, so they're either future work or already superseded:

| Folder | What it is |
|---|---|
| `PivotMarkingAggregationFunction` | Zig-zag/pivot-point breakout detection — has its own copy of SharedLibrary |
| `PortfolioService` | Order/trade processing, in-memory position tracking |
| `StreamAnalyticsService` | An older Kafka candle-aggregation implementation — likely superseded by `AggregationService`, worth confirming before deleting |
| `Helpers/HelpersSolution` (`TopicToCSVConverter`) | Dev utility to dump a Kafka topic to CSV/JSON |
| `Helpers/SenderSignalRApp` | Dev utility to push test messages into SignalR |

None of the 8 live services' folders, nor `SharedLibrary`, are in either list.
