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
| `IndicatorDispatcher{5,10,15,30,60,75}MinFunction` | that timeframe's own `live-aggregation-ohlc-{N}min-topic` | `live-indicator-ema-topic`, `live-indicator-supertrend-topic` | `live-indicator-{N}min-dispatcher` |

Anything prefixed `mock-` is for local testing against synthetic data, not the live pipeline. Watch the real flow in **Kafdrop (http://localhost:9000)**.

## Live indicator calculators

See [WARMUP_AND_INDICATOR_PLAN.md](../../../WARMUP_AND_INDICATOR_PLAN.md) section 2e for the full
design. Six thin `Indicators/IndicatorDispatcher{N}MinFunction.cs` wrappers (one per timeframe,
mirroring the "one file per timeframe" pattern above) each consume their own timeframe's completed-
bar output topic — own consumer group, so this doesn't interfere with `NotificationService`, which
already reads the same topic — and hand the bar to the shared `Indicators/IndicatorDispatcher.cs`.

The dispatcher reads `Indicator:Manifest:Active` fresh on every single candle (a plain Redis `GET`,
deliberately no in-process caching — `WarmUpService` only rewrites this once a day at NSE `Init`, so
there's no cache-invalidation question to solve), filters to whatever's active for this
`(Ticker, TimeframeMinutes)`, and dispatches each match by `Reference` to `Indicators/EmaCalculator.cs`
or `Indicators/SupertrendCalculator.cs`. Both calculators are a pure "read Redis Hash (+List for
Supertrend), compute exactly one step, write it back" — no in-memory state carried between
invocations, same restart-safety property `RunningBucket` already has above. Either returns `null`
(a genuine no-op) if the instance isn't seeded yet — a live candle alone can never seed an indicator,
only `WarmUpService`'s cold-start path (pulling history from `ohlc-live`) can.

A successful update publishes to `live-indicator-ema-topic`/`live-indicator-supertrend-topic`, Kafka
key `{Instrument}:{Timeframe}:{Period}:{Multiplier}` (not just `{Instrument}`, so two instances of
the same indicator on the same ticker still get correct per-instance partition ordering).

Verified live: seeded EMA(550) and Supertrend(20,4) on 5-min for NIFTY from real historical data via
`WarmUpService`, then fed one synthetic completed 5-min bar onto `live-aggregation-ohlc-5min-topic`
and confirmed both `Indicator:Running:*` Hashes advanced correctly (EMA shifted a small,
correctly-weighted amount; Supertrend's True-Range window stayed trimmed to 20 entries, its band
ratchet held), and both output topics received a correctly-shaped message.

⚠️ Supertrend's band-ratchet math hasn't been cross-checked against a reference implementation with
real data — see the plan doc's note under Supertrend for detail. Verified here only for internal
consistency (seed→live continuity, correct True Range math), not numerical correctness.

## Windowing & restart-safety

Each timeframe function buffers incoming lower-timeframe candles until it has enough of them to emit one aggregated candle (5 × 1-min → 5-min, 2 × 5-min → 10-min, 3 × 5-min → 15-min, 2 × 15-min → 30-min, 4 × 15-min → 60-min, 5 × 15-min → 75-min), then resets and starts the next bucket. **All six** now persist their in-progress bucket to Redis instead of a `static` in-memory field — this used to be scoped to 5-min only while the approach was being validated; it's since been extended to the rest.

Previously, that state — and the KafkaTrigger consumer group had already committed the offsets for the candles making it up, since the function returns successfully once a candle is buffered — lived only in process memory: any redeploy or crash mid-bucket permanently dropped whatever had accumulated so far, and the consumer wouldn't naturally replay those messages to recover it (Kafka retention isn't the issue — the committed offset is). The fix (`RunningBucket` in `Common/RunningBucket.cs`, backed by a Redis Hash at `Aggregation:Bucket:{Ticker}:{Timeframe}min`), and the exact field-update rule:

- **First candle to land for a fresh bucket**: `Open`, `High`, `Low`, `Close`, `VolumeSum` are all seeded straight from it, `Count = 1`.
- **Every candle after that, in the same bucket**: `High = max(High, candle.High)`, `Low = min(Low, candle.Low)`, `Close = candle.Close` (always moves to the latest), `VolumeSum += candle.Volume`, `Count += 1`. `Open` never changes again.
- **Bucket completion is `Count`-based**, not wall-clock-based like the old `_bucket`/`_bufferData` mechanism was (which compared `DateTime.Now`-derived bucket boundaries across messages) — once `Count` reaches the number of source candles the timeframe needs (5 one-min candles for a 5-min bucket; 2/3/2/4/5 lower-timeframe candles for 10/15/30/60/75-min respectively), that candle is treated as the bucket's last: the aggregated OHLCV is published and the Redis hash is deleted so the next candle starts a clean bucket. This also sidesteps the old approach's dependency on Kafka-processing wall-clock time lining up with the data's own timestamps (a real gap during any consumer lag/backlog catch-up).
- A restart just re-reads the hash (`RunningBucket.FromHash`) and resumes the bucket exactly where it left off — no data loss for anything that had already made it into the hash before the crash. The one residual gap: a crash between "candle buffered" and "hash written" can still drop at most one candle, since the two aren't one atomic operation.
- **`BucketStart`/`BucketEnd` are floor-aligned to bucket boundaries anchored to market open** (`RunningBucket.FloorToBucketStart`, anchored to 9:15 IST on the candle's own trading day) — not the top of the wall-clock hour, and not simply "whatever timestamp the first arriving candle happens to carry". The market-open anchor matters because most of these timeframes don't divide evenly into an hour: flooring a 10-min bucket against the hour would land 9:15 on 9:10 instead of the conventional 9:15-9:25 candle sequence a 5-min source naturally produces, and 60/75-min don't relate to the hour at all. Anchoring to 9:15 instead keeps every timeframe's bucket marks matching the sequence its lower-timeframe source actually produces (9:15, 9:25, 9:35... for 10-min; 9:15, 10:30, 11:45... for 75-min), and — the restart-safety payoff — keeps a restart's first-arriving candle landing in the same bucket it would have without the restart. Comparing the incoming candle's floored start against the stored bucket's start (rather than a plain date check) is also what decides whether a candle belongs to the in-progress bucket or starts a new one, which subsumes a "different day" guard — a new day trivially floors to a different bucket start too. The Hash also carries a 24h TTL as a secondary safety net, though this comparison is what actually prevents a stale bucket from being reused.
- **`BucketStart`/`BucketEnd` are IST throughout** — in memory, in the Redis Hash (e.g. `2026-08-05T11:45:00`, no `Z`), and in the published `TimeFrameAggregationEvent.WindowsStartTime`. This wasn't always true: an earlier version of this fix only converted the Redis Hash's display, leaving the in-memory value and the published event in UTC — which meant the aggregated candles landing in Kafka still showed a raw UTC `WindowsStartTime` (e.g. `06:08:00Z`) that read as "wrong" next to everything else IST. That's since been superseded by converting once at the true root, in `DataIngestionService` — see [its README](../DataIngestionService/ReadMe.md#where-windowsstarttime-becomes-ist) — so every stage in this service (including `RunningBucket`) now just carries an already-IST value forward with no conversion of its own. `FloorToBucketStart` and `ToHash`/`FromHash` do no UTC↔IST conversion at all anymore; they operate purely on IST wall-clock values.

**`MockCandleAggregator20PeriodFunction`** (despite the name, wired into the live pipeline — consumes `live-dataingestion-ohlc-topic`, produces `live-candle-stats-1min-20period-topic`) gets the same restart-safety treatment but with a different Redis shape: it's a **rolling** box-plot/candle-classification window over the trailing 20 1-min candles, not a fixed bucket that resets on completion, so it needs the actual last-20 raw candles (for percentile/quartile math), not just a running aggregate. Persisted as a Redis **List** at `Aggregation:Window:{Ticker}:{Timeframe}min:{Period}period` — `RedisHelper.PushToListAsync` does `RPUSH` + `LTRIM` to the last 20 + refreshes a 24h TTL on every candle, so the list is always capped at 20 without needing to track its length separately. Once it reaches 20, stats recompute on every subsequent candle (matching the old in-memory version's behavior, which similarly recomputed continuously once its buffer first filled — it just didn't call it "resetting," it called it `RemoveAt(0)`). No day-boundary guard here — a window that happens to be running when the market reopens will naturally blend a few of yesterday's trailing candles with today's until 20 fresh ones accumulate, which is also what the old in-memory version did on any day boundary crossed without a restart.

### Host-timezone-independent timestamps

Every "current time" calculation in this service now goes through `DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow)` (or the equivalent in `RunningBucket.FloorToBucketStart`) rather than `DateTime.Now`. `DateTime.Now` only returns IST today because the container's `TZ=Asia/Kolkata` env var happens to be set — if that assumption ever breaks (a different host, a cloud platform that resets or ignores container `TZ`), every bucket-alignment and day-scoping calculation would silently start using whatever timezone the host actually has, with no error to signal it. `DateTime.UtcNow` is always correct regardless of host timezone configuration; converting explicitly from there removes the dependency entirely. The same fix was applied to `NotificationService` (its two `Ingestion:Count`/`Aggregation:Count` date-keyed Redis keys) and `DashboardService` (an `IstNow()` helper added to `Program.cs`, used everywhere it computes "today" or elapsed session time) — see those services' READMEs.

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
| `RedisConnectionString` | `redis-live:6379` — used by every `Aggregation{N}MinutesFunction`'s running-bucket persistence and `MockCandleAggregator20PeriodFunction`'s rolling-window persistence |

### Testing

There's no HTTP endpoint to poke directly — feed `live-dataingestion-ohlc-topic` (e.g. by calling `dataingestion`'s webhook route, see its README) and watch downstream topics fill up in Kafdrop.

To manually verify restart/crash recovery for any timeframe's bucket:

```bash
docker exec redis-live redis-cli HGETALL "Aggregation:Bucket:NIFTY:5min"    # or :10min, :15min, :30min, :60min, :75min
docker-compose -f docker-compose-live.yml -p live restart aggregation-live # simulate a mid-bucket restart
# then re-check the hash above — Count should resume from where it was, not reset to 0/1
```

Same idea for the 20-period rolling window, checking length instead of a Count field:

```bash
docker exec redis-live redis-cli LLEN "Aggregation:Window:NIFTY:1min:20period"
```
