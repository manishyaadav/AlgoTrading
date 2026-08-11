using System.Text.Json;
using StrategyService.RedisConfig;

namespace StrategyService.Engine
{
    // Thin, request-scoped read layer over whatever WarmUpService/AggregationService/
    // NotificationService already maintain in Redis — one instance per rule-status request, so
    // the same indicator (e.g. Supertrend, referenced 3x across the Long branch alone) is only
    // fetched once per request rather than once per rule.
    public class LiveDataSnapshot
    {
        private readonly RedisHelper _redis;
        private readonly Dictionary<string, Dictionary<string, string>?> _hashCache = new();
        private string? _sessionState;
        private bool _sessionStateLoaded;

        public LiveDataSnapshot(RedisHelper redis)
        {
            _redis = redis;
        }

        // NotificationService's CountryNotificationFunctions writes this — bare key, no "Country:"
        // prefix (a known, documented inconsistency in that service, not something to fix here).
        public async Task<string?> GetSessionStateAsync()
        {
            if (_sessionStateLoaded) return _sessionState;
            _sessionStateLoaded = true;

            string? json = await _redis.GetStringAsync("India");
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                _sessionState = GetField(doc.RootElement, "State");
            }
            catch (JsonException) { /* leave null rather than fail the whole request */ }

            return _sessionState;
        }

        // Indicator:Running:{Instrument}:{Timeframe}:{Reference}:{Period}:{Multiplier} — the exact
        // key shape WarmUpService seeds and AggregationService live-updates. Null means either the
        // key doesn't exist yet (never seeded) or Redis is unreachable — callers treat both as
        // "unresolved", not as two different cases, since neither has a live value to compare.
        public async Task<Dictionary<string, string>?> GetIndicatorHashAsync(string instrument, string timeframe, string reference, int period, int multiplier)
        {
            string key = $"Indicator:Running:{instrument}:{timeframe}:{reference}:{period}:{multiplier}";
            if (_hashCache.TryGetValue(key, out var cached)) return cached;

            var hash = await _redis.GetHashAsync(key);
            var result = hash.Count > 0 ? hash : null;
            _hashCache[key] = result;
            return result;
        }

        // Pivot Central Range has no Period/Multiplier variation (always 0/0) but its Timeframe is
        // whatever the strategy's rule says (e.g. "15 Minutes") — the "Closing Price (Previous)"
        // Expression that needs PriorClose doesn't carry that same timeframe on its own operand (its
        // Properties.Timeframe says "1 Day"), so this scans for whichever PCR instance exists for
        // the instrument instead of requiring the caller to already know the exact timeframe key.
        // In practice there's only ever one PCR instance active per instrument at a time (WarmUpService
        // recomputes the same key fresh every Init) — see WARMUP_AND_INDICATOR_PLAN.md section 2e.
        public async Task<decimal?> GetPriorCloseAsync(string instrument)
        {
            foreach (var kv in _hashCache)
            {
                if (kv.Key.StartsWith($"Indicator:Running:{instrument}:", StringComparison.OrdinalIgnoreCase)
                    && kv.Key.Contains("Pivot Central Range", StringComparison.OrdinalIgnoreCase)
                    && kv.Value != null && kv.Value.TryGetValue("PriorClose", out var cachedClose)
                    && decimal.TryParse(cachedClose, out var cachedVal))
                    return cachedVal;
            }

            var hash = await _redis.ScanIndicatorHashAsync($"Indicator:Running:{instrument}:*:Pivot Central Range:0:0");
            if (hash == null || !hash.TryGetValue("PriorClose", out var raw) || !decimal.TryParse(raw, out var val))
                return null;
            return val;
        }

        // Mirrors DashboardService's GetField — ohlc-live/NotificationService's cached JSON blobs
        // aren't guaranteed to match this service's own casing expectations, so match
        // case-insensitively rather than assume PascalCase.
        private static string? GetField(JsonElement root, string propertyName)
        {
            if (root.ValueKind != JsonValueKind.Object) return null;
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
            }
            return null;
        }
    }
}
