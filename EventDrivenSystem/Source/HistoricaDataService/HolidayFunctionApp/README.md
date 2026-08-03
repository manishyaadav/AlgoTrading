# Holiday Function App

Read-only HTTP API backed by blob storage (`azurite-live`). Serves the holiday calendar that `country-live` calls into to compute daily country state.

## HTTP routes

Route prefix: `api` (default).

**`GET|POST /api/GetHolidays`**

| Query param | Meaning |
|---|---|
| `blobName` | `master` reads `holiday/holidaymaster.csv` (requires `year`); anything else reads `holiday/current.csv` |
| `year` | required only when `blobName=master` |

```bash
curl "http://localhost:8091/api/GetHolidays?blobName=master&year=2026"
curl "http://localhost:8091/api/GetHolidays?blobName=current"
```

**`GET|POST /api/IsExchangeHoliday`**

| Query param | Meaning |
|---|---|
| `exchangename` | exchange to check against today's date (IST), using `holiday/current.csv` |

```bash
curl "http://localhost:8091/api/IsExchangeHoliday?exchangename=NSE"
```

## Operations

### Compose

Service key: `holiday-live` in [docker-compose-live.yml](../../../../docker-compose-live.yml). Container: `holiday-service-live-container`. Host port `8091`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d holiday-live
docker-compose -f docker-compose-live.yml -p live logs -f holiday-live
```

### Build

```bash
cd EventDrivenSystem/Source/HistoricaDataService/HolidayFunctionApp
docker build -t holiday-service-live-image:v2 -f Dockerfile .
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `AzureWebJobsStorage` | points at `azurite-live` |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `ASPNETCORE_ENVIRONMENT` | `docker` |

Note: the underlying CSV blobs (`holiday/holidaymaster.csv`, `holiday/current.csv`) must actually exist in the `azurite-live` blob container for these routes to return data — this service only reads them, it doesn't seed them.
