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

> **Blob layout has two tiers, and both routes above correctly use the permanent one.** For the
> current month, day-level folders (`{year}/{month}/{day}/{blobName}.csv`) hold that day's data —
> but they're transient, purged once the month completes. The same-named file directly under the
> month folder (`{year}/{month}/{blobName}.csv`, no day segment — what these two routes and
> `HistoricalSufficiency` below all read) is cumulative for the whole month and is the permanent
> record, kept forever. `DataAvailableTill` (below) walks the day-level folders, so it only ever
> reflects the last few days, not full history — don't use it to conclude older data is missing.

**`GET|POST /api/DataAvailableTill`** — no params, returns the latest available date per exchange
by walking the current month's day-level folders. Since those are transient (see above), this
tells you how fresh things are, not how much history exists — that's `HistoricalSufficiency` below.

```bash
curl "http://localhost:8092/api/DataAvailableTill"
```

**`GET|POST /api/HistoricalSufficiency`** — "does Azurite have enough history for this instrument?"
(`WARMUP_AND_INDICATOR_PLAN.md` section 2d). Existence-only, checked against the monthly rollup
blob (see above) — confirms the month's file exists, not that the specific required day's data is
inside it (bar/date-level completeness within a month would be a separate, deeper check). Walks
backward from the day before `asOf` (default: today, IST), skipping weekends only — no
holiday-calendar awareness, since this service has no Redis dependency and adding one just for
this would be new coupling; a holiday just shows up as "missing," which is technically true from
Azurite's side.

| Query param | Meaning |
|---|---|
| `exchange` | `nse` or `nfo` |
| `instrumentName` | e.g. `nifty-50`, `BANKNIFTY` |
| `daysNeeded` | how many trading days of history are required |
| `asOf` | optional, `yyyy-MM-dd`, defaults to today (IST) |

```bash
curl "http://localhost:8092/api/HistoricalSufficiency?exchange=nse&instrumentName=nifty-50&daysNeeded=20"
curl "http://localhost:8092/api/HistoricalSufficiency?exchange=nfo&instrumentName=BANKNIFTY&daysNeeded=20&asOf=2026-08-07"
```

For NFO, the contract name is recomputed **per day checked**, not once for `asOf` — a futures
contract name is month-specific (`BANKNIFTY26AUGFUT` for August), so a lookback window crossing a
month boundary needs each day's own contract name or the earlier month's days would be silently
checked against the wrong (current month's) contract and always read as missing. Days that land in
the same month resolve to the same blob and are checked once, not once per day.

Designed as a reusable capability, not a private step of `WarmUpService` — a `StrategyService`
deploy-time "insufficient history" warning and `Backtest` will want the same question answered
later (see the plan doc's Parked section).

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
