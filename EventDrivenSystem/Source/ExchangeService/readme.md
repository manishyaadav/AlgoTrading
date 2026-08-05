# Exchange Service

Timer-triggered Azure Function. Five daily timers publish exchange session-state events (`ExchangeEvent`) to Kafka, for the NSE and NFO exchanges, tracking the trading day lifecycle (initiated → pre-open → open → pre-close → closed).

- **No HTTP routes** — timer-only. The container's port only serves the Functions host default page.
- Publishes to Kafka topic `live-exchange-workflow-topic` (`ProducerTopicName` env var), consumed by `notification-live`.

⚠️ **If you rename a field on `ExchangeEvent`/`EventBase`/`CimplifyBase` via `[JsonPropertyName]`**: this service serializes with `System.Text.Json`, but `notification-live` deserializes with `Newtonsoft.Json`, which ignores that attribute entirely and falls back to matching the plain C# member name case-insensitively. A rename here that isn't a same-word case change (e.g. `ExchangeTimerAction` → `"action"`) silently fails to bind on the consumer side with no error at the point of the mistake — this already broke every exchange event once. See [NotificationService/README.md](../NotificationService/README.md#-cross-service-json-contract--a-real-bug-already-happened-here) before touching these shared model files.

| Function | Cron | Fires at (IST) |
|---|---|---|
| `ExchangeTimerInitFunction` | `0 0 9 * * *` | 09:00 |
| `ExchangeTimerPreOpenFunction` | `0 7 9 * * *` | 09:07 |
| `ExchangeTimerOpenFunction` | `0 15 9 * * *` | 09:15 |
| `ExchangeTimerPreCloseFunction` | `0 15 15 * * *` | 15:15 |
| `ExchangeTimerCloseFunction` | `0 30 15 * * *` | 15:30 |

## Operations

### Compose

Service key: `exchange-live` in [docker-compose-live.yml](../../../docker-compose-live.yml). Container: `exchange-service-live-container`. Host port `8094`.

```bash
docker-compose -f docker-compose-live.yml -p live up -d exchange-live
docker-compose -f docker-compose-live.yml -p live logs -f exchange-live
```

### Build

```bash
cd EventDrivenSystem/Source/ExchangeService
docker build -t exchange-service-live-image:v2 -f Dockerfile .
```

### Environment variables (set in compose)

| Var | Value |
|---|---|
| `AzureWebJobsStorage` | points at `azurite-live` |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `ASPNETCORE_ENVIRONMENT` | `docker` |
| `KAFKA_BROKER_URL` | `kafka-live:29092` |
| `ProducerTopicName` | `live-exchange-workflow-topic` |
| `EnvironmentName` | `live` |

### Testing without waiting for a timer

There's no HTTP trigger to invoke on demand. To test sooner, temporarily edit a cron schedule in `ExchangeTimerFunctions.cs` and rebuild, or watch **Kafdrop (http://localhost:9000)** around one of the scheduled times above.

---

# TimerTrigger - C<span>#</span>

The `TimerTrigger` makes it incredibly easy to have your functions executed on a schedule. This sample demonstrates a simple use case of calling your function every 5 minutes.

## How it works

For a `TimerTrigger` to work, you provide a schedule in the form of a [cron expression](https://en.wikipedia.org/wiki/Cron#CRON_expression)(See the link for full details). A cron expression is a string with 6 separate expressions which represent a given schedule via patterns. The pattern we use to represent every 5 minutes is `0 */5 * * * *`. This, in plain text, means: "When seconds is equal to 0, minutes is divisible by 5, for any hour, day of the month, month, day of the week, or year".

## Learn more

<TODO> Documentation


To have exchange service sending notifications to signalr server, we have to install below packages first

1. Microsoft.AspNetCore.SignalR.Client
2. Microsoft.Azure.Functions.Worker.Extensions.SignalRService

Add the SignalRServiceUrl, we have to use to send notifications to, here we have to select the signalr server along with hub name, as this is from exchange, we can select exchangeHub

"SignalRServiceUrl": "http://localhost:8098/exchangeHub" in local.settings.json to test from local environment

### Step 3 - Update the function to have signal server config


# Workflow - Steps / Flow

## Strcuture

Should be a schema, either (Avro, Protobuf or Json), and have the structure exchange and exchangeNotification be in that format, so that it can be maintained at one place, all other services. Exchange service is going to produce these, so this one is responsible for the schema. Other services which are going to consume from other service.

### Country.Exchange

    Exchange.Event.Id                            (common)
    Exchange.Event.Date                      
    Exchange.Event.SessionState                  (enum)
    Exchange.Event.Reason
    Exchange.Event.Priority                      (common)
    Exchange.Event.Time                          (common)
    Exchange.Event.ProducedBy                    (common)
    Exchange.Event.TimeZone                      (common)
    Exchange.Event.Version                       (common)
    
        Exchange.Item.Name
        Exchange.Item.State                      (enum)
        Exchange.Item.LastUpdated      
        Exchange.Item.UpdatedBy      

__Note__: Marked with (common) are common to any event and marked with (enum) are self explanatory
 
### ExchangeNotification

    Exchange.Notification.Id                    (common)
    Exchange.Notification.Time                  (common)
    Exchange.Notification.ProducedBy            (common)
    Exchange.Notification.TimeZone              (common)
    Exchange.Notification.Version               (common)
    Exchange.Notification.SessionStateChanged   
    Exchange.Notification.SessionState


### Exchange.SessionState
    Weekday
    Holiday
    InSession

### ExchangeItem.State
    Initiated
    PreOpened
    Opened
    PreClosed
    Closed



## Exchange Details - All

| Exchange  | Segment   | InstrumentType    | InstrumentName    | InstrumentExpiry  | Tradable  |
|---------- |---------- |----------         |--------------     |---------------    |---------- |
| NSE       | Indices   | Equity            | Nifty 50          | Never             | false     |
| NSE       | Indices   | Equity            | Bank Nifty        | Never             | false     |
| NSE       | NSE       | Equity            | Hdfc Bank         | Never             | true      |
| NFO       | Options   | Call              | NiftyMay202425000 | Monthly           | true      | 
| NFO       | Options   | Put               | NiftyMay202425000 | Monthly           | true      |
| NFO       | Indices   | Futures           | NiftyMay2024      | Monthly           | true      |
| NFO       | Indices   | Futures           | BankNiftyMay2024  | Monthly           | true      |


## Exchange

| Name | Segment    | 
|------| -----------|
| NSE  | Indices    | 
| NSE  | Equity     | 
| NFO  | Derivatives| 
---

### ExchangeNameEnum
- NSE
- NFO
### ExchangeSegmentEnum
- Indices
- Equity
- Derivatives

### Exchange.Events
- DayChanged
- Initiated
- PreOpened
- Opened
- PreClosed
- Closed

## Instrument
| Name      | Type      | Expiry    | Tradable  | Token                  | FeedSource |
|-----------|---------  |--------   |---------- |---------------------   | -----------|
| Nifty 50  | Indices   | Never     | false     | nifty-50               | tradingview|
| Bank Nifty| Indices   | Never     | false     | bank-nifty             | tradingview|
| Nifty 50  | Futures   | Monthly   | true      | nifty{mmm}{yyyy}fut    | tradingview|
| Bank Nifty| Futures   | Monthly   | true      | banknifty{mmm}{yyyy}fut| tradingview|

---

### DataIngestion - Trading View

| SourceToken       | CimplifyToken             | Frequency     | Source        |
| ------------------| ------------------------- |-------------- | --------------|
| NIFTY             | nifty-50                  | Minute        | TradingView   |
| BANKNIFTY         | bank-nifty                | Minute        | TradingView   |
| NIFTY1!           | nifty{mmm}{yyyy}fut       | Minute        | TradingView   |
| NIFTY             | banknifty{mmm}{yyyy}fut   | Minute        | TradingView   |

### Dataingestion - NSE

| SourceToken       | CimplifyToken             | Frequency     | Source|
| ------------------| ------------------------- |-------------- | ------|
| NIFTY             | nifty-50                  | Day           | NSE   |
| BANKNIFTY         | bank-nifty                | Day           | NSE   |
| NIFTY1!           | nifty{mmm}{yyyy}fut       | Day           | NSE   |
| NIFTY             | banknifty{mmm}{yyyy}fut   | Day           | NSE   |

### DataIngetstion - Kite

| SourceToken       | CimplifyToken             | Frequency     | Source|
| ------------------| ------------------------- |-------------- | ------|


### DataFeed.Packet
- SourceToken
- DataPakcetEnum (Stream, Ohlc)
- DataPacket
    - LastPrice (In case of Stream)
    - Open      (Ohlc)
    - High      (Ohlc)
    - Low       (Ohlc)
    - Close     (Ohlc)
    - Volume    (Ohlc)

### DataFeed.Event
- NewDataReceived



## Flow

### At 12:01 AM Daily

#### __Step 1__
At every day at 12:01 midnight (which leads to our system to be running 24/7, that may be challenging in you are doing that in local system, in case of cloud deployment it's fine)

#### __Step 2__ 
Creates a CountryNotification to send notification that it's kicked in 

#### __Step 3__
Creates a CountryEvent and sets 
- Country.Producer to "country.service"
- Country.Date to CurrentDate
- Country.State 
    -   to Weekend if it's a weekend
    -   otherwise get the holiday list from repository pull only greater than CurrentDate
        -   set Country.Holiday to current holiday with reason
        -   set Country.NextHoliday to next holiday with reason

#### __Step 4__
Creates a CountryNotification to send notification that it's Starting sending to rabbitmq and kafka topic 

#### __Step 5__
Send the CountryEvent to rabbitmq and kafka topic

#### __Step 6__
Create a CountryNotificadtion to send notification that it's sent the Country Event to rabbitmq and kafka topic

### Kafka Topic Trigger 

Once data is received in Kafaka topic "country", triggers a workflow
#### __step 2__
Creates a CountryNotification to send notification that data is recieved in topic

### __step 3__

Based on the Country Event State
    - NORMAL
        - Creates a Exchange Notification that it's kicked in 
        - Creates a ExchangeEvent and set
            - 
            
    - HOLIDAY
    - WEEKEND

# Implementation 
### Base
    
````csharp
    public class CimplifyBase
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("priority")]
        public EventPriority Priority { get; set; } = EventPriority.Medium;

        [JsonPropertyName("time")]
        public DateTime Time { get; set; } = DateTime.Now;

        [JsonPropertyName("producer")]
        public string Producer { get; set; } = "";

        [JsonPropertyName("timeZone")]
        public string TimeZoneId { get; set; } = "Asia/Kolkata";

        [JsonIgnore]
        public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";
    }
````



### CimplifyTypeEnum

````csharp
    public enum CimplifyTypeEnum
    {
        Event = 1,
        Notification = 2
    }
````
### EventBase

````csharp
    public class EventBase : CimplifyBase
    {
        [JsonPropertyName("type)]
        public CimplifyTypeEnum Type {get;set;} = CimplifyTypeEnum.Event;
    }
````      

### NotificationBase

````csharp
    public class NotificationBase : CimplifyBase
    {
        [JsonPropertyName("type)]
        public string Type {get;set;} = CimplifyTypeEnum.Notification;
    }
````   

As both the Events and Notifications looks same, we can have a Base class named CimplifyBase, which has all the common properties, then we will be having EventBase and NotificationBase derived from Base and in that class just added a Type which has default property as Event or Notification.


### CountryBase

````csharp
    public class CountryBase : EventBase
    {
        [JsonPropertyName("name)]
        public string Name {get;set;} = "India";

        [JsonPropertyName("currency)]
        public string Currency {get;set;} = "Rs";
    }
````

### CountryStateEnum

````csharp
    public enum CountryStateEnum
    {
        Weekend = 1,
        Holiday = 2,
        Normal = 3
    }
````

### HolidayBase

````csharp
    public class HolidayBase
    {
        [JsonPropertyName("date")]
        public DateTime Date {get;set;}

        [JsonPropertyName("reason")]
        public string Reason {get;set;}
    }
````

### HolidayDataTransferModel

can be ussed for the blob storage model

````csharp
    public class HolidayDataTransferModel : HolidayBase
    {
    }
````

### HolidayItem
````csharp
    public class HolidayItem : HolidayBase
    {

    }
````

### CountryEvent

Country Event

````csharp
    public class CountryEvent : CountryBase
    {
        [JsonPropertyName("date)]
        public DateTime Date {get;set;} = DateTime.Now;

        [JsonPropertyName("state)]
        public CountryStateEnum State {get;set;}

        [JsonPropertyName("holiday")]
        public HolidayItem? Holiday {get;set;}

        [JsonPropertyName("nextHoliday")]
        public HolidayItem NextHoliday { get; set; } = CalculateLastWorkingDayOfCurrentYear();

        public static DateTime CalculateLastWorkingDayOfCurrentYear()
        {
            int year = DateTime.Now.Year;
            DateTime lastDay = new DateTime(year, 12, 31);

            // If last day is Saturday or Sunday, find the previous Friday
            switch (lastDay.DayOfWeek)
            {
                case DayOfWeek.Saturday:
                    lastDay = lastDay.AddDays(-1); // Previous Friday
                    break;
                case DayOfWeek.Sunday:
                    lastDay = lastDay.AddDays(-2); // Previous Friday
                    break;
            }

            return lastDay;
        }
    }
````