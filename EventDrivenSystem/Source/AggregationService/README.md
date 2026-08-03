# Aggregation Service

Kafka-triggered Azure Function. Rolls 1-minute OHLC candles (from `dataingestion`) up into 5/10/15/30/60/75-minute candles, and does swing/pivot marking analysis. **No HTTP routes** — the container's port only serves the Functions host default page.

## Kafka topics

| Function | Consumes | Produces | Consumer group |
|---|---|---|---|
| `MockCandleAggregator20PeriodFunction` | `live-dataingestion-ohlc-topic` | (candle aggregation, republished internally) | `live-candle-1min-20period-aggregator` |
| `Aggregation5MinutesFunction` | `live-dataingestion-ohlc-topic` | `live-aggregation-ohlc-5min-topic` | `live-dataingestion-5min-aggregator` |
| `Aggregation10MinutesFunction` | `live-aggregation-ohlc-5min-topic` | `live-aggregation-ohlc-10min-topic` | `live-aggregator-10min-consumer` |
| `Aggregation15MinutesFunction` | `live-aggregation-ohlc-5min-topic` | `live-aggregation-ohlc-15min-topic` | `live-aggregator-15min-consumer` |
| `Aggregation30MinutesFunction` | `live-aggregation-ohlc-15min-topic` | `live-aggregation-ohlc-30min-topic` | `live-aggregator-30min-consumer` |
| `Aggregation60MinutesFunction` | `live-aggregation-ohlc-15min-topic` | `live-aggregation-ohlc-60min-topic` | `live-aggregator-60min-consumer` |
| `Aggregation75MinutesFunction` | `live-aggregation-ohlc-15min-topic` | `live-aggregation-ohlc-75min-topic` | `live-aggregator-75min-consumer` |
| `PriceObservsorForMarkingFunction` | `mock--candle-aggregation-5min-20period-topic` | swing/pivot marking output | `mock-candle-5min-price-for-swing-observor` |
| `MockAggregation5MinutesFunction` | `mock-dataingestion-ohlc-topic` | — | `mock-dataingestion-5min-aggregator` |

Anything prefixed `mock-` is for local testing against synthetic data, not the live pipeline. Watch the real flow in **Kafdrop (http://localhost:9000)**.

## Operations

### Compose

Service key: `aggregation-live` in [docker-compose-live.yml](../../../docker-compose-live.yml). Container: `aggregation-service-live-container`. Host port `8095`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d aggregation-live
docker-compose -f docker-compose-live.yml -p live logs -f aggregation-live
```

### Build

```bash
cd EventDrivenSystem/Source/AggregationService
docker build -t aggregation-service-live-image:v1 -f Dockerfile .
docker-compose -f docker-compose-live.yml -p live up -d aggregation-live   # recreate with the new image
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `AzureWebJobsStorage` | points at `azurite-live` |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `ASPNETCORE_ENVIRONMENT` | `docker` |
| `KAFKA_BROKER_URL` | `kafka-live:29092` |

### Testing

There's no HTTP endpoint to poke directly — feed `live-dataingestion-ohlc-topic` (e.g. by calling `dataingestion`'s webhook route, see its README) and watch downstream topics fill up in Kafdrop.
