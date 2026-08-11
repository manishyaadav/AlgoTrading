using System.Text.RegularExpressions;

namespace WarmUpService.Common
{
    // Mirrors StrategyService's StrategyMaker.ParseTimeframeToMinutes exactly (same regex, same
    // "Day" == 375 trading minutes convention) — the two must agree, since a mismatch here would
    // mean WarmUpService seeds an indicator on a different bucket size than the days-needed
    // calculation that decided how much history to fetch in the first place.
    public static class TimeframeParser
    {
        private static readonly Regex Pattern = new(@"^(\d+)\s*(Minute|Day)s?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Returns -1 (deliberately, not a guess) when the string doesn't match the expected shape.
        public static int ParseMinutes(string? timeframe)
        {
            if (string.IsNullOrWhiteSpace(timeframe)) return -1;

            var match = Pattern.Match(timeframe.Trim());
            if (!match.Success) return -1;

            int value = int.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value;

            return unit.Equals("Day", StringComparison.OrdinalIgnoreCase) ? value * 375 : value;
        }
    }
}
