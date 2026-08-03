# EventDrivenSystem

Event-driven system design for algorithmic trading, with a focus on Indian markets (NSE/NFO, TradingView-style symbols, `Asia/Kolkata`).

## Overview

This repository implements an **event-driven pipeline** for algo trading: ingest **TradingView** (and related) feeds via **HTTP webhooks**, route events through **Kafka** (and **RabbitMQ** where used), run **scheduled and topic-triggered** logic in **.NET Azure Functions** (isolated worker), and push **real-time updates** to dashboards through a **self-hosted SignalR** server. Supporting services use **Redis**, **PostgreSQL**, and **Azurite** (Azure Storage emulation) for local and containerized development.

At a high level: **feeds trigger Functions that publish to Kafka; other Functions react on timers or topics to manage country/exchange state, aggregate data, support strategy/portfolio logic, and broadcast over SignalR to Blazor UIs.**

## Current status

| Component | State |
|-----------|--------|
| TradingView generating alerts | ONLINE |
| TradingView data → RabbitMQ queue | DOCKER |
| Historical data service (mock) | DOCKER |

## Main capabilities

### Data ingestion

- HTTP endpoints (for example TradingView webhooks) accept payloads, apply rate limiting, and produce to Kafka.
- Token normalization and stream vs OHLC-style packets are described alongside exchange domain docs under `Source/ExchangeService/readme.md`.

### Market and calendar orchestration

- **Country service**: country/calendar state (weekend, holiday, normal), integrated with holiday data and Kafka.
- **Exchange service**: exchange session lifecycle (for example initiated → opened → closed) and Kafka producers for exchange events; event/notification schema notes live in `Source/ExchangeService/readme.md`.

### Aggregation and analytics

- **Aggregation service** uses Kafka and Redis for rollups and related processing (see also pivot/marking aggregation under `Source/PivotMarkingAggregationFunction`).
- Stream analytics over Kafka exists in the repo; optional services may be commented out in `docker-compose.yml`.

### Historical and mock data

- **HistoricaDataService**: OHLC and Holiday Azure Function apps.
- **Mock stream** projects for testing without a live feed.

### Strategy, portfolio, and admin

- **StrategyService**: strategy models, rules, and backtest/performance-related types.
- **PortfolioService**: portfolio-oriented functions.
- **AdminServices**: administrative APIs/functions.

### Notifications and UI

- **Notification service**: connects Kafka, Redis, and SignalR for backend-to-UI notifications.
- **SignalR server** (`Source/Helpers/SignalRServer`): hubs for domains such as exchange, data ingestion, aggregation, strategy, portfolio, orders, and risk.
- **consoleService** / **Blazor** projects: dashboard and console UIs (several iterations in tree).

## Infrastructure (`docker-compose.yml`)

Typical local stack (names and images may vary by branch):

| Component | Role |
|-----------|------|
| Kafka + Zookeeper + Kafdrop | Event bus and topic inspection |
| RabbitMQ | Message queues (used with some flows and helpers) |
| Redis (Stack) | Caching and aggregation-related state |
| PostgreSQL | Relational data |
| Azurite | Azure Storage emulation for Functions |
| Service images | Data ingestion, holiday, OHLC, exchange, strategy, aggregation, country, notification, SignalR, etc. |

Timezone is set to **Asia/Kolkata** across services where configured.

## Repository layout (high level)

- `Source/DataIngestionService` — ingest TradingView and related HTTP triggers.
- `Source/CountryService`, `Source/ExchangeService` — calendar and exchange lifecycle.
- `Source/AggregationService`, `Source/PivotMarkingAggregationFunction` — aggregation workloads.
- `Source/StrategyService`, `Source/PortfolioService` — strategy and portfolio logic.
- `Source/NotificationService`, `Source/Helpers/SignalRServer` — notifications and real-time hub.
- `Source/HistoricaDataService`, `Source/MockStreamService` — historical and mock feeds.
- `Source/consoleService`, `Source/AdminServices` — UIs and admin.
- `Source/SharedLibrary` — shared types and helpers.
- `docker-compose.yml` — local orchestration of dependencies and app containers.

---

*This file is intended to stay in sync with the architecture; update the **Current status** and **Infrastructure** sections when deployment or feature flags change.*
