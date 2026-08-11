# Strategy Service

Azure Function app exposing a CRUD-ish HTTP API over trading strategy definitions stored as JSON files. This is the config store the dashboard's **Strategy** page (http://localhost:8099/#strategy) reads and writes.

## Strategy config folder

`StrategyService/config/strategies/{saved,deployed}/*.json` — **two folders, two independent lifecycles**. Drop a `.json` file into either and it's picked up automatically, no code change or rebuild needed (the whole `config` folder is bind-mounted into the container, not baked into the image — see compose section below).

- **`saved/`** — the working draft *history*. Every `PUT`/`POST` to `/api/strategies/{id}` writes a new version file here and **leaves every earlier version in place** — nothing is deleted on save. `GET /api/strategies`/`GET /api/strategies/{id}` and the dashboard's Edit form always resolve to the *highest-versioned* file for that id, so this reads like "one current draft" from the API even though the folder itself accumulates history. Old versions are there purely for manual reference/rollback — cleaning them up is a deliberate, explicit action (delete the file yourself, or `DELETE /api/strategies/{id}` removes all of them at once), not something a later save does automatically.
- **`deployed/`** — a snapshot taken *at the moment `/deploy` is called*, holding the actual deployed rule content. Unlike `saved/`, this folder still keeps exactly **one current file per strategy** — deploying replaces whatever was previously deployed, no history. `DeployedVersion` is **not** a field stored on the saved file — `StrategyMaker` computes it on every read by checking whether a matching file exists here, and reads `GetDeployedDataRequirements()` (below) straight from this folder's actual content.

Why the split exists: before it did, `DeployedVersion` was just a string field mutated on the *same* file `saved/` uses — so saving a new draft on top of a deployed version silently overwrote the deployed version's actual rules, with only the version *number* surviving. The data-requirements manifest read that overwritten content and could report a draft's requirements as if they were live. Splitting into two folders means a draft save can never touch what's actually deployed, and the manifest is always accurate to what was last explicitly deployed — verified: deploy v1.0.5, save an undeployed v1.0.6 draft with a different `Timeframe`, and the manifest still reports v1.0.5's requirements; only calling `/deploy` again updates it.

**Filename convention (same in both folders): `{strategy-name-slugified}-{version}.json`** — e.g. `second-income-1.0.2.json`. The slug (strategy name lowercased, spaces → `-`) is the strategy's **stable `id`**, used in every API/UI URL — it never changes across saves. The `-{version}` suffix is what makes the folder browsable/traceable by eye. The two folders now differ in what happens to the *previous* file at that id on a write: `deployed/` still deletes it (deploying v1.0.6 over a previously-deployed v1.0.5 replaces `deployed/second-income-1.0.5.json`, it doesn't keep both — one current file per strategy, not a history); `saved/` keeps it (saving v1.0.6 over a v1.0.5 draft leaves `saved/second-income-1.0.5.json` on disk alongside the new `saved/second-income-1.0.6.json`). Picking "the current one" among several saved versions is always by highest parsed `{major.minor.patch}`, not alphabetical filename order — a plain string sort gets this wrong once a patch number reaches double digits (`"1.0.10"` sorts before `"1.0.9"` as a string). (A file dropped into either folder without a version suffix, e.g. a hand-authored `my-strategy.json`, is still tolerated — its whole filename is treated as the id — but the next save through the API will write a properly versioned file alongside it; a file with no parseable version is never picked as "the current one" if a versioned file for the same id exists.)

Shape (matches the `Strategy`/`TradingStrategy`/`TradingRule`/`Operand` C# model in `Strategy/`):

```json
{
  "Exchange": "NSE",
  "StrategyName": "...",
  "Version": "1.0.0",
  "DeployedVersion": null,
  "Broker": "Zerodha",
  "Goals": ["..."],
  "Strategies": [
    {
      "Risk": "Moderate",
      "Instruments": ["..."],
      "TradeType": "Intraday",
      "TradingSessionRules": [ /* TradingRule[] */ ],
      "LongEntry": { "EntryRules": [], "RiskManagementRules": [], "UpdateStopLossRules": [], "ExitRules": [] },
      "ShortEntry": { /* same shape */ }
    }
  ]
}
```

A `TradingRule` is `{ Sequence, LeftOperand, Operator, RightOperand, Link }` where `Link` chains to the next rule (`"AND"`/`"OR"`/`""`). An `Operand` is `{ Type: "Indicator"|"Literal"|"Expression", Value, Properties }`, where `Properties` carries `Period`/`Multiplier`/`Timeframe`/`Instrument`/`RelativePosition` for indicator-type operands.

**This is a schema, not an execution engine** — nothing in this service (or anywhere else in the stack) generates a real entry/exit trigger or places an order; that's still fully undesigned (see `WARMUP_AND_INDICATOR_PLAN.md` section 4, "Strategy execution engine"). What *does* now exist is smaller and read-only: `GET /api/strategies/{id}/rule-status` (below) evaluates this rule tree against live Redis state for display purposes — see that section for the distinction. See the root README's architecture notes for the current known gaps in the example strategy (`second-income.json`) if you're extending this.

### Fixed value lists (dashboard form)

The dashboard's Strategy form currently constrains these fields to closed lists — expand the arrays in `wwwroot/app.js` (`GOALS_OPTIONS`, `BROKER_OPTIONS`, `INSTRUMENT_OPTIONS`, `TRADETYPE_OPTIONS`) as the product grows. `Exchange` (`NSE`) and `Risk` (`Moderate`) are hardcoded outright, not just closed lists — there's only one option and the form disables the field:

| Field | Current values |
|---|---|
| Broker | Zerodha, Upstox |
| Goals (multi-select) | Second Income, Education Goal, Retirement Goal, Accumulation Goal |
| Instruments (multi-select) | Bank Nifty Futures, Bank Nifty Options, Nifty 50 Futures, Nifty 50 Options |
| Trade Type | Intraday, CarryOver |

### Versioning and deployment

- `Version` auto-increments server-side on every save — `1.0.0` for a brand new strategy, then patch+1 (`1.0.1`, `1.0.2`, …) on each subsequent save. **Whatever `Version` value is in the request body is ignored** — the client can't set or spoof it; this is enforced in `StrategyMaker.SaveById`.
- `DeployedVersion` only changes via the `/deploy` endpoint, which sets it to whatever `Version` currently is *and writes a real snapshot of the strategy to `config/strategies/deployed/`* (see "Strategy config folder" above) — it's computed from that snapshot on every read, not stored as a mutable field. A strategy can have unsaved/undeployed changes (`Version` ahead of `DeployedVersion`) — the dashboard grid shows this as a "Draft vX.Y.Z" badge alongside the "Deployed vX.Y.Z" one, and saving further drafts never changes what `deployed/` holds.
- **Deploying an older version over a newer deployed one currently does no cleanup** — that behavior (what "cleanup" even means here — stopping a running strategy engine instance, discarding in-flight state, etc.) is intentionally deferred pending further discussion, not yet designed or implemented.

### Data-requirements manifest

`StrategyMaker.GetDeployedDataRequirements()` walks every currently-**deployed** strategy's rule tree (all 9 `TradingRule[]` arrays — `TradingSessionRules` plus LongEntry's and ShortEntry's 4 each) and collects every `(Instrument, Timeframe)` pair referenced by any operand carrying `Properties.Instrument` — Indicator operands (EMA, Supertrend, Pivot Central Range, …) **and** Expression operands that reference raw price data (e.g. `"Closing Price"`, `"Candle High"`) — unioned across all deployed strategies. This is meant to answer "what does the system actually need to be ingesting/aggregating right now" — the intended consumers are a future historical-data warm-up job (which pairs need backfilling before market open, and how much) and anything that reacts to a strategy being deployed or changed (e.g. gating which timeframe aggregations actually run, discussed but not yet built).

Each pair breaks down into a `references` list — the specific `(Value, Type, Period, Multiplier, RelativePosition)` combinations that need it, with which strategy id(s) use each one. This is the part that actually answers "how much history": an `(Instrument, Timeframe)` pair alone doesn't say whether it's backing an `EMA(550)` or a `Supertrend(20,4)`, and those need very different amounts of backfill. `RelativePosition: "Previous"` on an otherwise-identical reference is its own separate entry, not merged with the "current value" one — e.g. `second-income` references `Supertrend(20,4)` twice on 5-min NIFTY: once for the live value, once with `RelativePosition: "Previous"` (its `UpdateStopLossRules` compares the two), and the manifest reports both because a warm-up job needs at least one extra prior bar of Supertrend for the second one that it wouldn't need for the first alone.

Reads straight from `config/strategies/deployed/` — the actual deployed snapshot, not the saved draft — so it stays accurate even while a newer, undeployed draft with different requirements exists. Verified: deployed `second-income` at v1.0.5, saved an undeployed v1.0.6 draft with a different `Timeframe`, confirmed the manifest still reported v1.0.5's requirements untouched; redeploying (now at v1.0.6) updated it immediately. Before the `saved`/`deployed` folder split existed, this read the saved file directly and could report a draft's requirements as if they were live — that's the gap this split closes.

### Warm-up plan — "fetch the last N days of {Instrument} data"

`StrategyMaker.GetWarmUpPlan()` (`GET /api/strategies/warm-up-plan`) turns the manifest above into an actual day count per instrument — the manifest says *what* is needed, this says *how much*. Each `reference`'s `Period`/`Type`/`RelativePosition` maps to a day count via documented (not verified against a real indicator engine — none exists yet) assumptions in `ComputeDaysNeeded`:

1. `Period > 0` (EMA, Supertrend, any period-based indicator or expression) → needs at least `Period` bars — the mathematical minimum, not an extra-converged buffer.
2. `Period == 0` and `Type == "Indicator"` (e.g. `Pivot Central Range`, which has no Period/Multiplier of its own) → assumed to need exactly 1 prior trading day, independent of whatever Timeframe it's being *compared* against (conventionally computed from the prior day's H/L/C, not intraday bars).
3. `Type == "Expression"` with a `RelativePosition` other than null/`"Current"` (e.g. `"Closing Price"` at `"Previous"`) → needs 1 prior bar at that Timeframe.
4. `Type == "Expression"` with no `RelativePosition` (e.g. `"Candle High"` — asking for the live value) → 0 days, no backfill needed.

An instrument's `daysToFetch` is the max `daysNeeded` across every reason that needs it — one historical pull covering the most demanding requirement covers every lesser one too. Verified against the real deployed `second-income` strategy: `Nifty_Index_Spot` → **8 days**, driven by `EMA(550)` on 5-min (550 bars × 5 min ÷ 375 trading-min/day ≈ 7.3 → 8), matching the "pull last 7-8 days" estimate from the original data-preparation discussion almost exactly — everything else it needs (`Supertrend(20,4)`, `Pivot Central Range`, the prior day's close) needs far less, so it doesn't change the number.

If a strategy references an instrument other than the one you're expecting, it shows up as its own entry in the same array automatically — nothing here is hardcoded to NIFTY.

### Rule Engine — `GET /api/strategies/{id}/rule-status`

Backs the dashboard's **Rule Engine** page (`http://localhost:8099/#rule-engine`). Reads the actual
**deployed** rule tree (`StrategyMaker.GetDeployedById`, not the saved draft) and walks it top to
bottom, evaluating each `TradingRule` against whatever live data actually exists in Redis right
now — `Engine/RuleEvaluator.cs`. Read-only: this is a *visualization* of what the rules would
currently evaluate to, not the execution engine itself (see `WARMUP_AND_INDICATOR_PLAN.md` section
4) — no side effects, nothing written back to Redis, no order ever placed.

This service's first Redis dependency (`RedisConfig/RedisHelper.cs`) — it was pure CRUD-over-files
until this needed to read live indicator/session state.

404s if `id` has no deployed version — the page only ever calls this for a strategy it already
knows is deployed (its strategy switcher is built off `GET /api/strategies`' `deployedVersion`
field), so "is this strategy deployed?" isn't part of the response; there'd be nothing to evaluate
otherwise.

**What actually evaluates today, and what stays honestly "unknown"** — every rule is resolved, not
just the ones that happen to work, so a strategy with different rule shapes degrades the same way
rather than silently doing nothing:

| Resolves live | Doesn't (marked `unknown`, with a reason) |
|---|---|
| Pivot Central Range vs. `N × Closing Price (Previous)` — needs `PriorClose`, see `WarmUpService/README.md` | Anything needing account/capital state (Risk Management rules) |
| EMA vs. a literal or another indicator | Anything needing position/order state (Update-Stop-Loss, Exit rules — see the Position gate below) |
| Supertrend vs. EMA (numeric) | `RelativePosition: "Previous"` on EMA/Supertrend/PCR — only the current running state is kept, no separate prior-bar snapshot |
| Supertrend vs. `"GREEN"`/`"RED"` (Literal) — translates Redis's `TrendDirection: "Up"/"Down"` via the standard TradingView convention, the one place that translation happens anywhere in this codebase | Any other raw Expression (`Candle High/Low`, `Current Profit`, `Time in Trade`, `Trading Session State`, …) — no live source for these anywhere yet |

**The "In a position?" gate is a permanent placeholder** — confirmed via full repo search while
building this: no `PortfolioService`, no position/order Redis key, no Kafka topic, nothing tracks
this anywhere. The page draws the Entry Rules branch as live (the only branch anything backs) and
the Exit/Stop-Loss/Risk-Management branches as a static, never-evaluated preview of the same rule
tree, honestly labeled as such rather than pretending to have an answer.

Response shape: `{ strategyId, strategyName, exchange, instruments, deployedVersion,
tradingSessionRules, positionGate, long: {entryRules, riskManagementRules, exitBranch}, short:
{...} }`. Each evaluated rule carries the *original* `TradingRule` (same shape `GetStrategy` already
returns) plus `status` (`"pass"`/`"fail"`/`"unknown"`), `reason` (only when unknown), and a
`left`/`right` `OperandEvidence` pair — the dashboard reuses its existing
`describeRule()`/`describeOperand()` for the rule text rather than this service formatting it twice.

### Session gate — `GET /api/session-status`

The holiday/weekend check (Redis `"India"`, the same key `country-live`/`notification-live`
maintain) isn't a fact about any one strategy — it's identical no matter which deployed strategy
you're looking at. It used to be duplicated inside every strategy's own `rule-status` response as
that strategy's "Gate 2"; now it's evaluated once, standalone, and the dashboard renders it above
the strategy switcher rather than repeating it per strategy. Returns a single `GateNode`: `{
eyebrow, title, status, detail, values, sourceIds }` — the same shape `positionGate` and the old
per-strategy gates already used, so the dashboard's existing `gateNodeHtml()` renders it with no
new markup. `sourceIds` is always `[]` here — this endpoint stands alone, with no evidence-drawer
registry backing it the way a strategy's own rule tree has.

**`OperandEvidence` — where each side's value actually came from.** Not just the answer: the page
renders a per-rule drawer showing the derivation, so "why does it think RED" is answerable without
opening a Redis CLI.

| Field | Meaning |
|---|---|
| `display` | The formatted value, e.g. `"24,480.34 (Down→RED)"` — `null` if unresolved |
| `numeric` | Set only when the side genuinely compares as a number. Drives the dashboard's distance-to-flipping readout, so `null` means *don't draw a gap*, never *assume zero* |
| `kind` | `"indicator"` \| `"literal"` \| `"expression"` \| `"unresolved"` |
| `source` | The exact Redis key read. `null` for literals (they're part of the rule, not live data). **Set even when unresolved** — "we looked here and it wasn't seeded" is a more useful answer than "no idea" |
| `asOf` | The bar window (`LastBarWindowsStartTime`) for per-bar indicators, or `SessionDate` for Pivot Central Range, which is computed once a session rather than per bar |
| `fields` | The raw hash entries the value was derived from, **verbatim and unrounded** — the drawer's whole purpose is showing what's really in Redis, so reformatting here would defeat it |

The one synthetic entry in `fields` is Supertrend's `band used`: which of `PrevUpperBand`/
`PrevLowerBand` *is* the Supertrend line depends entirely on `TrendDirection`, and that choice is
invisible in the raw hash. Everything else is copied straight out.

Rules in the never-evaluated branches (Risk Management, Exit/Stop-Loss) deliberately resolve
**nothing at all** — not even the operands that would resolve fine. Reading Redis for the half of
each rule that *is* backed would fill a drawer with real-looking evidence for a rule that was never
evaluated; empty evidence is the honest shape, and the dashboard renders those rules in their
original compact one-line form.

**`sources` — the inputs behind the whole tree.** Alongside the rules, the response carries a flat
`DataSource[]` of every live input the evaluator touches, and every rule and gate carries
`sourceIds` linking to it. This is what lets the dashboard show the data flow rather than only the
verdicts: which inputs are real right now, how many rules read each one, and which rules are
reading something nothing feeds.

Built by `Engine/SourceRegistry.cs`, deliberately in two steps:

- **`Touch`** names a dependency, derived from the rule definition alone with no Redis involved.
  Called for **every** rule including never-evaluated ones — "this rule reads Supertrend" is a fact
  about the rule, true whether or not anything evaluates it.
- **`Fill`** attaches a value that was actually read, and is the only thing that sets `backed`.

Keeping them separate is what stops "this rule reads Supertrend" from being confused with
"Supertrend has a value right now". An input that is only ever Touched stays `backed: false` and is
drawn dashed and valueless — including the synthetic `Position / order state` entry, which exists
purely to be unbacked and to carry the count of rules depending on it.

`Engine/RuleEvaluator.ClassifySource()` is the single source of truth for source identity: the
resolvers use it to name what they just read, and the never-evaluated branches use it to name what
they *would* read, so both land on the same entry. Note that operands typed `Literal` are never
sources — they're part of the rule, not an input — so a strategy that mistypes account state as a
`Literal` will not show it in this list.

```bash
curl http://localhost:8096/api/strategies/second-income/rule-status
```

⚠️ **Azure Functions routing gotcha, if you add another `strategies/<literal>` route**: the isolated-worker HTTP router resolves an ambiguous literal-vs-`{id}` match by **function name alphabetical order**, not route specificity. `GetDataRequirements` (D) sorts before `GetStrategy` (S) and correctly wins; a function literally named `GetWarmUpPlan` (W) sorts *after* `GetStrategy` and silently loses — every request landed in `GetStrategy` with a `"No strategy with id 'warm-up-plan'"` 404, despite both routes showing up correctly in the startup log's "Mapped function route" lines. Renaming the function to `GetDataWarmUpPlan` (matching the "GetData…" prefix that's already proven to sort correctly) fixed it. Name any future literal-route function so it alphabetically precedes `GetStrategy`.

## HTTP API

Route prefix: `api` (default). All responses are JSON, camelCase, with permissive CORS (`Access-Control-Allow-Origin: *`) since the dashboard's browser JS calls this cross-origin.

| Method | Route | Does |
|---|---|---|
| GET | `/api/strategies` | List all strategies (summary: id, name, exchange, broker, version, deployedVersion, risk, tradeType, goals, instruments, instrument→timeframe map) |
| GET | `/api/strategies/data-requirements` | The deployed-strategies data-requirements manifest — array of `{instrument, timeframe, strategyIds, references}` (see above) |
| GET | `/api/strategies/warm-up-plan` | The warm-up plan derived from the manifest — array of `{instrument, daysToFetch, reasons, strategyIds}` (see above) |
| GET | `/api/strategies/{id}` | Full strategy JSON |
| GET | `/api/strategies/{id}/rule-status` | The deployed rule tree evaluated against live Redis state — see above |
| GET | `/api/session-status` | The holiday/weekend gate, common to every deployed strategy — see above |
| PUT / POST | `/api/strategies/{id}` | Create or overwrite — body is validated as parseable JSON before writing; `Version` is server-computed (see above), `DeployedVersion` carries over unchanged; on-disk file is re-serialized in PascalCase regardless of what casing was sent |
| POST | `/api/strategies/{id}/deploy` | Set `DeployedVersion` = current `Version`. No other side effects yet |
| DELETE | `/api/strategies/{id}` | Delete that strategy entirely — every version file in `saved/` plus the current `deployed/` file, if any |

```bash
curl http://localhost:8096/api/strategies
curl http://localhost:8096/api/strategies/data-requirements
curl http://localhost:8096/api/strategies/warm-up-plan
curl http://localhost:8096/api/strategies/second-income
curl -X PUT http://localhost:8096/api/strategies/my-strategy -H "Content-Type: application/json" -d '{"Exchange":"NSE","StrategyName":"My Strategy","Broker":"Zerodha","Goals":[],"Strategies":[]}'
curl -X POST http://localhost:8096/api/strategies/my-strategy/deploy
curl -X DELETE http://localhost:8096/api/strategies/my-strategy
```

## Operations

### Compose

Service key: `strategy-live` in [docker-compose-live.yml](../../../docker-compose-live.yml). Container: `strategy-service-live-container`. Host port `8096`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d strategy-live
docker-compose -f docker-compose-live.yml -p live logs -f strategy-live
```

The compose entry bind-mounts `StrategyService/StrategyService/StrategyService/config` into the container at `/home/site/wwwroot/config` — edits made through the API (or files you drop in by hand) land as real files in this repo, not trapped inside the container.

### Build

```bash
cd EventDrivenSystem/Source/StrategyService/StrategyService/StrategyService
docker build -t strategy-service-live-image:v1 -f Dockerfile .
docker-compose -f docker-compose-live.yml -p live up -d strategy-live   # recreate with the new image
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `AzureWebJobsStorage` | points at `azurite-live` |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `ASPNETCORE_ENVIRONMENT` | `docker` |
| `RedisConnectionString` | `redis-live:6379` — needed by `GET /api/strategies/{id}/rule-status` (see above); this service's first Redis dependency |
