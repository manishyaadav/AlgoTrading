using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Helpers
{
    public class DateTimeHelper
    {
        private static readonly TimeZoneInfo IndianTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        public static DateTimeOffset ConvertToIndianTime(DateTime utcDateTime)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, IndianTimeZone);
        }

        // Convert Indian Standard Time DateTime to UTC
        public static DateTime ConvertToUtc(DateTime indianDateTime)
        {
            return TimeZoneInfo.ConvertTimeToUtc(indianDateTime, IndianTimeZone);
        }

        public static string ToIsoStringWithTime(DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        public static string ToIsoStringWithoutTime(DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public static DateTimeOffset ParseIsoStringWithTime(string dateTimeString)
        {
            return DateTimeOffset.Parse(dateTimeString, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        }

        public static DateTime ParseIsoStringWithoutTime(string dateString)
        {
            return DateTime.ParseExact(dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        }

        // Get the offset for a given Indian Standard Time DateTime
        public static TimeSpan GetIndianTimeOffset(DateTime indianDateTime)
        {
            var dateTimeOffset = new DateTimeOffset(indianDateTime, IndianTimeZone.GetUtcOffset(indianDateTime));
            return dateTimeOffset.Offset;
        }

        // Convert Indian Standard Time DateTime to DateTimeOffset
        public static DateTimeOffset ConvertToDateTimeOffset(DateTime indianDateTime)
        {
            return new DateTimeOffset(indianDateTime, IndianTimeZone.GetUtcOffset(indianDateTime));
        }

        // Get the current date and time in Indian Standard Time
        public static DateTimeOffset GetCurrentIndianTime()
        {
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IndianTimeZone);
        }
    }
}
