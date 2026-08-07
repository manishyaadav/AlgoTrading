# Warm-Up & Indicator Computation — Design Plan

**Status: planning stage.** Nothing here is implemented except what's explicitly marked ✅ **Shipped**. This is a living reference, not a frozen spec — update it as decisions get made, revisited, or overturned. When we pick any thread back up, start by reading the relevant section here rather than re-deriving it from scratch.

Scope: how a deployed strategy's data gets prepared before market open, and how its indicators get computed live. **Explicitly out of scope for now** (see [Parked](#4-parked--deferred-revisit-when-reached) below) — don't try to design these until we deliberately come back to them.

---

## 1. Already shipped

**`StrategyService`** (on `feature-warm-up-changes`, verified against the real deployed `second-income` strategy):
- **`config/strategies/{saved,deployed}/`** — split storage so a draft save can never silently overwrite what's actually deployed. `DeployedVersion` is computed from `deployed/` on every read, not a mutable field.
- **`GET /api/strategies/data-requirements`** — every `(Instrument, Timeframe)` a deployed strategy's rules reference, broken down by the specific `(Value, Type, Period, Multiplier, RelativePosition)` combination and which strategy needs it.
- **`GET /api/strategies/warm-up-plan`** — turns the manifest into `daysToFetch` per instrument (documented lookback assumptions, not yet verified against a real indicator engine).

See `EventDrivenSystem/Source/StrategyService/README.md` for full detail on both endpoints.

**`WarmUpService`** (new service, first cut — see `EventDrivenSystem/Source/WarmUpService/README.md`):
- Consumes `live-exchange-workflow-topic`, filters to NSE `Init` only.
- Calls `strategy-live`'s `warm-up-plan` endpoint and, for each requirement, checks whether Redis already has the corresponding indicator state — reports present/missing per requirement via structured logs.

**`ohlc-live`**: `GET /api/HistoricalSufficiency` — see section 2d below for detail. Not yet wired into `WarmUpService`'s own check.
- **Does not fetch anything yet** — every "missing" case is a clearly-labeled placeholder, blocked on `ohlc-live`'s validation capability (2d) and `AggregationService`'s calculators (2e), neither built yet. This first cut proved the orchestration (Init → plan → Redis check) works end to end; the actual fetch path is the natural next piece of section 2b below.
- Verified live via both the real Kafka-triggered path (a synthetic NSE `Init` message) and a manual HTTP trigger (`POST /api/warmup/run`, for testing without waiting for the next real `Init`).

---

## 2. Designed, not yet built

### 2a. Session lifecycle (the timing model everything else hangs off)

Driven entirely by `ExchangeEvent` on `live-exchange-workflow-topic` (currently just cached to Redis + SignalR by `NotificationService`, no other consumer reacts to it yet):

| Phase | Trigger | What happens |
|---|---|---|
| Pre-market | `Init` (09:00) | Warm-up service validates/seeds indicator state |
| Session | `Open` (09:15) → `PreClose` (15:15) | Live indicator updates + (future) rule evaluation |
| Wind-down | `PreClose` → `Close` (15:30) | Close intraday positions, etc. — **parked**, needs the execution engine first |
| EOD | `Close` onward | Sync to Azurite, cache cleanup — folded into **maintenance**, parked |

**Key policy decision**: strategy changes only take effect from the *next* day's `Init` onward — never mid-session. This isn't really "market hours" enforcement, it's "once `Init` has locked something in for today, nothing that depends on that lock-in should move until the next lock-in." See 2c.

### 2b. New service: **WarmUpService** ✅ scaffolded, steps 1-2 shipped, step 3 not yet

Reacts to NSE's `Init` only (not NFO — this whole effort is index/spot-scoped for now, futures/options explicitly out of scope). Flow:

1. ✅ Call `StrategyService`'s `GET /api/strategies/warm-up-plan` to get today's data requirement.
2. ✅ For each `(Instrument, Timeframe, Indicator, Period, Multiplier)`, **check Redis first**: most indicators (EMA, Supertrend) are continuous — they never reset overnight, so if `AggregationService` correctly kept updating them live all through the *previous* session, yesterday's final state is already the correct starting point. This makes seeding-from-history a **cold-start fallback**, not a daily routine: normal case is "confirm what's there is still good," not "recompute." Shipped as a status check (logs present/missing) — no Redis writes yet.
3. ⬜ **Not yet built**: if Redis doesn't have valid state (first run ever, a newly-deployed strategy needing a new indicator instance, Redis was flushed, or state aged out) — fall back to `ohlc-live` to pull historical data from Azurite and compute a fresh seed. Blocked on 2d and 2e existing first; currently just logs "NOT YET IMPLEMENTED" per missing requirement.
4. ⬜ **Pivot Central Range is the exception** — see 2e, it has no live phase at all, so warm-up computes it fresh every single morning, not just on cold start. Currently just logged as a placeholder, same as step 3.

### 2c. `StrategyService`: deploy-time guardrail

`StrategyFunctions.DeployStrategy` should block deploys while a session is active, so a change can't land mid-day and desync from what warm-up already locked in.

- **Block window**: `Exchange:NSE` Redis state ∈ `{Initiated, PreOpened, Opened, PreClosed}` — i.e. from `Init` through `PreClose` inclusive. Allowed again once `Closed`, or if the cached `Date` isn't today (no timer has fired yet).
- **NSE only** for now, hardcoded — not the strategy's own `Exchange` field (even though that field exists) — matches the index-only, first-cut scope.
- **Fails open**: if Redis is unreachable or the key's missing, *allow* the deploy (with a logged warning) rather than block it. This is a workflow guardrail against confusion, not a funds-safety check — a Redis hiccup shouldn't be able to lock someone out of deploying an urgent fix.
- **Server-side enforced**, in the function itself — a dashboard-only disabled button is a nicety on top, not a substitute (trivially bypassed via curl).
- Only blocks `Deploy`, not `Save` — editing/saving a draft never affects anything live, so there's no safety reason to restrict it.

### 2d. `ohlc-live`: historical-sufficiency validation ✅ **Shipped**

`GET /api/HistoricalSufficiency` — "given this instrument and N days needed, does Azurite actually have that much?" Lives in `ohlc-live` because it already owns the Azurite connection and historical-query logic. Existence-only, checked against the **monthly rollup blob** (`{year}/{month}/{blobName}.csv`) — the day-level folders alongside it hold the current month's data broken out per day, but are transient (purged once the month completes), while the monthly file is cumulative for that whole month and permanent. Confirms the day's month has a file, not that the specific day's data is inside it. Weekends skipped, no holiday-calendar awareness (kept self-contained — `ohlc-live` has no Redis dependency today). NFO contract name recomputed per day checked, since a lookback window can cross a monthly contract-roll boundary; days landing in the same month share one check (memoized). See `EventDrivenSystem/Source/HistoricaDataService/OHLCFunctionApp/README.md`.

Built as a **reusable capability**, not a private step only `WarmUpService` calls — not yet wired into `WarmUpService`'s own check (that's still 2b step 3, blocked on 2e below too) or into the other callers this should eventually have (dashboard, `StrategyService` deploy-time warning, future Backtest — see [Parked](#4-parked--deferred-revisit-when-reached)).

First version of this endpoint checked the day-level (transient) path instead of the monthly rollup — every date in a completed month read as "missing" even though the permanent record was sitting one directory up. Caught and fixed live against real data before merging. `GetOHLCByYearAndMonth`/`GetOHLCDataByDate` (pre-existing `ohlc-live` routes) were never broken — they'd always read the correct monthly path; the confusion was entirely on this new endpoint's side.

### 2e. `AggregationService`: indicator computation

**Pattern**: one calculator per indicator *type*, not per instance — e.g. `EmaCalculator`, `SupertrendCalculator` — registered by name, driven at runtime by whatever `(Instrument, Timeframe, Period, Multiplier)` instances the day's locked-in manifest says are needed. Adding a new indicator type later means writing one new calculator class; it never touches the Kafka wiring, Redis persistence, or orchestration loop. Mirrors "one file per timeframe" at the type level instead of the instance level.

**Why this can't reuse `RunningBucket` as-is**: `RunningBucket` works as one universal shape because every timeframe rollup is *the same kind* of computation (OHLCV aggregation). Indicators aren't — EMA's state and Supertrend's state share nothing structurally. So persistence needs to be per-indicator-type-defined, under a common outer envelope (key naming, TTL, generic get/set), not a shared inner schema.

#### EMA

- **State**: `{ LastEma: decimal, SeedBarsSeenSoFar: int, IsSeeded: bool }` — tiny, no candle history needed.
- **Seed** (cold-start only, via `WarmUpService` + historical data from `ohlc-live`): simple average of the first `Period` closes.
- **Update** (every live candle, in `AggregationService`):
  ```
  multiplier = 2 / (Period + 1)
  EMA_today = (Close_today - EMA_yesterday) * multiplier + EMA_yesterday
  ```
- Standard/TradingView-default formula — no open questions here.

#### Supertrend

- **Hybrid persistence** — reuses the proven `RedisHelper.PushToListAsync` (`RPUSH`+`LTRIM`) mechanism from the 20-period rolling window, *plus* a small piece of sequential state that a window alone can't provide:
  - A Redis **List** of the last `Period` candles, feeding a **plain (simple-average) ATR** over that window — deliberately *not* Wilder's recursive smoothing (TradingView's typical default), chosen to reuse existing proven infrastructure rather than build a second, different persistence pattern just for this one indicator. Tradeoff: values will differ slightly from Wilder-smoothed ATR.
  - A small derived-state piece — `{ TrendDirection: Up|Down, PrevUpperBand: decimal, PrevLowerBand: decimal }` — because the band-ratchet logic (this bar's band can only move in the trend's favor vs. *last* bar's band) is inherently sequential and can't be recomputed fresh from a window alone.
- **Seed**: cold-start only, same fallback policy as EMA.

#### Pivot Central Range

- **No live phase at all** — architecturally different from EMA/Supertrend. `AggregationService` never touches it.
- Computed **fresh every single morning** by `WarmUpService`, not just on cold start (unlike EMA/Supertrend, there's nothing to "carry forward" — it's inherently derived from *yesterday's* session, which is a different day every morning).
- Method: from the validated historical data, build one "1 Day" OHLC bar out of the prior trading session (Open/High/Low/Close), compute PCR from that.

#### Output

- **One Kafka topic per indicator type** (`live-indicator-ema-topic`, `live-indicator-supertrend-topic`, `live-indicator-pcr-topic`, …) — not one shared topic for everything, not one topic per instance. Same convention the 6 timeframe-aggregation topics already use: one topic, specific instance disambiguated by payload, not topic name.
- **Payload** carries `Instrument`, `Timeframe`, `Period`, `Multiplier`, `Value`, `WindowsStartTime` — enough for a consumer to filter to exactly the instance it cares about.
- **Kafka message key**: `{Instrument}:{Timeframe}:{Period}:{Multiplier}` (not just `{Instrument}`) — so two different instances of the same indicator on the same ticker (e.g. two different EMA periods, if ever needed) still get correct per-instance partition ordering.

---

## 3. Open questions (proposed, not yet explicitly confirmed)

- PCR being warm-up-only with zero live phase — reflected back and unchallenged, treating as agreed unless revisited.
- Supertrend's hybrid List+state design and the plain-ATR-not-Wilder's tradeoff — same, treating as agreed unless revisited.
- Output topic-per-indicator-type + the `{Instrument}:{Timeframe}:{Period}:{Multiplier}` key convention — same.

None of these block moving forward, they're just worth a final read-back before code gets written, since nothing has explicitly been signed off with a "yes" the way earlier decisions were.

---

## 4. Parked / deferred (revisit when reached)

- **Strategy execution engine** — reads a deployed strategy's `TradingRule` tree and evaluates it against live indicator values to generate entry/exit triggers. Everything in this document is preparation *for* this; it's the biggest undesigned piece. Needs its own design pass.
- **Portfolio / position management** — deferred explicitly until triggers are actually being generated. `PortfolioService` already exists in the repo (order/trade processing, in-memory position tracking) but isn't wired into compose and hasn't been read/assessed this round — check it before building anything new here.
- **Maintenance taxonomy** — daily/weekly/monthly/yearly/ad-hoc, sketched but not designed:
  - **Daily**: Azurite-completeness check (did the manual EOD upload actually complete), generate daily reports, merge into running strategy stats.
  - **Weekly**: same checks + weekly report/stats, *and* Redis cleanup — reseed-then-clean ordering matters (if Monday's reseed fails, don't leave things emptier than before the cleanup). Not a correctness fix (continuous indicators' state doesn't actually go stale over a weekend), more a hygiene practice + regularly exercising the fallback path.
  - **Monthly**: same checks + futures/options contract expiry handling + index reconstitution (stocks added/removed).
  - **Yearly**: same + refresh next year's holiday calendar (`holiday-live` needs future holidays or `CountryService`'s daily gate runs out of calendar).
  - Indicator-state staleness/TTL policy (how long before Redis state needs re-seeding regardless) — explicitly tabled pending this whole maintenance design.
- **Dashboard**: Data page "Indicators" section — currently a placeholder from earlier this session ("nothing computes these anywhere yet"). Once 2e ships, surface per-instrument historical-sufficiency status here (reusing 2d's validation capability) — this is about *visibility for humans before market open*, distinct from the dashboard's existing Data page checks, which are all about *live/today's* data flow, not historical sufficiency.
- **`StrategyService` deploy-time "insufficient history" warning** — reusing 2d's validation capability at deploy time, so a gap is caught when someone deploys, not just at `Init` the next relevant morning. Doesn't have to block the deploy — could be warn-only — that's its own decision when we get there.
- **Backtest** — placeholder dashboard page, doesn't exist yet. Will eventually need the same "do we have enough Azurite history" check as 2d, just for an arbitrary date range instead of "today minus N days." Not worth designing until Backtest itself is being built.
