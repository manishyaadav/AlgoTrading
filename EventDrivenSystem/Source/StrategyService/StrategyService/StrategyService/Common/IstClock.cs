namespace StrategyService.Common
{
    // StrategyService's first need for "what time is it right now, in IST" — the Alerts feature's
    // position lifecycle (first-bar-of-day detection, Time in Trade, Alert:Feed:{date} key) all need
    // it. Small and self-contained rather than pulling in the SharedLibrary.Helpers.DateTimeHelper
    // copy every other service duplicates, since this service only needs "now" and "today", not the
    // full ISO-parsing surface those carry.
    public static class IstClock
    {
        private static readonly TimeZoneInfo IndianTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        public static DateTime Now() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IndianTimeZone);

        public static string TodayIso() => Now().ToString("yyyy-MM-dd");
    }
}
