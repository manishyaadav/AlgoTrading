# Dashboard Service

Lightweight status dashboard for the whole stack. Deliberately simple: an ASP.NET Core minimal API backend with **no frontend framework, no build step** — just a static `index.html` + vanilla JS that polls two JSON endpoints every 5 seconds.

Open **http://localhost:8099** to view it.

## Navigation

A left sidebar (collapses to a horizontal icon bar below 700px) lists 7 pages, switched client-side via URL hash (`#services`, `#freshness`, `#strategy`, `#datasync`, `#backtest`, `#broker`, `#alerts`) — no page reload, and the current page is bookmarkable/shareable as a link.

| Page | Status |
|---|---|
| Services & Connections | Live |
| Data Freshness | Live |
| Strategy | Live — list/view/edit/delete strategies via `strategy-live`'s API |
| Data Sync | Placeholder |
| Backtest | Placeholder |
| Broker Configuration | Placeholder |
| Alerts / Signals | Placeholder |

The still-placeholder pages are just static markup in `wwwroot/index.html` (`<div class="page" data-page="...">`) with a "not wired up yet" note — no backend endpoints exist for them yet. When one of those areas gets built out, add its real content to the matching `.page` div; the nav wiring, hash routing, and active-state styling need no changes.

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
