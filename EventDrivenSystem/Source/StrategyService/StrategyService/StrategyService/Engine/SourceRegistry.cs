namespace StrategyService.Engine
{
    // Collects every live input the rule tree depends on, as the tree is walked, so the dashboard
    // can show the data flow alongside the rules instead of only the verdicts.
    //
    // Two-step by design: Touch() names a dependency (derived purely from the rule definition, no
    // Redis involved), Fill() attaches a value that was actually read. A source that is only ever
    // Touched and never Filled is exactly the thing the Rule Engine page draws as unbacked — the
    // separation is what keeps "this rule reads Supertrend" from being confused with "Supertrend
    // has a value right now".
    internal class SourceRegistry
    {
        private class Entry
        {
            public string Id = "";
            public string Label = "";
            public string Scope = "";
            public string Kind = "";
            public string? Value;
            public string? Detail;
            public string? Key;
            public string? AsOf;
            public bool Backed;
            public int Feeds;
            public int Order;
            public List<EvidenceField> Fields = new();
        }

        // Natural ids are readable and derived from the operand ("supertrend:Indicator:Running:…"),
        // but what reaches the browser is the short opaque Id — the DOM matches linked rules with a
        // space-separated attribute selector, and Redis keys contain spaces ("5 Minutes").
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private int _next;

        // Records that one rule or gate depends on this input, and returns the id to link it by.
        // Callers dedupe within a single rule first, so a rule comparing an input against itself
        // counts as one dependent, not two.
        public string Touch(string naturalId, string label, string scope, string kind)
        {
            if (!_entries.TryGetValue(naturalId, out var entry))
            {
                entry = new Entry { Id = $"s{++_next}", Label = label, Scope = scope, Kind = kind, Order = _next };
                _entries[naturalId] = entry;
            }

            entry.Feeds++;
            return entry.Id;
        }

        // Attaches a real reading. Called only from the resolvers, so an input can only ever become
        // "backed" by actually having been read — never by being referenced.
        public void Fill(string naturalId, string value, string? detail, string? key, string? asOf, List<EvidenceField> fields)
        {
            if (!_entries.TryGetValue(naturalId, out var entry)) return;

            entry.Value = value;
            entry.Detail = detail;
            entry.Key = key;
            entry.AsOf = asOf;
            entry.Fields = fields;
            entry.Backed = true;
        }

        // Records why an input that has a source produced nothing ("not seeded yet"), without
        // marking it backed. Keeps "there's nowhere to look" and "we looked and it wasn't ready"
        // distinguishable on the page — they're different problems with different fixes.
        public void FillUnresolved(string naturalId, string? reason, string? key)
        {
            if (!_entries.TryGetValue(naturalId, out var entry) || entry.Backed) return;

            entry.Detail ??= reason;
            entry.Key ??= key;
        }

        // Backed inputs first (what's actually live is the reason to look at this list at all),
        // then unbacked, each in the order the rule tree first reached them — which follows the
        // tree top to bottom, so the rail reads in roughly the same order as the rules beside it.
        public List<DataSource> Build() =>
            _entries.Values
                .OrderByDescending(e => e.Backed)
                .ThenBy(e => e.Order)
                .Select(e => new DataSource(e.Id, e.Label, e.Scope, e.Kind, e.Value, e.Detail, e.Key, e.AsOf, e.Backed, e.Feeds, e.Fields))
                .ToList();
    }
}
