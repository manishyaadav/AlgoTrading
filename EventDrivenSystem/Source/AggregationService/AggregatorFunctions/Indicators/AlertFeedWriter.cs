using System.Text.Json;
using AggregatorFunctions.RedisConfig;
using SharedLibrary.Helpers;

namespace AggregatorFunctions.Indicators
{
    // Pushes IndicatorAlertRecords onto Alert:Feed:{yyyy-MM-dd} (IST) for the Alerts dashboard page —
    // reuses RedisHelper.PushToListAsync verbatim (already does RPUSH+LTRIM+expire, built for exactly
    // this "capped rolling feed" shape). StrategyService's PositionEntryFunction/PositionExitFunction
    // push their own PositionAlertRecord entries onto the same daily list — no shared project
    // reference between the two services (established convention), so the two record shapes are only
    // reconciled at read time in StrategyService's GET /api/alerts, which parses whatever's actually
    // in the list rather than assuming one fixed record type.
    public static class AlertFeedWriter
    {
        private const int MaxFeedLength = 5000;
        private static readonly TimeSpan FeedTtl = TimeSpan.FromDays(3);

        public static Task WriteEmaValueChangedAsync(RedisHelper redisHelper, ActiveIndicatorInstance instance, decimal newEma, decimal prevEma, decimal close, DateTime windowsStartTime)
        {
            var record = new IndicatorAlertRecord(
                Kind: "IndicatorSignal", AlertType: "EmaValueChanged",
                Instrument: instance.Instrument, Ticker: instance.Ticker, Timeframe: instance.Timeframe, TimeframeMinutes: instance.TimeframeMinutes,
                Reference: instance.Reference, Period: instance.Period, Multiplier: instance.Multiplier,
                Value: newEma, PreviousValue: prevEma,
                Direction: null, PreviousDirection: null,
                PenetratedPoints: null,
                Close: close, PreviousClose: null,
                WindowsStartTime: windowsStartTime, ProducedAt: NowIso());
            return PushAsync(redisHelper, record);
        }

        public static Task WriteEmaPriceCrossAsync(RedisHelper redisHelper, ActiveIndicatorInstance instance, decimal ema, decimal close, decimal prevClose, decimal prevEma, DateTime windowsStartTime)
        {
            var record = new IndicatorAlertRecord(
                Kind: "IndicatorSignal", AlertType: "PriceCrossedEma",
                Instrument: instance.Instrument, Ticker: instance.Ticker, Timeframe: instance.Timeframe, TimeframeMinutes: instance.TimeframeMinutes,
                Reference: instance.Reference, Period: instance.Period, Multiplier: instance.Multiplier,
                Value: ema, PreviousValue: prevEma,
                Direction: close >= ema ? "Above" : "Below", PreviousDirection: prevClose >= prevEma ? "Above" : "Below",
                PenetratedPoints: null,
                Close: close, PreviousClose: prevClose,
                WindowsStartTime: windowsStartTime, ProducedAt: NowIso());
            return PushAsync(redisHelper, record);
        }

        // May push up to 3 records for one bar: value-changed always (if the band actually moved),
        // color-changed only on a genuine flip, false-penetration only when the wick condition hits.
        public static async Task WriteSupertrendAlertsAsync(
            RedisHelper redisHelper, ActiveIndicatorInstance instance,
            string direction, decimal value, string prevDirection, decimal prevValue,
            decimal high, decimal low, decimal close, DateTime windowsStartTime)
        {
            string producedAt = NowIso();

            if (value != prevValue)
            {
                await PushAsync(redisHelper, new IndicatorAlertRecord(
                    Kind: "IndicatorSignal", AlertType: instance.Reference == "Adaptive Supertrend" ? "AdaptiveSupertrendValueChanged" : "SupertrendValueChanged",
                    Instrument: instance.Instrument, Ticker: instance.Ticker, Timeframe: instance.Timeframe, TimeframeMinutes: instance.TimeframeMinutes,
                    Reference: instance.Reference, Period: instance.Period, Multiplier: instance.Multiplier,
                    Value: value, PreviousValue: prevValue,
                    Direction: direction, PreviousDirection: prevDirection,
                    PenetratedPoints: null, Close: null, PreviousClose: null,
                    WindowsStartTime: windowsStartTime, ProducedAt: producedAt));
            }

            if (direction != prevDirection)
            {
                await PushAsync(redisHelper, new IndicatorAlertRecord(
                    Kind: "IndicatorSignal", AlertType: instance.Reference == "Adaptive Supertrend" ? "AdaptiveSupertrendColorChanged" : "SupertrendColorChanged",
                    Instrument: instance.Instrument, Ticker: instance.Ticker, Timeframe: instance.Timeframe, TimeframeMinutes: instance.TimeframeMinutes,
                    Reference: instance.Reference, Period: instance.Period, Multiplier: instance.Multiplier,
                    Value: value, PreviousValue: prevValue,
                    Direction: direction, PreviousDirection: prevDirection,
                    PenetratedPoints: null, Close: null, PreviousClose: null,
                    WindowsStartTime: windowsStartTime, ProducedAt: producedAt));
            }

            // Ported verbatim from StrategyService/Backtest/BacktestEngine.cs's false-penetration
            // formula (RED/GREEN there == Down/Up here) — a wick crossed the line against the trend
            // but the candle's own Close pulled back onto the trend's side, so the trend itself didn't
            // flip this bar (a real flip already shows up as the ColorChanged alert above).
            bool penetrated = direction == "Down"
                ? high > value && close <= value
                : low < value && close >= value;
            if (penetrated)
            {
                decimal points = direction == "Down" ? high - value : value - low;
                await PushAsync(redisHelper, new IndicatorAlertRecord(
                    Kind: "IndicatorSignal", AlertType: instance.Reference == "Adaptive Supertrend" ? "AdaptiveSupertrendFalsePenetration" : "SupertrendFalsePenetration",
                    Instrument: instance.Instrument, Ticker: instance.Ticker, Timeframe: instance.Timeframe, TimeframeMinutes: instance.TimeframeMinutes,
                    Reference: instance.Reference, Period: instance.Period, Multiplier: instance.Multiplier,
                    Value: value, PreviousValue: prevValue,
                    Direction: direction, PreviousDirection: prevDirection,
                    PenetratedPoints: points, Close: close, PreviousClose: null,
                    WindowsStartTime: windowsStartTime, ProducedAt: producedAt));
            }
        }

        private static Task PushAsync(RedisHelper redisHelper, IndicatorAlertRecord record)
        {
            string key = $"Alert:Feed:{DateTimeHelper.ToIsoStringWithoutTime(DateTimeHelper.GetCurrentIndianTime())}";
            string json = JsonSerializer.Serialize(record);
            return redisHelper.PushToListAsync(key, json, MaxFeedLength, FeedTtl);
        }

        private static string NowIso() => DateTimeHelper.ToIsoStringWithTime(DateTimeHelper.GetCurrentIndianTime());
    }
}
