# Dashboard Service

Lightweight status dashboard for the whole stack. Deliberately simple: an ASP.NET Core minimal API backend with **no frontend framework, no build step** — just a static `index.html` + vanilla JS that polls two JSON endpoints every 5 seconds.

Open **http://localhost:8099** to view it.

## Navigation

A left sidebar (collapses to a horizontal icon bar below 700px) lists 8 pages, switched client-side via URL hash (`#services`, `#exchanges`, `#data`, `#strategy`, `#datasync`, `#backtest`, `#broker`, `#alerts`) — no page reload, and the current page is bookmarkable/shareable as a link.

| Page | Status |
|---|---|
| Services & Connections | Live |
| Exchanges | Live — country/exchange session status from Redis |
| Data | Live — ingestion (1-min) and aggregation (5/10/15/30/60/75-min) candle-count status per contract |
| Strategy | Live — list/view/edit/delete strategies via `strategy-live`'s API |
| Data Sync | Placeholder |
| Backtest | Placeholder |
| Broker Configuration | Placeholder |
| Alerts / Signals | Placeholder |

The still-placeholder pages are just static markup in `wwwroot/index.html` (`<div class="page" data-page="...">`) with a "not wired up yet" note — no backend endpoints exist for them yet. When one of those areas gets built out, add its real content to the matching `.page` div; the nav wiring, hash routing, and active-state styling need no changes.

### Exchanges page

Two new backend endpoints (`GET /api/country`, `GET /api/exchanges`) read the `India` and `Exchange:*` Redis keys that `notification-live`'s `CountryNotificationFunctions`/`ExchangeNotificationFunctions` write.

- **Country**: a card showing today's state (Normal / Holiday / Weekend), holiday reason if applicable, next upcoming holiday, and when it last ran. A "Stale" badge appears if the cached `Date` isn't today — country-live only runs once at 00:01 IST, so this is the one signal that tells you whether it actually ran today.
- **Exchange Session Timeline**: one horizontal timeline per exchange found in Redis (currently NSE, NFO once both have fired at least once), five stages — Init (09:00) → Pre-Open (09:07) → Open (09:15) → Pre-Close (15:15) → Close (15:30) — connected by a line. A stage "lights up" (green dot + green connector) if it's at or before the exchange's current cached state **and** that state is from today; otherwise it stays dim. The lit-up logic relies on the timers firing strictly in order and each overwriting the same Redis key — there's no need to track each of the 5 stages independently, "current state's position in the sequence" already implies every earlier one fired. `EXCHANGE_STAGES` in `wwwroot/app.js` is the one place that would need updating if the schedule times ever change.
- Server (not browser) computes whether cached data is "today's" — `DateTime.Now` inside the container is already IST via the `TZ` env var, same convention every other service in this stack uses — avoiding any timezone ambiguity from viewing the dashboard on a different device/timezone (e.g. your phone).

### Data page

Two endpoints — `GET /api/data-ingestion` (1-min) and `GET /api/aggregation` (5/10/15/30/60/75-min) — each return one status entry per contract/timeframe, discovered dynamically from whatever `DataIngestion:*` / `Aggregation:OHLC:*:{tf}:Min` keys actually exist in Redis. **Nothing is hardcoded to a fixed ticker list** — a new contract shows up here automatically the moment it starts flowing through the pipeline, no dashboard code change needed.

The **data provider** (`TradingView` today) is discovered the same way, not hardcoded either — `/api/data-ingestion` scans the broad `DataIngestion:*` pattern and pulls both the provider and the ticker out of the key (`DataIngestion:{Provider}:{Ticker}`) via `DiscoverProviderTickers` in `Program.cs`. A future provider (Zerodha, Kite, NSE direct, ...) shows up here automatically the moment it starts writing to Redis under its own provider segment — see [NotificationService/README.md](../NotificationService/README.md#data-provider-is-discovered-not-hardcoded) for the producer/consumer side of this. Aggregation has no provider concept — its Redis keys never carried one — so `provider` is `null` on every `/api/aggregation` entry and the ingestion cards are the only ones that show a **Provider** line.

For each contract/timeframe, a card shows:
- A progress bar drawn from real **per-bucket ground truth**, not just a count. The `Ingestion:Count:`/`Aggregation:Count:` Redis SETs (see [NotificationService/README.md](../NotificationService/README.md#per-day-candle-counts-for-the-dashboards-data-page)) store each arrived bucket's own `WindowsStartTime` as its member — so the backend reads the SET's actual members, not just its length, and builds `bucketMap`: one character per expected bucket for the full session (375 ÷ timeframe-in-minutes — 375 for 1-min, 75 for 5-min), `'a'` arrived / `'m'` missing / `'p'` not due yet. Bucket starts are computed as `sessionOpen + i × timeframeMinutes`, matching exactly how the aggregators themselves align windows, so the map lines up bucket-for-bucket with reality — not an aggregate approximation of it.

  This replaced a simpler "first N ticks are fill, then one gap" model that couldn't represent an outage in the middle of an otherwise-healthy day — it could only ever draw one trailing gap, wherever the arithmetic put it, never where the real hole was. With the real map, an outage-and-recovery renders exactly as it happened: arrived, arrived, **missing** (the actual outage window), arrived again, arrived again — visible on the bar itself, not just inferred from a count.
- A status badge — computed server-side from **how fresh the most recently arrived bar is**, not from count vs. total: **Pending** (before market open, nothing to be behind on yet), **On Track** (latest arrival within 2× the timeframe old), **Behind** (within 4×), **Behind / No Data** (older than that, or nothing has arrived at all this session). Same "stale" thresholds `/api/freshness` already uses, applied here to whichever bar is most recent.

  This went through two earlier versions, both replaced for the same underlying reason. A ratio (`count / expectedSoFar >= 0.9`) made low-bucket-count timeframes wildly over-sensitive — 75-min only has 5 buckets in a session, so the same one-bucket lag that's invisible on 1-min (375 buckets) used to swing 75-min straight to amber. Fixing that to an absolute bucket gap (`expectedSoFar - count`) helped, but both share a deeper flaw: neither can recover from a *permanent* historical gap. If the pipeline was down for a stretch and missed 40 buckets, cumulative count stays 40 short of expected for the rest of the day — even hours after the pipeline is fully healthy again, the badge stayed red regardless. Freshness of the latest arrival has no memory of history: a bar that landed 90 seconds ago means caught up *right now*, whatever happened three hours earlier. The cumulative shortfall is still shown as informational text (`N short`) — that's accurate and worth keeping, it just no longer drives the color.

  Three fixed colors, never a status- or session-driven palette: arrived bars are **always** green, missing buckets are **always** red, not-due-yet is **always** dim — a bucket's own state doesn't change because the aggregate status badge or the session mood changed. See [design-system/algotrading-dashboard/pages/data.md](../../../design-system/algotrading-dashboard/pages/data.md#three-fixed-colors-never-a-status--or-phase-driven-palette).
- Two storage indicators — **Redis** (green if the count SET has any members) and **Azurite** (green if `ohlc-live` actually has a blob row for today's date for that contract). Used to be expected-red during live hours (the Azurite upload was a manual after-hours process) — that's no longer true since `ohlc-live`'s `LiveCandlePersistenceFunction` started writing every live candle straight through, so this should read green live now too. `CheckAzurite` (`Program.cs`) calls `GetOHLCByYearAndMonth`, not `GetOHLCDataByDate` — see below, the latter has a known hang for `BANKNIFTY`.

**Aggregation is grouped into two sub-sections**: **Timeframes** (the card grid above, one card per contract × configured timeframe — `int[] timeframes` in `Program.cs`'s `/api/aggregation` handler) and **Indicators** — real now (EMA/Supertrend/Pivot Central Range), see `GET /api/indicators` and [WARMUP_AND_INDICATOR_PLAN.md](../../../WARMUP_AND_INDICATOR_PLAN.md) section 2e. Discovers cards straight from `Indicator:Running:*` in Redis, not from `Indicator:Manifest:Active` — the manifest only lists what `AggregationService` needs to keep live, and deliberately excludes Pivot Central Range (no live phase), but this section should still show PCR once `WarmUpService` computes it each morning.

#### `/api/data-ingestion` and `/api/aggregation` run their per-card Azurite checks concurrently, not sequentially

Found live: each card's status includes an HTTP round-trip to `ohlc-live` (`CheckAzurite`). The
original code awaited these one at a time in a loop — `/api/aggregation` covers 6 timeframes × N
tickers, so that's up to a dozen sequential round-trips. Once the day's Azurite CSV blobs grew
large enough, that pushed the whole endpoint past 15 seconds, which silently hung both this page's
own polling *and* `home.html`'s landing-page poll (`Promise.all` with no client-side timeout) —
the console just sat on "Connecting to the stack" forever with no visible error. Fixed two ways:
1. Both endpoints now build a list of tasks and `Task.WhenAll` them instead of awaiting one at a
   time — total latency is bounded by the slowest single call, not the sum of all of them.
2. Both `app.js` and `home.js` now wrap every poll fetch with an explicit ~8s timeout
   (`AbortController`), so even a future slow endpoint degrades to "unreachable" instead of
   freezing the page indefinitely.

Ingestion is 1-min only (that's what the pipeline ingests). Aggregation covers every configured timeframe (5/10/15/30/60/75-min) in one flat card grid, sorted by timeframe then contract — no further grouping needed since the whole pipeline (counting, discovery, status computation, card rendering) was already timeframe-generic; the array of timeframes was the only thing scoping it down.

### Strategy page

Talks directly to [`strategy-live`](../StrategyService/README.md)'s API (`http://<host>:8096`, derived from `location.hostname` in the browser — not hardcoded, so it also works when the dashboard is opened from another device via the PC's LAN IP).

- **Grid**: a data table, one row per strategy, columns **Exchange, Strategy Name, Version, Broker, Goals, Risk, Trade Type, Instruments**, plus an Actions column with **View / Edit / Deploy / Delete** buttons. Version cell shows the current version and a badge — "Deployed" (green) if the saved version is the deployed one, "Deployed vX" (red) if a newer draft exists on top of an older deployed version, or "Not deployed". Scrolls horizontally on narrow screens rather than squeezing 9 columns into a phone width.
- **View**: read-only structured panel (exchange, version, broker, risk, trade type, goals and instruments as chips), plus a plain-English one-line summary per rule (e.g. *"Pivot Central Range (1 Day, BankNifty_Index_Spot) < 0.0038 * Closing Price"*) for **all nine** rule groups: Trading Session Rules, and the Entry / Risk Management / Update Stop-Loss / Exit rules for both Long Entry and Short Entry. No raw-JSON fallback remains — every rule array in the schema now has a structured view.
- **Edit** / **+ New Strategy**: clicking either opens the *same* form in the panel below the grid — Edit pre-fills it from the clicked row's full strategy data (fetched fresh via GET, not read off the row), + New Strategy opens it empty. A structured form — text input (Strategy Name), disabled/hardcoded fields (Exchange = NSE, Risk = Moderate, Version — server-computed, shown but not editable), single-select dropdowns (Broker, Trade Type), multi-select checkboxes (Goals, Instruments), and **nine rule-builder sections** (Trading Session Rules, plus Entry/Risk Management/Update Stop-Loss/Exit for both Long Entry and Short Entry) — all one reusable component, since every one of them is a `TradingRule[]` under the hood. Each rule card has Left/Right Operand (Type dropdown — Indicator/Literal/Expression — plus Value, and a Properties sub-grid for Period/Multiplier/Timeframe/Instrument/Relative Position that hides itself for Literal operands since those are pure constants), an Operator dropdown (`==`, `!=`, `<`, `<=`, `>`, `>=` — deliberately **not** offering the `<>=>` typo found in the original hardcoded strategy), and a Link-to-next-rule dropdown (`AND`/`OR`/none). **+ Add Rule** / **Remove** buttons per section. The old "Advanced: raw rules JSON" textarea is gone — nothing needs it anymore now that every rule array is structured.

  Implementation notes:
  - The rule builder's working state is `ruleSections` — an object keyed by section id (`tradingSessionRules`, `longEntryRules`, `longEntryRisk`, `longEntryStopLoss`, `longEntryExit`, `shortEntryRules`, `shortEntryRisk`, `shortEntryStopLoss`, `shortEntryExit`), each holding a camelCase `TradingRule[]` matching the API's GET shape. One `ruleSectionBlockHtml(key, title)` / `renderRuleSection(key)` pair renders all nine instances — the `LONG_ENTRY_SECTIONS`/`SHORT_ENTRY_SECTIONS` arrays are the only per-side thing; the rendering/save/sync code is identical for both.
  - Add/Remove/Save read the DOM back into `ruleSections[key]` only on those structural events, not on every keystroke, so typing in a rule field never loses cursor focus from a re-render.
  - The Add/Remove buttons and the operand Type-change listener are delegated once on the static `#strategy-panel` container (present from page load), not re-wired inside `renderStrategyForm` on every render — since the panel's *contents* get replaced via `innerHTML` each time the form opens, but the container itself never does.
- **Deploy**: marks the strategy's current version as deployed. This does **not** do anything about a previously-deployed version yet — no cleanup, no strategy-engine integration. That's explicitly unbuilt pending further design.

Saving always re-fetches the grid from the server rather than trusting local state, and navigating to the Strategy tab always reloads it — so edits made from another tab or device show up without a manual refresh.

## What it shows

- **Services & Connections** — every container in the `live` compose project (queried live from the Docker Engine API), with running/stopped state, port mappings, a small icon per service kind (Kafka/Zookeeper/Kafdrop, Redis, Azurite, SignalR, Dashboard, or a generic bolt icon for the Function-app services), and arrows drawn between cards showing `depends_on` relationships. The arrows are computed from Docker's own `com.docker.compose.depends_on` container label at request time — not hand-drawn — so they can't silently drift out of sync with `docker-compose-live.yml` the way a static diagram would.

  Services are grouped into three bordered, color-accented panels — **Infrastructure** (blue), **Helpers** (amber), **Core Services** (violet) — via a hand-maintained `CATEGORIES` map in `wwwroot/app.js` (Docker has no concept of this grouping, so unlike the dependency arrows it can't be derived automatically — **update that map when you add a new service to compose, or it'll silently fall into "Core Services" by default**). The panel row can be toggled horizontal/vertical via the button next to the section heading (saved in `localStorage`); on screens narrower than 700px it always stacks vertically regardless of that preference, since three columns don't fit a phone screen.
- **`/api/freshness`** (no longer a dedicated dashboard page — its ingestion/aggregation content overlapped the Data page too closely, so it was dropped from the sidebar in favor of Data) scans every string key in `redis-live` generically (no hardcoded ticker/timeframe list), grouped by the prefix before the first `:` in the key (`DataIngestion`, `DataFeed`, `Aggregation`, …), and flags each as stale once its age passes 2× its own timeframe. The endpoint itself is still live — it feeds the console's **Cache Freshness** widget on `home.html`.

Kafka topic-level detail isn't duplicated here — click **Kafdrop ↗** in the header for that.

The sun/moon button in the header toggles dark/light mode; the choice is saved in `localStorage` and otherwise defaults to the browser's OS-level color scheme preference.

## Host-timezone-independent timestamps

Every "today"/staleness/session-time calculation (`/api/freshness` age, Country/Exchange `IsToday`, the Data page's per-contract expected-so-far count) goes through a local `IstNow()` helper in `Program.cs` — `TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, "India Standard Time")` — instead of `DateTime.Now`. `DateTime.Now` only happens to return IST today because this container's `TZ=Asia/Kolkata` env var is set; on a host where that's not true (a different machine, or a future cloud platform that resets/ignores container `TZ`), every one of those calculations would silently start using the host's actual timezone instead, with nothing to signal the drift. `DateTime.UtcNow` is always correct regardless of host config, so converting explicitly from there removes the dependency entirely. Same fix applied in `NotificationService` (count-key dates) and `AggregationService` (bucket alignment) — see those services' READMEs.

## How it talks to Docker

Uses [Docker.DotNet](https://github.com/dotnet-ecosystem/Docker.DotNet) against the Docker Engine API, filtered to containers labeled `com.docker.compose.project=live`. No `docker` CLI is installed in the image.

⚠️ **Docker socket is mounted into this container** (`/var/run/docker.sock`), which gives it full control over the Docker daemon — not just this compose project. This is the same pattern tools like Portainer use, and is fine for local/single-host use, but don't expose port `8099` to an untrusted network without putting auth in front of it.

## Operations

### Compose

Service key: `dashboard-live` in [docker-compose-live.yml](../../../docker-compose-live.yml). Container: `dashboard-live`. Host port `8099` → container port `8080`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d dashboard-live
docker-compose -f docker-compose-live.yml -p live logs -f dashboard-live
```

### Build

```bash
cd EventDrivenSystem/Source/DashboardService
docker build -t dashboard-service-live-image:v1 -f Dockerfile .
docker-compose -f docker-compose-live.yml -p live up -d dashboard-live   # recreate with the new image
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `TZ` | `Asia/Kolkata` |
| `RedisConnectionString` | `redis-live:6379` |
| `DOCKER_HOST_URI` | `unix:///var/run/docker.sock` |
| `COMPOSE_PROJECT_NAME` | `live` — used to filter which containers show up |

### Running outside Docker (local dev)

```bash
cd EventDrivenSystem/Source/DashboardService
ASPNETCORE_URLS="http://localhost:6100" RedisConnectionString="localhost:6382" dotnet run
```

Outside a container, `DOCKER_HOST_URI` defaults to the Windows named pipe (`npipe://./pipe/docker_engine`) automatically, so Docker Desktop is picked up with no extra config.

### API

```bash
curl http://localhost:8099/api/services
curl http://localhost:8099/api/freshness
```
