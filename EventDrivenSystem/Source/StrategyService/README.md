# Strategy Service

Azure Function app exposing a CRUD-ish HTTP API over trading strategy definitions stored as JSON files. This is the config store the dashboard's **Strategy** page (http://localhost:8099/#strategy) reads and writes.

## Strategy config folder

`StrategyService/config/strategies/*.json` — every file here is one strategy. **Drop a new `.json` file in and it's picked up automatically**, no code change or rebuild needed (the folder is bind-mounted into the container, not baked into the image — see compose section below).

**Filename convention: `{strategy-name-slugified}-{version}.json`** — e.g. `second-income-1.0.2.json`. The slug (strategy name lowercased, spaces → `-`) is the strategy's **stable `id`**, used in every API/UI URL — it never changes across saves. The `-{version}` suffix is purely a storage/display detail for browsing the folder by eye: on every save, `StrategyMaker.SaveById` deletes whichever file currently holds that id and writes a new one under the new (auto-incremented) version — so there's always exactly **one current file per strategy**, not a version history. (A file dropped in without a version suffix, e.g. a hand-authored `my-strategy.json`, is still tolerated — its whole filename is treated as the id — but the next save through the API will rename it to the versioned form.)

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

**This is a schema, not an execution engine** — nothing in this service (or anywhere else in the stack yet) actually evaluates these rules against live data. See the root README's architecture notes for the current known gaps in the example strategy (`second-income.json`) if you're extending this.

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
- `DeployedVersion` only changes via the `/deploy` endpoint, which sets it to whatever `Version` currently is. A strategy can have unsaved/undeployed changes (`Version` ahead of `DeployedVersion`) — the dashboard grid shows this as a "Draft vX.Y.Z" badge alongside the "Deployed vX.Y.Z" one.
- **Deploying an older version over a newer deployed one currently does no cleanup** — that behavior (what "cleanup" even means here — stopping a running strategy engine instance, discarding in-flight state, etc.) is intentionally deferred pending further discussion, not yet designed or implemented.

## HTTP API

Route prefix: `api` (default). All responses are JSON, camelCase, with permissive CORS (`Access-Control-Allow-Origin: *`) since the dashboard's browser JS calls this cross-origin.

| Method | Route | Does |
|---|---|---|
| GET | `/api/strategies` | List all strategies (summary: id, name, exchange, broker, version, deployedVersion, risk, tradeType, goals, instruments, instrument→timeframe map) |
| GET | `/api/strategies/{id}` | Full strategy JSON |
| PUT / POST | `/api/strategies/{id}` | Create or overwrite — body is validated as parseable JSON before writing; `Version` is server-computed (see above), `DeployedVersion` carries over unchanged; on-disk file is re-serialized in PascalCase regardless of what casing was sent |
| POST | `/api/strategies/{id}/deploy` | Set `DeployedVersion` = current `Version`. No other side effects yet |
| DELETE | `/api/strategies/{id}` | Delete that strategy's file |

```bash
curl http://localhost:8096/api/strategies
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
