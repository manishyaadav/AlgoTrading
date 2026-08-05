# Dashboard Service

Lightweight status dashboard for the whole stack. Deliberately simple: an ASP.NET Core minimal API backend with **no frontend framework, no build step** — just a static `index.html` + vanilla JS that polls two JSON endpoints every 5 seconds.

Open **http://localhost:8099** to view it.

## Navigation

A left sidebar (collapses to a horizontal icon bar below 700px) lists 9 pages, switched client-side via URL hash (`#services`, `#freshness`, `#exchanges`, `#data`, `#strategy`, `#datasync`, `#backtest`, `#broker`, `#alerts`) — no page reload, and the current page is bookmarkable/shareable as a link.

| Page | Status |
|---|---|
| Services & Connections | Live |
| Data Freshness | Live |
| Exchanges | Live — country/exchange session status from Redis |
| Data | Live — ingestion/aggregation candle-count status per contract |
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

Two endpoints — `GET /api/data-ingestion` (1-min) and `GET /api/aggregation` (currently 5-min only — see below) — each return one status entry per contract, discovered dynamically from whatever `DataIngestion:*` / `Aggregation:OHLC:*:5:Min` keys actually exist in Redis. **Nothing is hardcoded to a fixed ticker list** — a new contract shows up here automatically the moment it starts flowing through the pipeline, no dashboard code change needed.

The **data provider** (`TradingView` today) is discovered the same way, not hardcoded either — `/api/data-ingestion` scans the broad `DataIngestion:*` pattern and pulls both the provider and the ticker out of the key (`DataIngestion:{Provider}:{Ticker}`) via `DiscoverProviderTickers` in `Program.cs`. A future provider (Zerodha, Kite, NSE direct, ...) shows up here automatically the moment it starts writing to Redis under its own provider segment — see [NotificationService/README.md](../NotificationService/README.md#data-provider-is-discovered-not-hardcoded) for the producer/consumer side of this. Aggregation has no provider concept — its Redis keys never carried one — so `provider` is `null` on every `/api/aggregation` entry and the ingestion cards are the only ones that show a **Provider** line.

For each contract/timeframe, a card shows:
- A progress bar: **count today** (from the new `Ingestion:Count:`/`Aggregation:Count:` Redis SETs — see [NotificationService/README.md](../NotificationService/README.md#per-day-candle-counts-for-the-dashboards-data-page)) out of the **expected total for a full session** (375 ÷ timeframe-in-minutes — 375 for 1-min, 75 for 5-min).
- A status badge — **not** just raw count vs. total, since a session isn't over until 3:30. It's count vs. *how many should have landed by now*, computed server-side from elapsed time since 9:15: **Pending** (before market open, nothing to be behind on yet), **On Track** (≥90% of expected-so-far), **Behind** (50-89%), **Behind / No Data** (<50%, including no data at all today).
- Two storage indicators — **Redis** (green if the count SET has any members) and **Azurite** (green only if `ohlc-live` actually has a blob for today's date for that contract). During live market hours this is **expected to show red** — the Azurite blob upload is a manual, after-hours process today (see the root README's data-preparation discussion), not a bug in this indicator.

**Aggregation is grouped into two sub-sections**: **Timeframes** (the card grid above) and **Indicators** (a placeholder — nothing computes Supertrend/EMA/Pivot Central Range yet, this is just marking where that will eventually surface).

Currently wired for **1-min ingestion and 5-min aggregation only**, per explicit scope — extending to 10/15/30/60/75-min aggregation is one line each (`int[] timeframes = { 5 };` in `Program.cs`'s `/api/aggregation` handler), no other code changes needed since the whole pipeline (counting, discovery, status computation, card rendering) is timeframe-generic already.

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
- **Data Freshness** — every string key in `redis-live`, parsed generically (no hardcoded ticker/timeframe list) and grouped by the prefix before the first `:` in the key (`DataIngestion`, `DataFeed`, `Aggregation`, …). Shows last-updated time, age, and a stale/fresh pill. "Stale" means no update in more than 2× the entry's timeframe — outside market hours everything will show stale, which is expected, not a fault.

Kafka topic-level detail isn't duplicated here — click **Kafdrop ↗** in the header for that.

The sun/moon button in the header toggles dark/light mode; the choice is saved in `localStorage` and otherwise defaults to the browser's OS-level color scheme preference.

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
