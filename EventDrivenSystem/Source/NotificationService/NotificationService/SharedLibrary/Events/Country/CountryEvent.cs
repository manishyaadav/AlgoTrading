using SharedLibrary.Enums;
using SharedLibrary.Enums.Country;
using SharedLibrary.Enums.Exchange;
using SharedLibrary.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SharedLibrary.Events.Country
{
    public class CountryEvent : CountryBase
    {
        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public CountryStateEnum State { get; set; }

        [JsonPropertyName("holiday")]
        public HolidayItem? Holiday { get; set; }

        [JsonPropertyName("nextHoliday")]
        public HolidayItem NextHoliday { get; set; } = new HolidayItem()
        {
            Date = DateTimeHelper.ToIsoStringWithoutTime(CalculateLastWorkingDayOfCurrentYear()),
            Reason = "Default"
        };

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

    public class HolidayItem
    {
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;
    }
}
