# SignalR Server

Self-hosted ASP.NET Core SignalR server. `notification-live` pushes events here; browser/UI clients connect to receive real-time pushes. No Kafka/Redis dependency of its own — it's purely a fan-out relay.

## Hubs

Mapped in [Program.cs](Program.cs):

| Hub path | Hub class |
|---|---|
| `/aggregationHub` | `AggregationHub` |
| `/countryHub` | `CountryHub` |
| `/exchangeHub` | `ExchangeHub` |
| `/datafeedHub` | `DataFeedHub` |
| `/dataIngestionHub` | `DataIngestionHub` |
| `/alertIngestionHub` | `AlertHub` |
| `/indicatorHub` | `IndicatorHub` |
| `/orderHub` | `OrderHub` |
| `/portfolioHub` | `PortfolioHub` |
| `/riskHub` | `RiskHub` |
| `/strategyHub` | `StrategyHub` |
| `/pivotMarkingHub` | `StrategyHub` ⚠️ |

⚠️ **Known issue**: `/pivotMarkingHub` is mapped to `StrategyHub` instead of its own hub class — likely a copy-paste leftover. Anything sent to `/pivotMarkingHub` currently lands in the strategy hub's group instead of a dedicated one.

Listens on `http://*:5091` inside the container (`Program.cs` calls `UseUrls` explicitly — without it, Kestrel falls back to port 8080 by default even with `EXPOSE 8080` removed from the Dockerfile, and binding to `localhost` instead of `*` makes the container unreachable from other containers or the host).

## CORS

Allowed origins come from `Cors:AllowedOrigins` in [appsettings.docker.json](appsettings.docker.json) (comma-separated). Any new service that needs to open a SignalR connection to this server must be added there, and that container's hostname needs to resolve on `live-network` (i.e. it must be a service in `docker-compose-live.yml`).

## Operations

### Compose

Service key: `signalr-live` in [docker-compose-live.yml](../../../../docker-compose-live.yml). Container: `signalr-server-live`. Host port `8098` → container port `5091`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d signalr-live
docker-compose -f docker-compose-live.yml -p live logs -f signalr-live
```

### Build

```bash
cd EventDrivenSystem/Source/Helpers/SignalRServer
docker build -t signalr-server-live-image:v1 -f Dockerfile .
docker-compose -f docker-compose-live.yml -p live up -d signalr-live   # recreate with the new image
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `docker` (selects `appsettings.docker.json`, i.e. which CORS origins are allowed) |

### Testing

SignalR hubs aren't plain REST, but the negotiate handshake responds to a normal POST — useful as a liveness check per hub:

```bash
curl -X POST http://localhost:8098/aggregationHub/negotiate?negotiateVersion=1
# → {"connectionId":"...","connectionToken":"...","availableTransports":[...]}
```

A `curl` client can't easily hold a live SignalR connection open — for real end-to-end testing use a `HubConnectionBuilder` client (see `Helpers/ReceiverSignalRApp` / `Helpers/SenderSignalRApp` for worked examples) or a browser.
