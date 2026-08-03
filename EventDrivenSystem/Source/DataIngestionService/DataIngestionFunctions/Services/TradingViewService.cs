using System.Text.Json;
using DataIngestionFunctions.SharedLibrary.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataIngestionFunctions.Services
{
    public class TradingViewService : ITradingViewService
    {
        private readonly ILogger<TradingViewService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _timeZone;

        public TradingViewService(
            ILogger<TradingViewService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _timeZone = _configuration["TimeZone"] ?? "India Standard Time";
        }

        public async Task<TradingViewDataEvent> ProcessDataFeed(string rawData)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                var dataFeed = JsonSerializer.Deserialize<TradingViewDataEvent>(rawData, options);
                
                if (dataFeed == null)
                {
                    throw new ArgumentException("Failed to deserialize trading view data");
                }

                if (!await ValidateDataFeed(dataFeed))
                {
                    throw new ArgumentException("Invalid trading view data");
                }

                return new TradingViewDataEvent
                {
                    SourceToken = dataFeed.SourceToken,
                    Timeframe = dataFeed.Timeframe,
                    Open = dataFeed.Open,
                    High = dataFeed.High,
                    Low = dataFeed.Low,
                    Close = dataFeed.Close,
                    Volume = dataFeed.Volume,
                    EventTime = ConvertToLocalTime(dataFeed.EventTime),
                    WindowsStartTime = ConvertToLocalTime(dataFeed.WindowsStartTime)
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize trading view data: {RawData}", rawData);
                throw;
            }
        }

        public async Task<bool> ValidateDataFeed(TradingViewDataEvent dataFeed)
        {
            if (string.IsNullOrEmpty(dataFeed.SourceToken))
            {
                _logger.LogWarning("Missing SourceToken in trading view data");
                return false;
            }

            if (string.IsNullOrEmpty(dataFeed.Timeframe))
            {
                _logger.LogWarning("Missing Timeframe in trading view data for {SourceToken}", dataFeed.SourceToken);
                return false;
            }

            if (dataFeed.EventTime == DateTime.MinValue)
            {
                _logger.LogWarning("Invalid EventTime in trading view data for {SourceToken}", dataFeed.SourceToken);
                return false;
            }

            if (dataFeed.WindowsStartTime == DateTime.MinValue)
            {
                _logger.LogWarning("Invalid WindowsStartTime in trading view data for {SourceToken}", dataFeed.SourceToken);
                return false;
            }

            return true;
        }

        public DateTime ConvertToLocalTime(DateTime utcTime)
        {
            try
            {
                TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_timeZone);
                return TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert time to timezone {TimeZone}. Falling back to UTC", _timeZone);
                return utcTime;
            }
        }
    }
}
