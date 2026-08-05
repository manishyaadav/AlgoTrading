# Country Service

Daily timer-triggered Azure Function. Once a day it determines the current "country state" (normal / weekend / holiday, using `holiday-live` as the holiday-calendar source) and publishes a `CountryEvent` to Kafka for downstream services to react to.

- **No HTTP routes** — this is timer-only. The container's port only serves the Azure Functions host default page.
- Trigger: `CountryTimerFunction`, cron `0 1 0 * * *` — daily at **00:01 IST**.
- Calls `holiday-live` over HTTP to resolve the holiday calendar.
- Publishes to Kafka topic `live-country-workflow-topic`, consumed by `notification-live`.

⚠️ **If you rename a field on `CountryEvent`/`EventBase`/`CimplifyBase` via `[JsonPropertyName]`**: this service serializes with `System.Text.Json`, but `notification-live` deserializes with `Newtonsoft.Json`, which ignores that attribute and matches the plain C# member name case-insensitively instead. `CountryEvent` currently works only because its renames (`"name"`, `"date"`, `"state"`) happen to be lowercased versions of the real property names — Newtonsoft's case-insensitive fallback rescues it by coincidence, not by design. `ExchangeEvent` had the identical pattern with a rename that *wasn't* just a case change, and it silently broke every event. See [NotificationService/README.md](../NotificationService/README.md#-cross-service-json-contract--a-real-bug-already-happened-here) before touching these shared model files.

## Operations

### Compose

Service key: `country-live` in [docker-compose-live.yml](../../../docker-compose-live.yml). Container: `country-service-live-container`. Host port `8093`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d country-live
docker-compose -f docker-compose-live.yml -p live logs -f country-live
```

### Build

```bash
cd EventDrivenSystem/Source/CountryService
docker build -t country-service-live-image:v2 -f Dockerfile .
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `AzureWebJobsStorage` | points at `azurite-live` |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `ASPNETCORE_ENVIRONMENT` | `docker` |
| `KAFKA_BROKER_URL` | `kafka-live:29092` |
| `HOLIDAY_API` | `http://holiday-live` |
| `ProducerTopicName` | `live-country-workflow-topic` |
| `EnvironmentName` | `live` |

### Testing without waiting for the daily timer

There's no HTTP trigger to invoke on demand. To test sooner, either temporarily edit the cron schedule in `CountryTimerFunction` and rebuild, or watch **Kafdrop (http://localhost:9000)** at 00:01 IST for the next natural run.
