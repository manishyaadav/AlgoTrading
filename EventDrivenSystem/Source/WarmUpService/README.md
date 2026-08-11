# Warm-Up Service

Azure Function app. Reacts to NSE's `Init` exchange event to make sure every currently-deployed
strategy's indicator state is ready before market open — confirming what's already in Redis is
good, or cold-starting it from `ohlc-live`'s historical data when it isn't. See
[WARMUP_AND_INDICATOR_PLAN.md](../../../WARMUP_AND_INDICATOR_PLAN.md) for the full design (this
service is section 2b there).

## What it does

1. Consumes `live-exchange-workflow-topic`, ignoring everything except an `ExchangeName == "NSE"`
   event with `ExchangeTimerAction == Init`. NFO, and every other NSE action
   (`PreOpen`/`Open`/`PreClose`/`Close`), are logged and skipped — this whole effort is scoped to
   index/spot strategies for now, not futures/options.
2. Calls `strategy-live`'s `GET /api/strategies/warm-up-plan` to get the day's data-requirements
   plan (which instruments, which indicators/expressions, how many days of history each implies).
3. For each instrument, one historical fetch (not one per reason — `daysToFetch` is already the max
   any reason under that instrument needs): `ohlc-live`'s `HistoricalSufficiency` confirms the
   required trading days actually exist, then `GetOHLCByYearAndMonth` pulls the raw 1-min rows (one
   call per distinct month the lookback spans). The raw series is reused across every reason for
   that instrument, building whichever timeframe(s) each one needs.
4. Per requirement:
   - **Live-only references** (`DaysNeeded == 0`, e.g. raw `Candle High`/`Low`) — nothing to do,
     already flowing.
   - **Period-based Indicator references** (EMA, Supertrend) — checks
     `Indicator:Running:{Instrument}:{Timeframe}:{Reference}:{Period}:{Multiplier}` in Redis. If
     present, leaves it alone (continuous indicators carry forward correctly from a healthy previous
     session — re-seeding isn't the normal case). If missing, seeds it from the fetched history.
     Either way, the instance goes into today's manifest (see below).
   - **Parameterless Indicator references** (`Period == 0`, e.g. Pivot Central Range) — recomputed
     fresh every single run, unconditionally (no live phase to carry forward from — see the plan
     doc). Never added to the manifest.
   - **Expression references with a `RelativePosition`** (e.g. `"Closing Price"` at `"Previous"`) —
     still a logged placeholder; the plan doc doesn't yet define a Redis key convention for this
     case.
5. Writes `Indicator:Manifest:Active` — a JSON array of every active period-based instance
   (`Instrument`, `Ticker`, `Exchange`, `Timeframe`, `TimeframeMinutes`, `Reference`, `Period`,
   `Multiplier`), deduplicated (a strategy can reference the same instance from more than one rule —
   e.g. Supertrend(20,4) shows up in both an entry rule and an update-stop-loss rule in the deployed
   `second-income` config). `AggregationService`'s live calculators read this fresh on every candle —
   one source of truth, so warm-up's seed and the live update path can never independently disagree
   about what's active today.

## Instrument name mapping

`StrategyService`'s deployed config identifies instruments by a strategy-facing name (the real
value in `second-income`'s config is `"Nifty_Index_Spot"`), but the live pipeline (Kafka payloads,
Redis keys elsewhere, `ohlc-live`'s blob paths) only ever knows the bare ticker (`"NIFTY"`) plus
exchange. Nothing else in the codebase bridged these two vocabularies — `Common/InstrumentMapper.cs`
is a small explicit table, not string-parsing (a wrong guess here would silently seed or query the
wrong instrument with no error to catch it). Extend the table, not parsing rules, when a new
instrument shows up in a strategy config.

## Verified live

Against real Azurite history (NIFTY, 8 trading days back, 631 raw 1-min bars): seeded EMA(550) and
Supertrend(20,4) on 5-min, computed a fresh Pivot Central Range on 15-min, then fed a synthetic
completed 5-min bar onto `live-aggregation-ohlc-5min-topic` and confirmed `AggregationService`'s live
calculators picked up the exact same Redis state and moved it forward correctly.

Two real bugs found and fixed along the way, both in `ohlc-live`, not this service:
- `GetOHLCByYearAndMonth` used a throwing `DateTime.ParseExact` — one malformed pre-existing row
  (missing its seconds component) anywhere in a month crashed the whole request with no HTTP
  response ever sent, so this service just hung for 100s (the default `HttpClient` timeout) instead
  of getting a fast error. Fixed there; also gave `OhlcServiceClient` here an explicit 30s timeout
  as a second line of defense.
- The running `ohlc-live` container was a stale image predating `HistoricalSufficiency` entirely —
  rebuilding it was the first fix needed before any of this could work at all.

## Testing without waiting for the next real `Init`

`Init` only fires once a day at 09:00 IST. For everything else, there's a manual trigger that runs
the identical logic:

```bash
curl -X POST http://localhost:8100/api/warmup/run
docker-compose -f docker-compose-live.yml -p live logs -f warmup-live   # the actual per-requirement detail is in the logs, not the HTTP response
```

To simulate a real `Init` event instead (exercises the Kafka-triggered path specifically, not just the logic it calls into):

```bash
echo '{"ExchangeName":"NSE","ExchangeTimerAction":1,"Date":"2026-08-06","CimplifyType":1,"Priority":2,"ProducedAt":"2026-08-06T09:00:00","Producer":"exchange.service","Id":"22222222-2222-2222-2222-222222222222","TimeZoneId":"Asia/Kolkata","Version":"1.0"}' | docker exec -i kafka-live kafka-console-producer --bootstrap-server localhost:29092 --topic live-exchange-workflow-topic
```

(A `kafka-console-producer <<'EOF' ... EOF` heredoc was unreliable in testing — piping via `echo | docker exec -i` is what actually worked.)

## Operations

### Compose

Service key: `warmup-live` in [docker-compose-live.yml](../../../docker-compose-live.yml). Container: `warmup-service-live-container`. Host port `8100`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d warmup-live
docker-compose -f docker-compose-live.yml -p live logs -f warmup-live
```

### Build

```bash
cd EventDrivenSystem/Source/WarmUpService
docker build -t warmup-service-live-image:v1 -f Dockerfile .
docker-compose -f docker-compose-live.yml -p live up -d warmup-live   # recreate with the new image
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `AzureWebJobsStorage` | points at `azurite-live` (required by the Functions runtime even though this service doesn't use blob/queue/table storage directly) |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `ASPNETCORE_ENVIRONMENT` | `docker` |
| `TZ` | `Asia/Kolkata` |
| `KAFKA_BROKER_URL` | `kafka-live:29092` |
| `RedisConnectionString` | `redis-live:6379` |
| `StrategyServiceUrl` | `http://strategy-live` — internal container-to-container URL, no port |
| `OhlcApiBase` | `http://ohlc-live` — same internal-URL convention, matches `DashboardService`'s own `OhlcApiBase` |

### HTTP routes

| Method | Route | Does |
|---|---|---|
| POST | `/api/warmup/run` | Manually runs the same logic the `Init` Kafka trigger does — see Testing above |

### Testing Redis state directly

```bash
docker exec redis-live redis-cli HGETALL "Indicator:Running:Nifty_Index_Spot:5 Minutes:EMA:550:0"
docker exec redis-live redis-cli HGETALL "Indicator:Running:Nifty_Index_Spot:5 Minutes:Supertrend:20:4"
docker exec redis-live redis-cli LRANGE "Indicator:Window:Nifty_Index_Spot:5 Minutes:Supertrend:20:4" 0 -1
docker exec redis-live redis-cli GET "Indicator:Manifest:Active"
```
