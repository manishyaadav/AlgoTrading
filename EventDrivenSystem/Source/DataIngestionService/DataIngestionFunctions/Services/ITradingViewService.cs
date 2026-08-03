using DataIngestionFunctions.SharedLibrary.Events;

namespace DataIngestionFunctions.Services
{
    public interface ITradingViewService
    {
        Task<TradingViewDataEvent> ProcessDataFeed(string rawData);
        Task<bool> ValidateDataFeed(TradingViewDataEvent dataFeed);
        DateTime ConvertToLocalTime(DateTime utcTime);
    }
}
