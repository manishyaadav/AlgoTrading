# Notification Service

Kafka-triggered Azure Function. Sits at the end of the pipeline: consumes domain events from Kafka, updates a Redis cache, and forwards a notification to the matching SignalR hub on `signalr-live`. **No HTTP routes** — the container's port only serves the Functions host default page.

## Kafka topics → SignalR hubs

| Function | Consumes topic | Redis key pattern | SignalR hub |
|---|---|---|---|
| `DataFeedNotificationFunctions` | `live-tradingview-ohlc-topic` | `DataFeed:...` | `/datafeedHub` |
| `DataIngestionNotificationFunctions` | `live-dataingestion-ohlc-topic` | `DataIngestion:TradingView:{Ticker}` | `/dataIngestionHub` |
| `AlertNotificationFunction` | `live-tradingview-alert-topic` | — | `/alertIngestionHub` |
| `CountryNotificationFunctions` | `live-country-workflow-topic` | — | `/countryHub` |
| `ExchangeNotificationFunctions` | `live-exchange-workflow-topic` | — | `/exchangeHub` |
| `DataAggregationNotificationFunctions` | `live-aggregation-ohlc-5min-topic`, `-10min-`, `-15min-`, `-30min-`, `-60min-`, `-75min-` | — | `/aggregationHub` |

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
```
