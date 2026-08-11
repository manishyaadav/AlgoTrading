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

**`WarmUpService`** — see `EventDrivenSystem/Source/WarmUpService/README.md`:
- Consumes `live-exchange-workflow-topic`, filters to NSE `Init` only.
- Calls `strategy-live`'s `warm-up-plan` endpoint and, for each requirement, checks whether Redis already has the corresponding indicator state.
- **Cold-start seeding (2b step 3) ✅ shipped**: when state is missing, pulls raw 1-min history from `ohlc-live` (via `HistoricalSufficiency` + `GetOHLCByYearAndMonth`), reconstructs the required timeframe from it (session-open-aligned, identical to `AggregationService`'s own bucketing), and seeds EMA/Supertrend/Pivot Central Range from that. Writes a manifest (`Indicator:Manifest:Active`) of every active period-based instance for `AggregationService`'s live calculators to read. See section 2b below for detail.
- Verified live via both the real Kafka-triggered path (a synthetic NSE `Init` message) and a manual HTTP trigger (`POST /api/warmup/run`, for testing without waiting for the next real `Init`).

**`ohlc-live`**: `GET /api/HistoricalSufficiency` — see section 2d below for detail. Now wired into `WarmUpService`'s cold-start fetch, as intended.

**`AggregationService`**: EMA and Supertrend live calculators (2e) — see that section below for detail. Pivot Central Range has no live phase by design; `WarmUpService` is its only computation point.

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

### 2b. New service: **WarmUpService** ✅ shipped (steps 1-4)

Reacts to NSE's `Init` only (not NFO — this whole effort is index/spot-scoped for now, futures/options explicitly out of scope). Flow:

1. ✅ Call `StrategyService`'s `GET /api/strategies/warm-up-plan` to get today's data requirement.
2. ✅ For each `(Instrument, Timeframe, Indicator, Period, Multiplier)`, **check Redis first**: most indicators (EMA, Supertrend) are continuous — they never reset overnight, so if `AggregationService` correctly kept updating them live all through the *previous* session, yesterday's final state is already the correct starting point. This makes seeding-from-history a **cold-start fallback**, not a daily routine: normal case is "confirm what's there is still good," not "recompute."
3. ✅ **Cold-start seed**: if Redis doesn't have valid state, one historical fetch per instrument via `ohlc-live` (`HistoricalSufficiency` to confirm the days exist, then `GetOHLCByYearAndMonth` — one call per distinct month the lookback spans, not per day) reconstructs the raw 1-min history, which gets grouped into whatever timeframe(s) each reason needs (same session-open-anchored bucketing `AggregationService`'s `RunningBucket.FloorToBucketStart` uses live, duplicated here — see `Common/TimeframeBuilder.cs` — so a seeded bar lines up bucket-for-bucket with what the live pipeline would have produced). EMA/Supertrend get seeded from that; see 2e for the exact math.
4. ✅ **Pivot Central Range** — no live phase at all (see 2e), computed fresh every single `Init`, unconditionally, from the prior trading session's one "1 Day" OHLC bar.
5. ✅ Every active period-based instance (freshly seeded *or* already-present) gets written to `Indicator:Manifest:Active` (JSON array, 7-day TTL, rewritten whole on every `Init`) — the single source of truth `AggregationService`'s live calculators read, so warm-up's seed and the live update path can never independently disagree about which instances exist for today. Carries both the strategy-facing instrument name (used in the `Indicator:Running:*` key) and the live-pipeline ticker (used to match an incoming candle), via a small explicit `Common/InstrumentMapper.cs` table — nothing else in the codebase bridged those two vocabularies (StrategyService's deployed config literally says `"Instrument": "Nifty_Index_Spot"`; everywhere else it's `"NIFTY"`).

Verified live end-to-end against real Azurite history (NIFTY, 8 trading days back): seeded EMA(550) and Supertrend(20,4) on 5-min from 631 raw 1-min bars, computed a fresh Pivot Central Range, then fed a synthetic completed 5-min bar onto `live-aggregation-ohlc-5min-topic` and confirmed `AggregationService`'s live calculators (2e) picked up the exact same Redis state and moved it forward correctly (EMA shifted a small, correctly-weighted amount; Supertrend's True-Range window stayed trimmed to 20; band ratchet held per the formula).

Two real bugs found and fixed live during this: `ohlc-live`'s `GetOHLCByYearAndMonth` used a throwing `DateTime.ParseExact` — one malformed pre-existing row (missing its seconds component) anywhere in a month crashed the whole request with no HTTP response ever sent, so callers just hung until their own client timeout rather than getting a fast error (fixed: wired in the file's own already-present-but-unused resilient `ParseDate` helper, wrapped the whole row in a try/catch, and recovered the malformed rows instead of just skipping them). And the running `ohlc-live` container was a stale image predating `HistoricalSufficiency` (2d) entirely — rebuilding it was the first fix needed before any of this could work.

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

### 2e. `AggregationService`: indicator computation ✅ shipped (EMA, Supertrend)

**Pattern**: one calculator per indicator *type*, not per instance — `Indicators/EmaCalculator.cs`, `Indicators/SupertrendCalculator.cs` — driven at runtime by whatever `(Instrument, Timeframe, Period, Multiplier)` instances `Indicator:Manifest:Active` says are needed (see 2b step 5 — WarmUpService writes it, this only ever reads it, fresh on every candle, deliberately no in-process caching). Adding a new indicator type later means writing one new calculator class plus one new `if` branch in `Indicators/IndicatorDispatcher.cs`; it never touches the Kafka wiring. Kafka wiring itself mirrors "one file per timeframe" at the type level: 6 thin `IndicatorDispatcher{5,10,15,30,60,75}MinFunction.cs` wrappers, each consuming that timeframe's own completed-bar output topic (`live-aggregation-ohlc-{N}min-topic` — the same topic `NotificationService` already reads, own consumer group) and handing the bar to the shared dispatcher.

Both calculators are a pure "read Hash (+List for Supertrend), compute one step, write Hash (+List) back" — no in-memory state, so a container restart never loses anything mid-day the way the pre-`RunningBucket` OHLCV rollup used to. Returns `null` (a genuine no-op, not an error) if the instance isn't seeded yet — a live candle alone can never seed either indicator, only `WarmUpService`'s cold-start path can.

**Why this can't reuse `RunningBucket` as-is**: `RunningBucket` works as one universal shape because every timeframe rollup is *the same kind* of computation (OHLCV aggregation). Indicators aren't — EMA's state and Supertrend's state share nothing structurally. So persistence needs to be per-indicator-type-defined, under a common outer envelope (key naming, TTL, generic get/set), not a shared inner schema.

#### EMA

- **State**: `{ LastEma: decimal, SeedBarsSeenSoFar: int, IsSeeded: bool, LastBarWindowsStartTime }` — tiny, no candle history needed. Redis Hash at `Indicator:Running:{Instrument}:{Timeframe}:EMA:{Period}:{Multiplier}`.
- **Seed** (cold-start only, via `WarmUpService` + historical data from `ohlc-live`): simple average of the first `Period` closes, then the recursive formula runs forward through every remaining historical bar — so the seeded state reflects the most recent available bar (yesterday's close), not one stale from the start of the fetch window.
- **Update** (every live candle, in `AggregationService`):
  ```
  multiplier = 2 / (Period + 1)
  EMA_today = (Close_today - EMA_yesterday) * multiplier + EMA_yesterday
  ```
- Standard/TradingView-default formula — no open questions here. Verified live: seeded from 631 real 1-min bars (8 trading days of NIFTY), then correctly advanced on a live candle.

#### Supertrend

- **Hybrid persistence** — reuses the proven `RedisHelper.PushToListAsync` (`RPUSH`+`LTRIM`) mechanism from the 20-period rolling window, *plus* a small piece of sequential state that a window alone can't provide:
  - A Redis **List** at `Indicator:Window:{Instrument}:{Timeframe}:Supertrend:{Period}:{Multiplier}` holding the last `Period` bars' True Range (each entry also carries `High`/`Low`/`Close` for debugging), feeding a **plain (simple-average) ATR** over that window — deliberately *not* Wilder's recursive smoothing (TradingView's typical default), chosen to reuse existing proven infrastructure rather than build a second, different persistence pattern just for this one indicator. Tradeoff: values will differ slightly from Wilder-smoothed ATR.
  - A small derived-state Hash at `Indicator:Running:{Instrument}:{Timeframe}:Supertrend:{Period}:{Multiplier}` — `{ TrendDirection: Up|Down, PrevUpperBand: decimal, PrevLowerBand: decimal, PrevClose: decimal, Atr: decimal, IsSeeded: bool, LastBarWindowsStartTime }` — because the band-ratchet logic (this bar's band can only move in the trend's favor vs. *last* bar's band) is inherently sequential and can't be recomputed fresh from a window alone. `PrevClose` is carried specifically so the *next* bar's True Range can be computed without re-reading the window.
- **Seed**: cold-start only, same fallback policy as EMA — see `WarmUpService/Indicators/SupertrendSeeder.cs`.
- ⚠️ **Not independently cross-checked against a reference implementation** (e.g. real TradingView output on the same data) — the band-ratchet formula follows the commonly-published version (the same one Pine Script's built-in indicator uses), but Supertrend has several slightly different popular variants, and a subtle indexing mistake wouldn't throw, it would just produce a plausible-looking but wrong value. Verified live only for internal consistency (seed → live update continuity, correct True Range math, correct band-ratchet behavior on one synthetic bar) — not for numerical correctness against a known-good source. Do that before trusting this for actual trading decisions.

#### Pivot Central Range

- **No live phase at all** — architecturally different from EMA/Supertrend. `AggregationService` never touches it; never appears in `Indicator:Manifest:Active` either (`WarmUpService` deliberately excludes it from the manifest it writes for the live dispatcher).
- Computed **fresh every single morning** by `WarmUpService`, not just on cold start (unlike EMA/Supertrend, there's nothing to "carry forward" — it's inherently derived from *yesterday's* session, which is a different day every morning). Redis Hash at `Indicator:Running:{Instrument}:{Timeframe}:Pivot Central Range:0:0` (`Period`/`Multiplier` both 0, matching the actual manifest values for this indicator shape).
- Method: from the validated historical data, build one "1 Day" OHLC bar out of the prior trading session (Open/High/Low/Close), compute `Pivot = (H+L+C)/3`, `BottomCentral = (H+L)/2`, `TopCentral = 2*Pivot - BottomCentral`, `Width = TopCentral - BottomCentral` — `Width` is the value the deployed strategy's own rule actually compares against a percentage of closing price, i.e. a narrow/wide-range-day gauge, not a price level.

#### Output

- **One Kafka topic per indicator type** (`live-indicator-ema-topic`, `live-indicator-supertrend-topic` — no PCR topic, it has no live phase to publish from) — not one shared topic for everything, not one topic per instance. Same convention the 6 timeframe-aggregation topics already use: one topic, specific instance disambiguated by payload, not topic name.
- **Payload** carries `Instrument`, `Ticker`, `Timeframe`, `TimeframeMinutes`, `Reference`, `Period`, `Multiplier`, `Value`, `Direction` (`null` for EMA; `"Up"`/`"Down"` for Supertrend — what the strategy rule's `== "GREEN"`/`"RED"` comparison will eventually need, translation is the parked execution engine's job), `WindowsStartTime`.
- **Kafka message key**: `{Instrument}:{Timeframe}:{Period}:{Multiplier}` (not just `{Instrument}`) — so two different instances of the same indicator on the same ticker (e.g. two different EMA periods, if ever needed) still get correct per-instance partition ordering.
- Verified live: both topics received a correctly-shaped message after a synthetic completed 5-min bar.

---

## 3. Open questions

All three of the below are now implemented (section 2e), not just designed — listed here for what's still genuinely unresolved about each, not as "proposed, unconfirmed" design sketches anymore:

- **Instrument-name mapping** (new, found while implementing 2b step 3 — not anticipated when this doc was first written): `StrategyService`'s deployed config identifies instruments by a strategy-facing name (`"Nifty_Index_Spot"`), but the live pipeline (Kafka, Redis, `ohlc-live`) only ever knows the bare ticker (`"NIFTY"`) + exchange. Resolved with a small explicit table (`WarmUpService/Common/InstrumentMapper.cs`) rather than string-parsing — extend the table, not parsing rules, when a new instrument shows up in a strategy config.
- **Supertrend's numerical correctness** — implemented per the commonly-published band-ratchet formula, verified live only for internal consistency (seed→live continuity, correct TR math, correct band-ratchet behavior), *not* cross-checked against a reference implementation with real data. Do that before trusting it for actual trading decisions — see the ⚠️ note under Supertrend above.
- PCR being warm-up-only with zero live phase — implemented as designed, no issues found.
- Output topic-per-indicator-type + the `{Instrument}:{Timeframe}:{Period}:{Multiplier}` key convention — implemented as designed, verified live.

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
- **Dashboard**: Data page "Indicators" section — currently a placeholder ("nothing computes these anywhere yet"). No longer blocked now that 2e ships real EMA/Supertrend state into `Indicator:Running:*` — surface per-instrument seeded/live status here (and separately, historical-sufficiency status reusing 2d's validation capability). This is about *visibility for humans before market open*, distinct from the dashboard's existing Data page checks, which are all about *live/today's* data flow, not historical sufficiency or indicator state.
- **`StrategyService` deploy-time "insufficient history" warning** — reusing 2d's validation capability at deploy time, so a gap is caught when someone deploys, not just at `Init` the next relevant morning. Doesn't have to block the deploy — could be warn-only — that's its own decision when we get there.
- **Backtest** — placeholder dashboard page, doesn't exist yet. Will eventually need the same "do we have enough Azurite history" check as 2d, just for an arbitrary date range instead of "today minus N days." Not worth designing until Backtest itself is being built.
