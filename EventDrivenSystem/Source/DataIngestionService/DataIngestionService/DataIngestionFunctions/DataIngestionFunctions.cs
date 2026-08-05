using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedLibrary.Enums.DataFeed;
using SharedLibrary.Events;
using SharedLibrary.Events.DataIngestion;
using SharedLibrary.Helpers;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;
using DataIngestionFunctions.SharedLibrary.Events;
using NAudio.Wave;
using SharedLibrary.Events.AlertIngestion;
using SharedLibrary.Enums.AlertFeed;

namespace DataIngestionFunctions
{
    public class DataIngestionFunctions
    {
        private readonly ILogger<DataIngestionFunctions> _logger;
        private readonly IProducer<string, string> _producer;
        private static string _producerTopicName = $"live-dataingestion-ohlc-topic";
        private static string _mockProducerTopicName = _producerTopicName;
        private static string _producerAlertTopicName = $"live-alertingestion-alert-topic";

        public DataIngestionFunctions(ILoggerFactory loggerFactory, IProducer<string, string> producer)
        {
            _logger = loggerFactory.CreateLogger<DataIngestionFunctions>();
            _producer = producer;
        }

        [Function("DataIngestionTradingViewFunction")]
        public async Task DataIngestionTradingViewFunction(
                            [KafkaTrigger("%KAFKA_BROKER_URL%",
                        "live-tradingview-ohlc-topic",
                        AuthenticationMode = BrokerAuthenticationMode.Plain,
                        ConsumerGroup = "live-tradingview-dataingestion-consumer")] string eventDataJson,
                            FunctionContext context)
        {
            _logger.LogInformation("Live Data Ingestion, Base 1M, Kafka triggered function: Data Ingestion for TradingView Starting");
            var result = await ProcessMessage(eventDataJson, true); 
        }

        [Function("MockDataIngestionOHLCFunction")]
        public async Task MockDataIngestionOHLCFunction(
                            [KafkaTrigger("%KAFKA_BROKER_URL%",
                        "mock-ohlc-1m-topic",
                        AuthenticationMode = BrokerAuthenticationMode.Plain,
                        ConsumerGroup = "mock-tradingview-dataingestion-consumer")] string eventDataJson,
                            FunctionContext context)
        {
            _logger.LogInformation("Mock Data Ingestion, Base 1M, Kafka triggered function: Data Ingestion for TradingView Starting");
            var result = await ProcessMessage(eventDataJson, false);
        }

        private async Task<bool> ProcessMessage(string eventDataJson, bool isLive)
        {
            var eventDataValue = string.Empty;
            JsonDocument document = JsonDocument.Parse(eventDataJson);
            JsonElement root = document.RootElement;

            // Access the "Value" property
            if (document.RootElement.TryGetProperty("Value", out JsonElement valueElement))
            {
                eventDataValue = valueElement.GetString() ?? string.Empty;
            }

            if (isLive)
            {
                var dataIngestionEvent =JsonSerializer.Deserialize<TradingViewDataEvent>(eventDataValue);

                if (dataIngestionEvent != null)
                {
                    var dataToSend = CreateDataForIngestion(dataIngestionEvent);

                    //await SendToDataIngestionTopic("live-dataingestion-ohlc-topic", dataToSend);
                    var key = $"{dataToSend.Ticker}:{dataToSend.Timeframe}Min";
                    var kafkaMessage = JsonSerializer.Serialize(dataToSend);
                    await ProduceToKafka(_producerTopicName, key, kafkaMessage, _logger);
                    _logger.LogInformation("Live Data Ingestion, Base 1M, C# Kafka trigger function processed a message: {EventData}", eventDataValue);
                }
            }
            else
            {
                var dataIngestionEvent = JsonSerializer.Deserialize<MockOhlcDataEvent>(eventDataValue);

                if (dataIngestionEvent != null)
                {
                    var dataToSend = CreateMockDataForIngestion(dataIngestionEvent);

                    //await SendToDataIngestionTopic("live-dataingestion-ohlc-topic", dataToSend);
                    var key = $"{dataToSend.Ticker}:{dataToSend.Timeframe}Min";
                    var kafkaMessage = JsonSerializer.Serialize(dataToSend);
                    await ProduceToKafka(_mockProducerTopicName, key, kafkaMessage, _logger);
                    _logger.LogInformation("Mock Data Ingestion, Base 1M, C# Kafka trigger function processed a message: {EventData}", eventDataValue);
                }
            }
             
            

            return true;
        }        

        //[Function("AlertIngestionTradingViewFunction")]
        //public async Task AlertIngestionTradingViewFunction(
        //                    [KafkaTrigger("%KAFKA_BROKER_URL%",
        //                "live-tradingview-alert-topic",
        //                AuthenticationMode = BrokerAuthenticationMode.Plain,
        //                ConsumerGroup = "live-tradingview-alert-consumer")] string eventDataJson,
        //                    FunctionContext context)
        //{
        //    _logger.LogInformation("Trading View Alert, Kafka triggered function: Alert received from TradingView Starting");

        //    var eventDataValue = string.Empty;

        //    using JsonDocument document = JsonDocument.Parse (eventDataJson);
        //    JsonElement root = document.RootElement;

        //    // Access the "Value" property
        //    if (document.RootElement.TryGetProperty("Value", out JsonElement valueElement))
        //    {
        //        eventDataValue = valueElement.GetString() ?? string.Empty; 
        //    }

        //    var alertEvent = JsonSerializer.Deserialize<TradingViewAlertEvent>(eventDataValue);
        //    if (alertEvent != null)
        //    {
        //        var dataToSend = CreateAlertDataForIngestion(alertEvent);

        //        //await SendToDataIngestionTopic("live-dataingestion-ohlc-topic", dataToSend);
        //        var key = $"{dataToSend.Ticker}:Price:Alert";
        //        var kafkaMessage = JsonSerializer.Serialize(dataToSend);
        //        await ProduceToKafka(_producerAlertTopicName, key, kafkaMessage, _logger);
        //        _logger.LogInformation($"Data Ingestion, Base 1M, C# Kafka trigger function processed a message: {eventDataValue}");
        //    }
        //}

        private AlertIngestionEvent CreateAlertDataForIngestion(TradingViewAlertEvent alertIngestionEvent)
        {
            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);

            return new AlertIngestionEvent ()
            {
                SourceToken = alertIngestionEvent.SourceToken,
                Ticker = GetTickerFromSourceToken(alertIngestionEvent.SourceToken.ToLower(), alertIngestionEvent.WindowsStartTime),
                WindowsStartTime = alertIngestionEvent.WindowsStartTime,
                
                AlertType = alertIngestionEvent.Type,
                Timeframe = int.Parse(alertIngestionEvent.Timeframe),
                Direction = int.Parse(alertIngestionEvent.Direction),
                Level = int.Parse(alertIngestionEvent.Level),
                PointVal=decimal.Parse(alertIngestionEvent.PointVal),
                Producer = "dataingestion.service",
                ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset),
                DataSource = AlertFeedSourceEnum.TradingView,
                DataType = AlertFeedTypeEnum.PriceAlert
            };
        }

        private async Task ProduceToKafka(string topicName, string key, string value, ILogger logger)
        {
            try
            {
                var deliveryReport = await _producer.ProduceAsync
                        (
                            topicName, 
                            new Message<string, string> 
                            {
                                Key = key,
                                Value = value 
                            }
                        );
                logger.LogInformation($"Delivered message to: {deliveryReport.TopicPartitionOffset}");
            }
            catch (ProduceException<string, string> e)
            {
                logger.LogError($"Delivery failed: {e.Error.Reason}");
            }
        }
        // The single point where a candle's WindowsStartTime gets converted from the wire's UTC
        // value (what TradingView actually sends) to IST — every downstream stage (all 6 aggregation
        // levels, the notification caches, the dashboard) just copies this field forward verbatim
        // from here on, so converting once here is what makes it read as IST everywhere, not a
        // change needed in each of those places individually.
        private DataIngestionMinDataEvent CreateDataForIngestion(TradingViewDataEvent dataIngestionEvent)
        {
            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);
            var windowsStartTimeIst = DateTimeHelper.ConvertToIndianTime(dataIngestionEvent.WindowsStartTime).DateTime;

            return new DataIngestionMinDataEvent()
            {
                SourceToken = dataIngestionEvent.SourceToken,
                Ticker = GetTickerFromSourceToken(dataIngestionEvent.SourceToken.ToLower(), dataIngestionEvent.WindowsStartTime),
                WindowsStartTime = windowsStartTimeIst,
                Timeframe = dataIngestionEvent.Timeframe,
                Open = dataIngestionEvent.Open,
                High = dataIngestionEvent.High,
                Low = dataIngestionEvent.Low,
                Close = dataIngestionEvent.Close,
                Volume = dataIngestionEvent.Volume,                
                Producer = "dataingestion.service",
                ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset),
                DataSource = DataFeedSourceEnum.TradingView,
                DataType = DataFeedTypeEnum.OHLC
            };
        }

        private DataIngestionMinDataEvent CreateMockDataForIngestion(MockOhlcDataEvent dataIngestionEvent)
        {
            var indianDateTimeOffset = DateTimeHelper.ConvertToIndianTime(DateTime.UtcNow);
            var windowsStartTimeIst = DateTimeHelper.ConvertToIndianTime(dataIngestionEvent.WindowsStartTime).DateTime;

            return new DataIngestionMinDataEvent()
            {
                SourceToken = dataIngestionEvent.ContractName,
                Ticker = dataIngestionEvent.ContractName,
                WindowsStartTime = windowsStartTimeIst,
                Timeframe = dataIngestionEvent.Timeframe,
                Open = dataIngestionEvent.Open,
                High = dataIngestionEvent.High,
                Low = dataIngestionEvent.Low,
                Close = dataIngestionEvent.Close,
                Volume = dataIngestionEvent.Volume,
                Producer = "mock.dataingestion.service",
                ProducedAt = DateTimeHelper.ToIsoStringWithTime(indianDateTimeOffset),
                DataSource = DataFeedSourceEnum.TradingView,
                DataType = DataFeedTypeEnum.OHLC
            };
        }

        private string GetTickerFromSourceToken(string sourceToken, DateTime date)
        {
            string tickerName = string.Empty;

            if (sourceToken.Contains("nifty") && sourceToken.Contains("bank"))
            {
                if (sourceToken.Contains("!"))
                {
                    // bank nifty futures data
                    tickerName = GetFutureFileName("BANKNIFTY", date);
                }
                else
                {
                    // bank nifty index data                    
                    tickerName = "BANKNIFTY";
                }
            }
            else if (sourceToken.Contains("nifty") && !(sourceToken.Contains("bank")))
            {
                if (sourceToken.Contains("!"))
                {
                    // nifty futures data                   
                    tickerName = GetFutureFileName("NIFTY", date);
                }
                else
                {
                    // nifty index data                    
                    tickerName = "NIFTY";
                }
            }

            return tickerName;
        }


        // private string GetFutureFileName1(string symbol, DateTime _date)
        // {
        //     // Find the last Thursday of the _date's month 
        //     DateTime lastThursday = new DateTime(_date.Year, _date.Month, 1);
        //     while (lastThursday.DayOfWeek != DayOfWeek.Thursday)
        //     {
        //         lastThursday = lastThursday.AddDays(1);
        //     }
        //     lastThursday = lastThursday.AddDays(7 * (Math.Floor((DateTime.DaysInMonth(_date.Year, _date.Month) - lastThursday.Day) / 7.0)));

        //     if (_date > lastThursday)
        //     {
        //         // Date is after the last Thursday of the month
        //         int nextMonth = _date.Month % 12 + 1;
        //         int nextYear = nextMonth == 1 ? _date.Year + 1 : _date.Year;
        //         return symbol + nextYear.ToString().Substring(2) + _date.ToString("MMM").ToUpper() + "FUT";
        //     }
        //     else
        //     {
        //         // Date is before or on the last Thursday of the month
        //         return symbol + _date.ToString("yy") + _date.ToString("MMM").ToUpper() + "FUT";
        //     }
        // }

        private string GetFutureFileName(string symbol, DateTime _date)
        {
            // Find the last Thursday of the _date's month 
            DateTime lastThursday = new DateTime(_date.Year, _date.Month, 1);
            
            while (lastThursday.DayOfWeek != DayOfWeek.Thursday)
            {
                lastThursday = lastThursday.AddDays(1);
            }
            lastThursday = lastThursday.AddDays(7 * (Math.Floor((DateTime.DaysInMonth(_date.Year, _date.Month) - lastThursday.Day) / 7.0)));

            if (_date > lastThursday)
            {
                // Date is after the last Thursday of the month
                int nextMonth = _date.Month % 12 + 1;
                int nextYear = nextMonth == 1 ? _date.Year + 1 : _date.Year;
                var newDate = new DateTime(nextYear, nextMonth, 1);
                return symbol + newDate.ToString("yy") + newDate.ToString("MMM").ToUpper() + "FUT";
            }
            else
            {
                // Date is before or on the last Thursday of the month
                return symbol + _date.ToString("yy") + _date.ToString("MMM").ToUpper() + "FUT";
            }
        }
        
    }


}
