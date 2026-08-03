# Data Ingestion Service

## Overview
The Data Ingestion Service is specifically designed to capture and process TradingView webhooks and alerts through a local Ngrok tunnel. This service acts as a bridge between TradingView signals and your internal data processing pipeline, seamlessly routing data to SQL databases and Kafka topics for further processing by various listeners.

## System Architecture
```
TradingView Alerts → Ngrok Tunnel → Data Ingestion Service → SQL/Kafka → Listeners
```

## System Specifications

### Rate Limits
- **TradingView**: 15 alerts per 3 minutes
- **NGROK Free HTTP Requests**: 20,000 requests per month

### Current Implementation
- TradingView webhook integration through Ngrok tunnel
- Local development environment with seamless data flow
- Event-driven architecture using Azure Functions
- Real-time data routing to SQL and Kafka

## Features
- Webhook endpoint for TradingView alerts
- Ngrok tunnel for local development and testing
- Real-time data processing and validation
- Dual storage strategy (SQL + Kafka)
- Automated data distribution to listeners
- Time zone handling (Asia/Kolkata default)
- Error handling and retry mechanisms

## Local Development Setup
1. Start the Data Ingestion Service locally
2. Launch Ngrok tunnel pointing to your local service
3. Configure TradingView webhooks with Ngrok URL
4. Verify SQL database connection
5. Ensure Kafka topics are created and accessible

## Data Sources and Processing

### Supported Data Sources
The service is designed to handle multiple data sources with varying characteristics:
- Data formats
- Update frequencies
- Rate limits
- Cost structures
- API specifications

### Current Implementation
1. **Primary Data Source**: TradingView
   - Instruments covered:
     - NIFTY
     - BANKNIFTY
     - NIFTY1! (Future contract)
     - BANKNIFTY1! (Future contract)
   - Data type: OHLC (Open, High, Low, Close)

### Data Flow Pipeline
1. **TradingView Alerts**
   - Configured webhooks in TradingView
   - Alerts triggered based on trading conditions
   - JSON payload sent to Ngrok URL

2. **Ngrok Tunnel**
   - Provides public URL for local service
   - Forwards requests to local development environment
   - Enables webhook testing without deployment

3. **Data Processing**
   - Receives webhook payload
   - Validates data structure and content
   - Processes trading signals and alerts

4. **Data Storage**
   - Primary storage in SQL database
   - Parallel streaming to Kafka topics
   - Ensures data persistence and real-time processing

5. **Listener Integration**
   - Multiple listeners monitoring Kafka topics
   - Independent processing of trading signals
   - Specialized handling based on alert type

### Future Contract Handling
- Special handling for instruments with '!' suffix
- Automated contract identification
- Mapping to standard instrument names

## Technical Implementation Details

### TradingView Integration

#### DateTime Handling
The service handles two different time formats:
1. **EventTime** (Local Time)
   - Format: `YYYY-MM-DDTHH:mm:ss`
   - Example: `"2024-05-23T09:33:02"`
   - TimeZone: Asia/Kolkata (hardcoded)
   - Used for: Event logging and local processing

2. **Time** (UTC)
   - Format: `YYYY-MM-DDTHH:mm:ssZ`
   - Example: `"2024-05-23T04:02:00Z"`
   - TimeZone: UTC (indicated by 'Z' suffix)
   - Used for: Cross-system synchronization

#### Alert Format
```json
{
    "Ticker": "{{ticker}}",
    "Message": "{{ticker}} Stop Loss on Call at {{close}}",
    "EventTime": "{{timenow}}",
    "Time": "{{time}}"
}
```

### Planned Integrations

#### Kite Connect
- Status: Planned
- Purpose: Direct market data access
- Implementation: TBD

#### NSE (National Stock Exchange)
- Status: Planned
- Purpose: Reference data and market updates
- Implementation: TBD

## Technical Architecture

### Components
1. **Webhook Endpoint (Azure Functions)**
   - HTTP-triggered function for TradingView alerts
   - Payload validation and processing
   - Error handling and logging

2. **Ngrok Integration**
   - Local tunnel configuration
   - URL management for TradingView
   - Request forwarding and monitoring

3. **Data Storage Layer**
   - SQL database for persistent storage
   - Real-time Kafka topics
   - Transaction management
   - Data consistency checks

4. **Listener Framework**
   - Multiple specialized listeners
   - Independent processing pipelines
   - Scalable architecture

5. **Monitoring and Logging**
   - Webhook reception tracking
   - Data flow monitoring
   - Error logging and alerts
   - Performance metrics

### Error Handling
- Rate limit exceeded handling
- Data validation errors
- Connection failure recovery
- Retry mechanisms

### Security
- API authentication
- Data encryption
- Rate limiting
- Access control

## Development and Deployment

### Prerequisites
- .NET 8.0
- Azure Functions runtime
- Kafka cluster
- SQL Server instance (only needed for `TradingViewAlertToSQLFunction` — not provisioned in `docker-compose-live.yml` today, so that route will fail if called)

### Configuration
- Environment-specific settings
- API keys and secrets
- Connection strings
- Rate limit configurations

## Operations

### Compose

Service key: `dataingestion` in [docker-compose-live.yml](../../../docker-compose-live.yml). Container: `dataingestion-service`. Host port `8080`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d dataingestion
docker-compose -f docker-compose-live.yml -p live logs -f dataingestion
```

### Build

```bash
cd EventDrivenSystem/Source/DataIngestionService/DataIngestionService
docker build -t dataingestion-service-image:v6 -f Dockerfile .
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `AzureWebJobsStorage` | points at `azurite-live` (blob/queue/table) |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `ASPNETCORE_ENVIRONMENT` | `docker` |
| `KAFKA_BROKER_URL` | `kafka-live:29092` |

### HTTP routes

Route prefix: `api` (default). Both routes are `POST`.

**`funcTradingViewDataFeed`** — `TradingViewMinDataFeedFunction`. Parses the TradingView OHLC payload and publishes it to the `live-tradingview-ohlc-topic` Kafka topic. This is the one TradingView alerts should hit — see the [root README](../../../README.md#ngrok-tradingview-webhook-tunnel) for the ngrok webhook URL.

```bash
curl -X POST http://localhost:8080/api/dataingestion/tradingview/funcTradingViewDataFeed \
  -H "Content-Type: application/json" \
  -d '{
        "SourceToken": "NIFTY",
        "Timeframe": "1",
        "Open": 24590.00, "High": 24591.50, "Low": 24587.35, "Close": 24588.75,
        "Volume": 381387,
        "EventTime": "2026-08-03T12:33:00",
        "WindowsStartTime": "2026-08-03T12:33:00"
      }'
```

**`funcTradingViewAlertTestSQL`** — `TradingViewAlertToSQLFunction`. Writes the alert straight to SQL (no Kafka involved). Requires a reachable SQL Server — not part of this compose stack, so this will fail until one is configured.

```bash
curl -X POST http://localhost:8080/api/dataingestion/tradingview/funcTradingViewAlertTestSQL \
  -H "Content-Type: application/json" \
  -d '{"Ticker":"NIFTY","Message":"NIFTY Stop Loss on Call at 24588.75","EventTime":"2026-08-03T12:33:00","Time":"2026-08-03T07:03:00Z"}'
```