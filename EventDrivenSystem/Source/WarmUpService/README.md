# Warm-Up Service

Azure Function app. Reacts to NSE's `Init` exchange event to check whether Redis already has what every currently-deployed strategy's indicators need, ahead of market open. **No cleanup of a previously-deployed version's *effects* happens here** — this is a status-check service today, not yet a data-fetching one. See [WARMUP_AND_INDICATOR_PLAN.md](../../../WARMUP_AND_INDICATOR_PLAN.md) for the full design (this service is section 2b there) and what's still to be built.

## What it does today

1. Consumes `live-exchange-workflow-topic`, ignoring everything except an `ExchangeName == "NSE"` event with `ExchangeTimerAction == Init`. NFO, and every other NSE action (`PreOpen`/`Open`/`PreClose`/`Close`), are logged and skipped — this whole effort is scoped to index/spot strategies for now, not futures/options.
2. Calls `strategy-live`'s `GET /api/strategies/warm-up-plan` to get the day's data-requirements plan (which instruments, which indicators/expressions, how many days of history each implies).
3. For each requirement, reports (via structured logs — no Redis writes yet):
   - **Live-only references** (`DaysNeeded == 0`, e.g. raw `Candle High`/`Low`) — nothing to do, already flowing.
   - **Period-based Indicator references** (EMA, Supertrend) — checks `Indicator:Running:{Instrument}:{Timeframe}:{Reference}:{Period}:{Multiplier}` in Redis; reports present or missing.
   - **Parameterless Indicator references** (`Period == 0`, e.g. Pivot Central Range) — always reported as needing fresh daily computation, since this indicator shape has no live phase to check against (see plan doc section 2e).
   - **Expression references with a `RelativePosition`** (e.g. `"Closing Price"` at `"Previous"`) — reported as needing a historical value lookup.

**What it doesn't do yet**: actually fetch anything. Every "missing" or "needs computation" case is currently a logged, clearly-labeled placeholder ("NOT YET IMPLEMENTED") — the real fallback (pulling from `ohlc-live`/Azurite and writing a seed to Redis) needs `ohlc-live`'s new historical-sufficiency validation capability (plan section 2d) and `AggregationService`'s indicator calculators (plan section 2e) to exist first. This service's first job was proving the orchestration — Init trigger → plan → Redis check — works end to end; the fetch path is the natural next piece.

## Testing without waiting for the next real `Init`

`Init` only fires once a day at 09:00 IST. For everything else, there's a manual trigger that runs the identical logic:

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
| `StrategyServiceUrl` | `http://strategy-live` — internal container-to-container URL, no port (matches every other internal service-to-service URL in this stack, e.g. `DashboardService`'s `OhlcApiBase`) |

### HTTP routes

| Method | Route | Does |
|---|---|---|
| POST | `/api/warmup/run` | Manually runs the same logic the `Init` Kafka trigger does — see Testing above |

### Testing Redis state directly

```bash
docker exec redis-live redis-cli EXISTS "Indicator:Running:Nifty_Index_Spot:5 Minutes:EMA:550:0"
```
