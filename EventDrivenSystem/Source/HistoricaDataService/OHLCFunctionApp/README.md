# OHLC Function App

Read-only HTTP API backed by blob storage (`azurite-live`). Serves historical OHLC data — this is a **query API only**, it isn't wired into the live Kafka pipeline (that's `dataingestion` → Kafka → `aggregation-live`).

## HTTP routes

Route prefix: `api` (default).

**`GET|POST /api/GetOHLCByYearAndMonth`**

| Query param | Meaning |
|---|---|
| `year` | e.g. `2026` |
| `month` | e.g. `08` |
| `exchange` | `nse` or `nfo` |
| `instrumentName` | e.g. `nifty-50` |

```bash
curl "http://localhost:8092/api/GetOHLCByYearAndMonth?year=2026&month=08&exchange=nse&instrumentName=nifty-50"
```

**`GET|POST /api/GetOHLCDataByDate`**

| Query param | Meaning |
|---|---|
| `date` | `yyyy-mm-dd` |
| `exchange` | `nse` or `nfo` |
| `instrumentName` | e.g. `nifty-50` |

```bash
curl "http://localhost:8092/api/GetOHLCDataByDate?date=2026-08-03&exchange=nse&instrumentName=nifty-50"
```

**`GET|POST /api/DataAvailableTill`** — no params, returns the latest available date per exchange.

```bash
curl "http://localhost:8092/api/DataAvailableTill"
```

## Operations

### Compose

Service key: `ohlc-live` in [docker-compose-live.yml](../../../../docker-compose-live.yml). Container: `ohlc-service-live-container`. Host port `8092`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d ohlc-live
docker-compose -f docker-compose-live.yml -p live logs -f ohlc-live
```

### Build

```bash
cd EventDrivenSystem/Source/HistoricaDataService/OHLCFunctionApp
docker build -t ohlc-service-live-image:v1 -f Dockerfile .
```

Note: this Dockerfile uses a single-stage `dotnet publish` (no separate SDK/runtime split like the other services), and it publishes with `--nightly` isolated worker base image — check the base image tag still exists before rebuilding, since `-nightly` tags roll and can be pruned upstream.

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `AzureWebJobsStorage` | points at `azurite-live` |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `ASPNETCORE_ENVIRONMENT` | `docker` |

Data must already exist in the `azurite-live` blob container for these routes to return anything — this service only reads it.
