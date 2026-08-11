using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StrategyService.Strategy
{
    public static class StrategyMaker
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // On-disk filename is "{slug}-{version}.json" (e.g. second-income-1.0.2.json) — the version
        // is embedded so the folder is browsable/traceable by eye. The *slug* (no version) is the
        // stable id used everywhere in the API/UI; the versioned filename is an internal storage
        // detail that changes on every save/deploy (old-version file for that slug is removed, new
        // one written) — this is NOT a version history, just one current file per strategy per folder.
        private static readonly Regex VersionedFileNamePattern = new(@"^(?<slug>.+)-(?<version>\d+\.\d+\.\d+)$", RegexOptions.Compiled);

        // Two separate folders, two separate lifecycles: `saved/` is the working draft — every Save
        // overwrites it, whether or not that draft has ever been deployed. `deployed/` is a snapshot
        // taken *at deploy time* and only ever changes when Deploy is called again — so a draft saved
        // on top of a deployed version no longer clobbers the deployed version's actual rule content,
        // which is exactly the gap that made the data-requirements manifest (below) inaccurate before
        // this split existed. DeployedVersion is no longer a field persisted on the saved file — it's
        // computed at read time from whatever's actually sitting in deployed/, so there's only one
        // source of truth instead of two that can drift apart.
        private static string SavedConfigFolder =>
            Path.Combine(AppContext.BaseDirectory, "config", "strategies", "saved");

        private static string DeployedConfigFolder =>
            Path.Combine(AppContext.BaseDirectory, "config", "strategies", "deployed");

        private static string SlugFromFileName(string fileNameWithoutExtension)
        {
            var match = VersionedFileNamePattern.Match(fileNameWithoutExtension);
            // tolerates a legacy/hand-dropped file with no version suffix — falls back to the whole name
            return match.Success ? match.Groups["slug"].Value : fileNameWithoutExtension;
        }

        private static string? FindFileForId(string id, string folder)
        {
            if (!Directory.Exists(folder)) return null;
            var sanitized = SanitizeId(id);
            return Directory.EnumerateFiles(folder, "*.json")
                .FirstOrDefault(f => string.Equals(SlugFromFileName(Path.GetFileNameWithoutExtension(f)), sanitized, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<(string Id, Strategy Strategy)> LoadAllFrom(string folder)
        {
            if (!Directory.Exists(folder))
                yield break;

            foreach (var file in Directory.EnumerateFiles(folder, "*.json").OrderBy(f => f))
            {
                Strategy? strategy;
                try
                {
                    strategy = JsonSerializer.Deserialize<Strategy>(File.ReadAllText(file), JsonOptions);
                }
                catch (JsonException)
                {
                    continue; // skip a malformed file rather than failing the whole list
                }

                if (strategy != null)
                    yield return (SlugFromFileName(Path.GetFileNameWithoutExtension(file)), strategy);
            }
        }

        private static Strategy? GetSavedById(string id)
            => LoadAllFrom(SavedConfigFolder).FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)).Strategy;

        /// <summary>
        /// The actual deployed rule content for `id`, not the saved draft — used anywhere that needs
        /// to evaluate/describe what's really live right now (the Rule Engine page), as opposed to
        /// GetById's "current draft, deployed version number overlaid for display" shape. Null if
        /// nothing has ever been deployed for this id.
        /// </summary>
        public static Strategy? GetDeployedById(string id)
            => LoadAllFrom(DeployedConfigFolder).FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)).Strategy;

        private static string? GetDeployedVersionForId(string id)
        {
            var file = FindFileForId(id, DeployedConfigFolder);
            if (file == null) return null;
            try
            {
                return JsonSerializer.Deserialize<Strategy>(File.ReadAllText(file), JsonOptions)?.Version;
            }
            catch (JsonException)
            {
                return null; // malformed deployed snapshot — treat as "not deployed" rather than throw
            }
        }

        /// <summary>Every saved (draft) strategy, with DeployedVersion overlaid from the deployed/ folder — not read off the saved file itself.</summary>
        public static IEnumerable<(string Id, Strategy Strategy)> LoadAll()
        {
            foreach (var (id, strategy) in LoadAllFrom(SavedConfigFolder))
            {
                strategy.DeployedVersion = GetDeployedVersionForId(id);
                yield return (id, strategy);
            }
        }

        public static Strategy? GetById(string id)
        {
            var strategy = GetSavedById(id);
            if (strategy != null)
                strategy.DeployedVersion = GetDeployedVersionForId(id);
            return strategy;
        }

        /// <summary>
        /// Validates the JSON deserializes to a Strategy, then writes it to
        /// config/strategies/saved/{id}-{version}.json, removing whatever saved file previously held
        /// this id (if any) so saves don't accumulate one file per version. The Version field in the
        /// incoming JSON is ignored and replaced with a server-computed auto-increment (1.0.0 for a
        /// brand new id, otherwise the existing saved file's patch version + 1) — the client can't set
        /// an arbitrary or backwards version number. Never touches config/strategies/deployed/ — only
        /// DeployById does.
        /// </summary>
        public static Strategy SaveById(string id, string json)
        {
            var strategy = JsonSerializer.Deserialize<Strategy>(json, JsonOptions)
                ?? throw new JsonException("Strategy JSON deserialized to null.");

            var existing = GetSavedById(id);
            strategy.Version = NextVersion(existing?.Version);
            strategy.DeployedVersion = null; // not persisted here — GetById/LoadAll overlay it from deployed/ on read

            Directory.CreateDirectory(SavedConfigFolder);

            var oldFile = FindFileForId(id, SavedConfigFolder);
            if (oldFile != null) File.Delete(oldFile);

            var path = Path.Combine(SavedConfigFolder, $"{SanitizeId(id)}-{strategy.Version}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(strategy, JsonOptions));

            strategy.DeployedVersion = GetDeployedVersionForId(id); // for the response — unaffected by this save
            return strategy;
        }

        /// <summary>
        /// Snapshots the strategy's current saved content into config/strategies/deployed/{id}-{version}.json
        /// with DeployedVersion set to match — a real copy, not just a flag flipped on the saved file, so a
        /// later draft save can never silently overwrite what's actually deployed. Replaces whatever was
        /// previously deployed for this id (single current deployed snapshot, not a history). No cleanup of
        /// a previously-deployed version's *effects* happens here (yet) — that's a separate, still-undefined
        /// concern from just preserving its rule content.
        /// </summary>
        public static Strategy? DeployById(string id)
        {
            var strategy = GetSavedById(id);
            if (strategy == null) return null;

            strategy.DeployedVersion = strategy.Version;

            Directory.CreateDirectory(DeployedConfigFolder);

            var oldDeployedFile = FindFileForId(id, DeployedConfigFolder);
            if (oldDeployedFile != null) File.Delete(oldDeployedFile);

            var path = Path.Combine(DeployedConfigFolder, $"{SanitizeId(id)}-{strategy.Version}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(strategy, JsonOptions));
            return strategy;
        }

        /// <summary>Deletes the strategy from both saved/ and deployed/ (whichever it's found in) — a full removal, not just the draft.</summary>
        public static bool DeleteById(string id)
        {
            var savedFile = FindFileForId(id, SavedConfigFolder);
            var deployedFile = FindFileForId(id, DeployedConfigFolder);

            if (savedFile == null && deployedFile == null) return false;

            if (savedFile != null) File.Delete(savedFile);
            if (deployedFile != null) File.Delete(deployedFile);
            return true;
        }

        private static string NextVersion(string? currentVersion)
        {
            if (string.IsNullOrWhiteSpace(currentVersion))
                return "1.0.0";

            var parts = currentVersion.Split('.');
            if (parts.Length == 3 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor) && int.TryParse(parts[2], out var patch))
                return $"{major}.{minor}.{patch + 1}";

            // unparseable existing version (e.g. hand-edited) — don't guess, just start a fresh patch lineage
            return "1.0.0";
        }

        public static string SanitizeId(string id)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(id.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "strategy" : cleaned.Replace(' ', '-').ToLowerInvariant();
        }

        /// <summary>
        /// The data-requirements manifest: every (Instrument, Timeframe) pair referenced by any
        /// operand (Indicator or Expression — see CollectDataRequirement) anywhere in a
        /// currently-deployed strategy's rules, unioned across all deployed strategies — broken down
        /// by which specific reference(s) need it and their Period/Multiplier/RelativePosition, since
        /// that's what actually determines how much historical data a warm-up job needs to fetch (an
        /// EMA(550) and a Supertrend(20,4) on the same instrument/timeframe need very different
        /// amounts of backfill; a "Closing Price" Expression with RelativePosition "Previous" needs a
        /// prior day's close, not a live one; the pair alone doesn't say any of this). This
        /// intentionally stops at surfacing the raw reference inputs — it does not compute a
        /// "bars/days needed" number, since that's an indicator-specific lookback formula (EMA vs
        /// Supertrend vs Pivot Central Range all differ) that belongs with whatever eventually
        /// computes those indicators, not here.
        ///
        /// Reads directly from config/strategies/deployed/ — the actual deployed snapshot, not the
        /// saved draft — so this is accurate even when a newer, undeployed draft exists with different
        /// requirements. (Before the saved/deployed folder split, this read the saved file and could
        /// report a draft's requirements instead of the actually-deployed version's.)
        /// </summary>
        public static List<DataRequirement> GetDeployedDataRequirements()
        {
            var byPair = new Dictionary<(string Instrument, string Timeframe), HashSet<string>>();
            // nested by pair, then by the specific (Value, Type, Period, Multiplier, RelativePosition) combination
            var referencesByPair = new Dictionary<(string Instrument, string Timeframe), Dictionary<(string Value, string Type, int Period, int Multiplier, string? RelativePosition), HashSet<string>>>();

            foreach (var (id, strategy) in LoadAllFrom(DeployedConfigFolder))
            {
                if (strategy.Strategies == null) continue;

                foreach (var tradingStrategy in strategy.Strategies)
                {
                    foreach (var rule in AllRulesOf(tradingStrategy))
                    {
                        CollectDataRequirement(rule.LeftOperand, id, byPair, referencesByPair);
                        CollectDataRequirement(rule.RightOperand, id, byPair, referencesByPair);
                    }
                }
            }

            return byPair
                .Select(kv => new DataRequirement(
                    kv.Key.Instrument,
                    kv.Key.Timeframe,
                    kv.Value.OrderBy(x => x).ToList(),
                    referencesByPair[kv.Key]
                        .Select(rkv => new IndicatorUsage(rkv.Key.Value, rkv.Key.Type, rkv.Key.Period, rkv.Key.Multiplier, rkv.Key.RelativePosition, rkv.Value.OrderBy(x => x).ToList()))
                        .OrderBy(u => u.Indicator).ThenBy(u => u.Period).ThenBy(u => u.Multiplier)
                        .ToList()))
                .OrderBy(r => r.Instrument).ThenBy(r => r.Timeframe)
                .ToList();
        }

        // Every TradingRule across all 9 rule arrays a TradingStrategy can carry — the same set
        // ProcessTradingRules/ProcessEntryExitRules walk separately below, unified here so
        // GetDeployedDataRequirements only has to walk the tree once.
        private static IEnumerable<TradingRule> AllRulesOf(TradingStrategy tradingStrategy)
        {
            foreach (var rule in tradingStrategy.TradingSessionRules ?? Enumerable.Empty<TradingRule>())
                yield return rule;

            foreach (var entryExit in new[] { tradingStrategy.LongEntry, tradingStrategy.ShortEntry })
            {
                if (entryExit == null) continue;
                foreach (var rule in (entryExit.EntryRules ?? new())
                    .Concat(entryExit.RiskManagementRules ?? new())
                    .Concat(entryExit.UpdateStopLossRules ?? new())
                    .Concat(entryExit.ExitRules ?? new()))
                    yield return rule;
            }
        }

        private static void CollectDataRequirement(
            Operand? operand,
            string strategyId,
            Dictionary<(string Instrument, string Timeframe), HashSet<string>> byPair,
            Dictionary<(string Instrument, string Timeframe), Dictionary<(string Value, string Type, int Period, int Multiplier, string? RelativePosition), HashSet<string>>> referencesByPair)
        {
            // Any operand carrying Instrument/Timeframe is a real data need — not just Indicator
            // operands (EMA, Supertrend, Pivot Central Range, where Period/Multiplier are the lookback
            // inputs) but also Expression operands referencing raw price data (e.g. "Closing Price" at
            // Timeframe "1 Day" with RelativePosition "Previous" — that's a genuine historical need,
            // just expressed as a price reference instead of an indicator computation). Literal
            // operands are constants and never carry an Instrument, so they're naturally excluded.
            if (operand?.Properties?.Instrument == null) return;

            var instrument = operand.Properties.Instrument;
            var timeframe = operand.Properties.Timeframe ?? "unspecified";
            var pairKey = (instrument, timeframe);

            if (!byPair.TryGetValue(pairKey, out var strategyIds))
                byPair[pairKey] = strategyIds = new HashSet<string>();
            strategyIds.Add(strategyId);

            if (!referencesByPair.TryGetValue(pairKey, out var references))
                referencesByPair[pairKey] = references = new Dictionary<(string, string, int, int, string?), HashSet<string>>();

            var referenceKey = (operand.Value ?? "Unknown", operand.Type ?? "Unknown", operand.Properties.Period, operand.Properties.Multiplier, operand.Properties.RelativePosition);
            if (!references.TryGetValue(referenceKey, out var referenceStrategyIds))
                references[referenceKey] = referenceStrategyIds = new HashSet<string>();
            referenceStrategyIds.Add(strategyId);
        }

        // Trading-minutes in one session (9:15-15:30 IST = 375 one-minute bars) — same convention
        // DashboardService's Data page uses for "expected candles today." Used here to convert
        // "N bars at timeframe T" into "N*T minutes of trading" into "how many trading days that is."
        private const int TradingMinutesPerDay = 375;

        /// <summary>
        /// "Fetch the last N trading days of {Instrument} data" — turns GetDeployedDataRequirements()'s
        /// (Instrument, Timeframe, References) manifest into an actual day count per instrument, which
        /// is what a warm-up job needs to act on (the manifest alone says *what* is needed, not *how
        /// much*). One instrument's DaysToFetch is the max across every reference that needs it, since
        /// a single historical pull covering the most demanding requirement covers every lesser one too.
        ///
        /// The day-count formulas are documented assumptions, not verified against a real indicator
        /// engine (none exists yet) — see ComputeDaysNeeded for exactly what's assumed and why.
        /// </summary>
        public static List<InstrumentWarmUpPlan> GetWarmUpPlan()
        {
            var manifest = GetDeployedDataRequirements();
            var reasonsByInstrument = new Dictionary<string, List<WarmUpReason>>(StringComparer.OrdinalIgnoreCase);
            var strategyIdsByInstrument = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in manifest)
            {
                if (!reasonsByInstrument.TryGetValue(pair.Instrument, out var reasons))
                    reasonsByInstrument[pair.Instrument] = reasons = new List<WarmUpReason>();
                if (!strategyIdsByInstrument.TryGetValue(pair.Instrument, out var ids))
                    strategyIdsByInstrument[pair.Instrument] = ids = new HashSet<string>();

                foreach (var id in pair.StrategyIds) ids.Add(id);

                foreach (var reference in pair.References)
                {
                    var daysNeeded = ComputeDaysNeeded(reference, pair.Timeframe);
                    reasons.Add(new WarmUpReason(pair.Timeframe, reference.Indicator, reference.Type, reference.Period, reference.Multiplier, reference.RelativePosition, daysNeeded));
                    foreach (var id in reference.StrategyIds) ids.Add(id);
                }
            }

            return reasonsByInstrument
                .Select(kv => new InstrumentWarmUpPlan(
                    kv.Key,
                    kv.Value.Count > 0 ? kv.Value.Max(r => r.DaysNeeded) : 0,
                    kv.Value.OrderByDescending(r => r.DaysNeeded).ThenBy(r => r.Timeframe).ToList(),
                    strategyIdsByInstrument[kv.Key].OrderBy(x => x).ToList()))
                .OrderBy(p => p.Instrument)
                .ToList();
        }

        /// <summary>
        /// Documented assumptions, in order of precedence — none of these are verified against a real
        /// indicator computation, since none exists yet:
        ///
        /// 1. Period &gt; 0 (EMA, Supertrend, any period-based indicator): needs at least Period bars
        ///    to produce one value — the mathematical minimum, not an extra-converged buffer. This
        ///    matches the earlier "pull last 7-8 days" estimate for EMA(550) on 5-min almost exactly
        ///    (550 bars * 5 min / 375 trading-min/day ≈ 7.3 → 8 days), which is why the minimum was
        ///    chosen over a larger multiple.
        /// 2. Period == 0 and Type == "Indicator" (e.g. Pivot Central Range, which has no Period/
        ///    Multiplier of its own): assumed to need exactly 1 prior trading day, independent of
        ///    whatever Timeframe it's being *compared* against in the rule (Pivot Central Range is
        ///    conventionally computed from the prior day's H/L/C, not from intraday bars at the
        ///    comparison timeframe) — a generic assumption for this shape of indicator, worth
        ///    revisiting per-indicator once a real computation exists.
        /// 3. Type == "Expression" with a RelativePosition other than null/"Current" (e.g. "Closing
        ///    Price" with RelativePosition "Previous"): needs exactly 1 prior bar at the recorded
        ///    Timeframe.
        /// 4. Type == "Expression" with no RelativePosition (or "Current"): asking for the live/current
        ///    value only — 0 days of historical backfill needed.
        ///
        /// Returns -1 if the Timeframe string doesn't match ParseTimeframeToMinutes's expected shape —
        /// deliberately not silently guessed at, since an under-estimate here means a strategy runs on
        /// an indicator that hasn't actually converged.
        /// </summary>
        private static int ComputeDaysNeeded(IndicatorUsage reference, string timeframe)
        {
            if (reference.Period > 0)
            {
                var timeframeMinutes = ParseTimeframeToMinutes(timeframe);
                if (timeframeMinutes == null) return -1;
                return (int)Math.Ceiling((double)reference.Period * timeframeMinutes.Value / TradingMinutesPerDay);
            }

            if (reference.Type == "Indicator")
                return 1;

            if (!string.IsNullOrEmpty(reference.RelativePosition) && !string.Equals(reference.RelativePosition, "Current", StringComparison.OrdinalIgnoreCase))
            {
                var timeframeMinutes = ParseTimeframeToMinutes(timeframe);
                if (timeframeMinutes == null) return -1;
                return (int)Math.Ceiling((double)timeframeMinutes.Value / TradingMinutesPerDay);
            }

            return 0;
        }

        // "5 Minutes" -> 5, "1 Day" -> 375 (one trading session's worth of minutes), "15 Minutes" -> 15, etc.
        // Returns null rather than guessing if the string doesn't match this shape.
        private static int? ParseTimeframeToMinutes(string timeframe)
        {
            var match = Regex.Match(timeframe.Trim(), @"^(\d+)\s*(Minute|Day)s?$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            var count = int.Parse(match.Groups[1].Value);
            return string.Equals(match.Groups[2].Value, "Day", StringComparison.OrdinalIgnoreCase)
                ? count * TradingMinutesPerDay
                : count;
        }

        public static Dictionary<string, HashSet<string>> ExtractInstrumentTimeframeDictionary(Strategy strategy)
        {
            var instrumentTimeframes = new Dictionary<string, HashSet<string>>();

            if (strategy.Strategies != null)
            {
                foreach (var tradingStrategy in strategy.Strategies)
                {
                    ProcessTradingRules(tradingStrategy.TradingSessionRules, instrumentTimeframes);
                    ProcessEntryExitRules(tradingStrategy.LongEntry, instrumentTimeframes);
                    ProcessEntryExitRules(tradingStrategy.ShortEntry, instrumentTimeframes);
                }
            }

            return instrumentTimeframes;
        }

        public static Dictionary<string, HashSet<string>> ExtractUniqueIndicatorsAndValues(Strategy strategy)
        {
            var indicatorsValues = new Dictionary<string, HashSet<string>>();

            if (strategy.Strategies != null)
            {
                foreach (var tradingStrategy in strategy.Strategies)
                {
                    ProcessIndicatorRules(tradingStrategy.TradingSessionRules, indicatorsValues);
                    if (tradingStrategy.LongEntry != null)
                    {
                        ProcessIndicatorRules(tradingStrategy.LongEntry.EntryRules, indicatorsValues);
                        ProcessIndicatorRules(tradingStrategy.LongEntry.RiskManagementRules, indicatorsValues);
                        ProcessIndicatorRules(tradingStrategy.LongEntry.UpdateStopLossRules, indicatorsValues);
                        ProcessIndicatorRules(tradingStrategy.LongEntry.ExitRules, indicatorsValues);
                    }
                    if (tradingStrategy.ShortEntry != null)
                    {
                        ProcessIndicatorRules(tradingStrategy.ShortEntry.EntryRules, indicatorsValues);
                        ProcessIndicatorRules(tradingStrategy.ShortEntry.RiskManagementRules, indicatorsValues);
                        ProcessIndicatorRules(tradingStrategy.ShortEntry.UpdateStopLossRules, indicatorsValues);
                        ProcessIndicatorRules(tradingStrategy.ShortEntry.ExitRules, indicatorsValues);
                    }
                }
            }

            return indicatorsValues;
        }

        private static void ProcessTradingRules(List<TradingRule>? rules, Dictionary<string, HashSet<string>> dict)
        {
            if (rules == null) return;
            foreach (var rule in rules)
            {
                AddToDictionary(rule.LeftOperand, dict);
                AddToDictionary(rule.RightOperand, dict);
            }
        }

        private static void ProcessEntryExitRules(EntryExitRule? entryExitRule, Dictionary<string, HashSet<string>> dict)
        {
            if (entryExitRule == null) return;
            var all = (entryExitRule.EntryRules ?? new())
                .Concat(entryExitRule.RiskManagementRules ?? new())
                .Concat(entryExitRule.UpdateStopLossRules ?? new())
                .Concat(entryExitRule.ExitRules ?? new());
            foreach (var rule in all)
            {
                AddToDictionary(rule.LeftOperand, dict);
                AddToDictionary(rule.RightOperand, dict);
            }
        }

        private static void AddToDictionary(Operand? operand, Dictionary<string, HashSet<string>> dict)
        {
            if (operand?.Properties?.Instrument == null) return;

            var instrument = operand.Properties.Instrument;
            var timeframe = operand.Properties.Timeframe ?? "unspecified";

            if (!dict.ContainsKey(instrument))
                dict[instrument] = new HashSet<string>();
            dict[instrument].Add(timeframe);
        }

        private static void ProcessIndicatorRules(List<TradingRule>? rules, Dictionary<string, HashSet<string>> dict)
        {
            if (rules == null) return;
            foreach (var rule in rules)
            {
                AddIndicatorToDictionary(rule.LeftOperand, dict);
                AddIndicatorToDictionary(rule.RightOperand, dict);
            }
        }

        private static void AddIndicatorToDictionary(Operand? operand, Dictionary<string, HashSet<string>> dict)
        {
            if (operand == null || operand.Type != "Indicator") return;

            var indicator = operand.Value ?? "Unknown";
            var value = operand.Properties != null
                ? $"Period={operand.Properties.Period}, Timeframe={operand.Properties.Timeframe}"
                : "No Properties";

            if (!dict.ContainsKey(indicator))
                dict[indicator] = new HashSet<string>();
            dict[indicator].Add(value);
        }
    }
}
