# OHLC Function App

Blob storage (`azurite-live`) backend for historical OHLC data — an HTTP query API (below), plus one
Kafka consumer that keeps Azurite in sync with the live 1-min feed instead of relying on a manual
after-hours upload (see **Kafka consumers**, further down).

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

Row parsing is resilient — a malformed row (bad date or number) is skipped and logged, not thrown.
This wasn't always true: it used a throwing `DateTime.ParseExact` with no format fallback, so one
real pre-existing row missing its seconds component (`"03-08-2026 09:15"` instead of
`"...09:15:00"`) crashed the *entire* request with an unhandled exception — which meant no HTTP
response was ever sent at all, so callers just hung until their own client-side timeout instead of
getting a fast error. Found live while building `WarmUpService`'s historical fetch path (see
[WARMUP_AND_INDICATOR_PLAN.md](../../../../WARMUP_AND_INDICATOR_PLAN.md) section 2b). Fixed by
wiring in this file's own `ParseDate` helper (multi-format `TryParseExact`, already present but
never actually called), wrapping the whole row in a try/catch, and adding the no-seconds format so
those rows get recovered instead of just skipped.

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


## Kafka consumers

**`LiveCandlePersistenceFunction`** — consumes `live-dataingestion-ohlc-topic` (own consumer group
`live-ohlc-azurite-persistence-consumer`, so it doesn't interfere with the 5-min aggregator or
`NotificationService`, which already read the same topic) and appends every 1-min candle to
Azurite, in the exact same shape the HTTP routes above read: header
`Date,Open,Low,High,Close,Volume`, date as `dd-MM-yyyy HH:mm:ss`, path
`{basePath}/{year}/{month}/{blobName}.csv` (see `BlobPathHelper.cs`) — always the candle's own
month/contract, resolved the same way `GetOHLCByYearAndMonth` resolves it.

The live feed's ticker (`"NIFTY"`, `"BANKNIFTY"` — see `NotificationService`'s
`DataIngestion:TradingView:*` Redis keys) carries no exchange/instrument-type marker; it represents
**both** the NSE spot index and the NFO front-month future, so every candle is written to **both**
blob paths, not just one.

Being a brand-new consumer group, its first run reads from Kafka's earliest retained offset for
this topic — not just new candles going forward — so it backfills Azurite with however much
history the topic still has retained, then settles into real-time as it catches up. Confirmed live:
processed a multi-day backlog and landed exactly on today's most recent candle within about 30
seconds.

### Write strategy — configurable, not hardcoded

Azurite blobs are **Block Blobs** (not Append Blobs — checked directly against real blob metadata
from this environment), so "append a row" has two honest options, chosen via the
`BLOB_APPEND_STRATEGY` env var:

| Value | Strategy | When |
|---|---|---|
| `Simple` (default) | Download the whole existing file, append the row, re-upload. | Local/Azurite — files are small (~650KB/month at current volume) and this is all localhost, so the redundant transfer costs nothing in practice. **This is the strategy actually exercised end-to-end.** |
| `BlockList` | Stage the new row as its own block, commit an updated block list — never re-transfers existing content. | Meant for real Azure, where re-uploading a growing file on every 1-min candle would be a genuine, recurring cost. **⚠️ Not independently verified against real Azure from this environment** — only Azurite is available here, and it may not enforce every real-Azure constraint (e.g. same-length block IDs within one commit) with full strictness. Test carefully against a real storage account before switching this on in production. |

Both strategies are **ETag-conditional with retry**: read-or-check the blob's current ETag, write
conditionally (`IfMatch`/`IfNoneMatch`), and on a 412 conflict (another candle landed on the same
blob in between), retry from a fresh read. Without this, two candles racing on the same blob could
silently lose a row — one write's read-modify-write cycle overwriting the other's.
=======
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
| `KAFKA_BROKER_URL` | `kafka-live:29092` — needed by `LiveCandlePersistenceFunction` |
| `BLOB_APPEND_STRATEGY` | `Simple` or `BlockList` — see **Kafka consumers** above |

The HTTP routes are read-only against whatever's already in `azurite-live`. `LiveCandlePersistenceFunction` is the write path — it's what keeps that data current now, instead of a manual after-hours upload.
