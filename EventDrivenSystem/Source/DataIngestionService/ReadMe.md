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

#### Where WindowsStartTime becomes IST

TradingView's raw payload — and everything published to `live-tradingview-ohlc-topic` — carries `WindowsStartTime` in this UTC form. `DataIngestionTradingViewFunction`'s `CreateDataForIngestion`/`CreateMockDataForIngestion` (in `DataIngestionFunctions.cs`) convert it to IST **once**, right here, via `DateTimeHelper.ConvertToIndianTime(...).DateTime`, before republishing the enriched event to `live-dataingestion-ohlc-topic`. Every stage downstream of that point — all 6 `AggregationService` timeframe functions, `NotificationService`'s caches, the dashboard — just copies the field forward verbatim, so this is the single place the conversion needs to happen for the whole aggregation pipeline to read as IST end to end. `live-tradingview-ohlc-topic` itself (and `NotificationService`'s parallel `DataFeed:TradingView:{Ticker}` cache, which consumes it directly) is **not** converted — it still carries the raw UTC value, since that's a separate, earlier branch this fix doesn't touch.

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
| `RedisConnectionString` | `redis-live:6379` — needed by `SessionCloseGapFillFunction` (see below) |
| `Features:GapFill` | `true`/`false` — master on/off switch for gap-filling, no redeploy needed to flip it |
| `GapFill:WindowMinutesBeforeClose` | `15` — how many minutes before close count as the CAS window |
| `GapFill:GraceMinutes` | `1` — extra minutes of latency tolerance before a missing minute is treated as genuinely missing, so a slightly-late real candle always wins over a synthetic fill |
| `GapFill:SessionCloseTime` | `15:30:00` — session close, in case it's ever not 15:30 (e.g. Muhurat trading) |

### HTTP routes

Route prefix: `api` (default). Both routes are `POST`.

**`funcTradingViewDataFeed`** — `TradingViewMinDataFeedFunction`. Parses the TradingView OHLC payload and publishes it to the `live-tradingview-ohlc-topic` Kafka topic. This is the one TradingView alerts should hit — see the [root README](../../../README.md#ngrok-tradingview-webhook-tunnel) for the ngrok webhook URL.

```bash
curl -X POST http://localhost:8080/api/dataingestion/tradingview/funcTradingViewDataFeed \
  -H "Content-Type: application/json" \
  -d '{
        "Ticker": "NIFTY",
        "Timeframe": 1,
        "Open": 24590.00, "High": 24591.50, "Low": 24587.35, "Close": 24588.75,
        "Volume": 381387,
        "EventTime": "2026-08-03T12:33:00",
        "Time": "2026-08-03T12:33:00"
      }'
```

Field names here must match `TradingViewDataEvent`'s `[JsonPropertyName]` attributes exactly — it's `Ticker`/`Time`/a numeric `Timeframe`, not `SourceToken`/`WindowsStartTime`/a string (this example previously had all three wrong, which throws a plain `{"error":"Invalid JSON format"}` with no further detail — found while manually verifying `SessionCloseGapFillFunction`, see below).

**`funcTradingViewAlertTestSQL`** — `TradingViewAlertToSQLFunction`. Writes the alert straight to SQL (no Kafka involved). Requires a reachable SQL Server — not part of this compose stack, so this will fail until one is configured.

```bash
curl -X POST http://localhost:8080/api/dataingestion/tradingview/funcTradingViewAlertTestSQL \
  -H "Content-Type: application/json" \
  -d '{"Ticker":"NIFTY","Message":"NIFTY Stop Loss on Call at 24588.75","EventTime":"2026-08-03T12:33:00","Time":"2026-08-03T07:03:00Z"}'
```

## Gap-filling the CAS window near close

SEBI's Closing Auction Session (CAS) for index derivatives means TradingView/Kite genuinely stop
streaming 1-min ticks for the index in the last ~15 minutes before close — no trading happens until
the single auction-uncrossing print lands, typically right at 15:29. Confirmed against real
NIFTY/BANKNIFTY data: candles run normally through 15:14, nothing arrives 15:15-15:28, then one real
print lands at 15:29. That's expected silence, not an outage.

`SessionCloseGapFillFunction` (a `[TimerTrigger("0 * * * * *")]`, fires every minute) forward-fills
the last known price for whichever minutes inside the configured window (`GapFill:*` env vars above)
never got a real candle — publishing a flat `O=H=L=C=lastClose`, `Volume=0` candle (CAS really does
mean zero trades, not an approximation) onto `live-dataingestion-ohlc-topic`, tagged
`producerBy: "dataingestion.gapfill"` so it's distinguishable from a real candle
(`"dataingestion.service"`) later if needed. This only ever acts inside that window — a gap anywhere
else in the session is left alone and stays a genuine "missing" bucket on the dashboard, because
outside the CAS window a gap really does mean the pipeline broke, and silently papering over that
would hide a real problem.

Publishing onto the exact same topic and `DataIngestionMinDataEvent` shape a real candle uses is what
makes this transparent to everything downstream: `NotificationService`, all 6 `AggregationService`
timeframes, and `ohlc-live`'s `LiveCandlePersistenceFunction` each already consume this topic
independently and have no way to tell a filled candle from a real one, so every aggregation timeframe
and the Azurite blob self-correct with zero code changes on their end — verified live end-to-end
(seeded a real candle via the webhook, let the timer fill two consecutive missing minutes with a flat
copy of the last close, then sent a later real candle and confirmed it correctly pre-empted the fill
for its own minute — `NotificationService`'s Redis cache picked up every one of those candles,
synthetic and real, without any change on its side).

Needs its own "last known real candle" cache to copy from — `DataIngestionFunctions.cs` writes
`Ingestion:LastCandle:{provider}:{ticker}` (3-day TTL) right after every real candle it publishes.
This is new state DataIngestionService didn't have before (it was previously a fully stateless
webhook-in/Kafka-out transform) — hence the new `RedisConnectionString` dependency above.