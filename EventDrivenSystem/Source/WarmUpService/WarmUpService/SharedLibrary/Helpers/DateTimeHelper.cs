using System.Globalization;

namespace SharedLibrary.Helpers
{
    public class DateTimeHelper
    {
        private static readonly TimeZoneInfo IndianTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        public static DateTimeOffset ConvertToIndianTime(DateTime utcDateTime)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, IndianTimeZone);
        }

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
    }
}
