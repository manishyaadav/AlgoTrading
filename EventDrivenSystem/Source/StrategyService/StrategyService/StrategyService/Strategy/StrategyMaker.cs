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
        // stable id used everywhere in the API/UI.
        //
        // The two folders now have DIFFERENT retention behavior, deliberately:
        //  - deployed/ still holds exactly one current file per strategy — Deploy replaces whatever
        //    was previously deployed, no history.
        //  - saved/ now KEEPS every version a Save ever wrote, on purpose — earlier drafts are left
        //    on disk for reference/rollback by hand, not auto-deleted the moment a newer one exists.
        //    "Current" for API purposes is always the highest parsed version among however many
        //    files exist for that slug (LatestOf/FindLatestFileForId below) — never just whichever
        //    file a plain alphabetical sort happens to return first, which silently picks the wrong
        //    one once a patch number reaches double digits ("1.0.10" sorts before "1.0.9" as a
        //    string). Cleaning up superseded saved versions is a manual, explicit action (delete the
        //    file, or DeleteById removes all of them at once) — not something a later Save does.
        private static readonly Regex VersionedFileNamePattern = new(@"^(?<slug>.+)-(?<version>\d+\.\d+\.\d+)$", RegexOptions.Compiled);

        // Two separate folders, two separate lifecycles: `saved/` is the working draft history —
        // every Save adds a new version, whether or not any draft has ever been deployed. `deployed/`
        // is a snapshot taken *at deploy time* and only ever changes when Deploy is called again — so
        // a draft saved on top of a deployed version no longer clobbers the deployed version's actual
        // rule content, which is exactly the gap that made the data-requirements manifest (below)
        // inaccurate before this split existed. DeployedVersion is no longer a field persisted on the
        // saved file — it's computed at read time from whatever's actually sitting in deployed/, so
        // there's only one source of truth instead of two that can drift apart.
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

        private static string? VersionFromFileName(string fileNameWithoutExtension)
        {
            var match = VersionedFileNamePattern.Match(fileNameWithoutExtension);
            return match.Success ? match.Groups["version"].Value : null;
        }

        // Parses "major.minor.patch" into a comparable tuple; null for anything that doesn't match
        // that shape (e.g. a hand-dropped file with no version suffix). Such files are still found by
        // FindAllFilesForId, just never chosen as "the latest" by LatestOf since there's nothing
        // reliable to compare them by.
        private static (int Major, int Minor, int Patch)? ParseVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;
            var parts = version.Split('.');
            return parts.Length == 3
                && int.TryParse(parts[0], out var major)
                && int.TryParse(parts[1], out var minor)
                && int.TryParse(parts[2], out var patch)
                ? (major, minor, patch)
                : null;
        }

        // Still exactly right for deployed/, which only ever holds one file per id — kept as the
        // simple "find the one file" lookup for that folder. Do not use this against saved/ now that
        // it can hold multiple version files per id (use FindLatestFileForId/FindAllFilesForId there).
        private static string? FindFileForId(string id, string folder)
        {
            if (!Directory.Exists(folder)) return null;
            var sanitized = SanitizeId(id);
            return Directory.EnumerateFiles(folder, "*.json")
                .FirstOrDefault(f => string.Equals(SlugFromFileName(Path.GetFileNameWithoutExtension(f)), sanitized, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> FindAllFilesForId(string id, string folder)
        {
            if (!Directory.Exists(folder)) return Enumerable.Empty<string>();
            var sanitized = SanitizeId(id);
            return Directory.EnumerateFiles(folder, "*.json")
                .Where(f => string.Equals(SlugFromFileName(Path.GetFileNameWithoutExtension(f)), sanitized, StringComparison.OrdinalIgnoreCase));
        }

        // Picks the file with the highest parsed {major.minor.patch} among candidates — a plain
        // alphabetical sort gets this wrong once a patch number reaches double digits ("1.0.10" sorts
        // before "1.0.9" as a string). A file with no parseable version suffix sorts last rather than
        // winning by accident.
        private static string? LatestOf(IEnumerable<string> files)
        {
            return files
                .Select(f => (File: f, Version: ParseVersion(VersionFromFileName(Path.GetFileNameWithoutExtension(f)))))
                .OrderByDescending(c => c.Version?.Major ?? -1)
                .ThenByDescending(c => c.Version?.Minor ?? -1)
                .ThenByDescending(c => c.Version?.Patch ?? -1)
                .Select(c => c.File)
                .FirstOrDefault();
        }

        private static string? FindLatestFileForId(string id, string folder) => LatestOf(FindAllFilesForId(id, folder));

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

        // The current saved draft for `id` is the highest-versioned file for that slug — saved/ can
        // now hold several, and this is the one thing that decides which one counts as "the" draft
        // (for Save's next-version computation, GetById, and DeployById).
        private static Strategy? GetSavedById(string id)
        {
            var file = FindLatestFileForId(id, SavedConfigFolder);
            if (file == null) return null;
            try
            {
                return JsonSerializer.Deserialize<Strategy>(File.ReadAllText(file), JsonOptions);
            }
            catch (JsonException)
            {
                return null; // malformed saved file — treat as "no draft" rather than throw
            }
        }

        /// <summary>
        /// The actual deployed rule content for `id`, not the saved draft — used anywhere that needs
        /// to evaluate/describe what's really live right now (the Rule Engine page), as opposed to
        /// GetById's "current draft, deployed version number overlaid for display" shape. Null if
        /// nothing has ever been deployed for this id.
        /// </summary>
        public static Strategy? GetDeployedById(string id)
            => LoadAllFrom(DeployedConfigFolder).FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)).Strategy;

        /// <summary>
        /// Every currently-deployed strategy, id + rule content together — used by the Alerts
        /// feature's position-lifecycle functions and by GET /api/alerts to enumerate "what's
        /// deployed right now" without each caller re-implementing the deployed-folder scan.
        /// </summary>
        public static IEnumerable<(string Id, Strategy Strategy)> GetAllDeployed()
            => LoadAllFrom(DeployedConfigFolder);

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

        /// <summary>
        /// Every strategy, one row per id, with DeployedVersion overlaid from the deployed/ folder —
        /// not read off the saved file itself. saved/ can now hold multiple version files per id (see
        /// SaveById), so this groups by slug first and surfaces only the latest version of each —
        /// otherwise every superseded draft left on disk for reference would show up as its own row.
        /// </summary>
        public static IEnumerable<(string Id, Strategy Strategy)> LoadAll()
        {
            if (!Directory.Exists(SavedConfigFolder)) yield break;

            var bySlug = Directory.EnumerateFiles(SavedConfigFolder, "*.json")
                .GroupBy(f => SlugFromFileName(Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);

            foreach (var group in bySlug)
            {
                var id = group.Key;
                var file = LatestOf(group);
                if (file == null) continue;

                Strategy? strategy;
                try
                {
                    strategy = JsonSerializer.Deserialize<Strategy>(File.ReadAllText(file), JsonOptions);
                }
                catch (JsonException)
                {
                    continue; // skip a malformed file rather than failing the whole list
                }
                if (strategy == null) continue;

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
        /// config/strategies/saved/{id}-{version}.json. Deliberately does NOT remove whatever saved
        /// file(s) previously held this id — earlier versions are left on disk on purpose, as
        /// reference/rollback material a person can inspect and clean up by hand once no longer
        /// needed; a save only ever adds a file, it never deletes one. The Version field in the
        /// incoming JSON is ignored and replaced with a server-computed auto-increment (1.0.0 for a
        /// brand new id, otherwise the latest existing saved version's patch version + 1) — the client
        /// can't set an arbitrary or backwards version number. Never touches config/strategies/deployed/
        /// — only DeployById does, and that folder keeps its single-current-file behavior unchanged.
        /// </summary>
        public static Strategy SaveById(string id, string json)
        {
            var strategy = JsonSerializer.Deserialize<Strategy>(json, JsonOptions)
                ?? throw new JsonException("Strategy JSON deserialized to null.");

            var existing = GetSavedById(id);
            strategy.Version = NextVersion(existing?.Version);
            strategy.DeployedVersion = null; // not persisted here — GetById/LoadAll overlay it from deployed/ on read

            Directory.CreateDirectory(SavedConfigFolder);

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

        /// <summary>
        /// Deletes the strategy from both saved/ and deployed/ — a full, explicit removal, distinct
        /// from routine saving. Removes ALL saved version files for this id, not just the latest —
        /// saved/ can hold several (see SaveById), and an explicit "delete this strategy" shouldn't
        /// leave orphaned old drafts behind with no id left to reach them by.
        /// </summary>
        public static bool DeleteById(string id)
        {
            var savedFiles = FindAllFilesForId(id, SavedConfigFolder).ToList();
            var deployedFile = FindFileForId(id, DeployedConfigFolder);

            if (savedFiles.Count == 0 && deployedFile == null) return false;

            foreach (var file in savedFiles) File.Delete(file);
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
        /// 0. Indicator == "Adaptive Supertrend": needs Period (its ATR length) bars to seed the
        ///    first raw ATR value, PLUS a further AdaptiveVolatilityClusterer.TrainingWindow (100,
        ///    duplicated as a local constant below — no shared project reference exists between this
        ///    service and WarmUpService/AggregationService, same as everywhere else this kind of
        ///    constant gets duplicated) bars of raw-ATR history before its K-Means volatility
        ///    clustering has a full window to train on for even the earliest seedable bar. The
        ///    generic Period-only rule below would under-provision history for this specific
        ///    indicator — WarmUpService's seeder would never have enough bars to produce a seed at
        ///    all. See WarmUpService/Indicators/AdaptiveSupertrendSeeder.cs for the full derivation.
        /// 1. Period &gt; 0 (EMA, Supertrend, any other period-based indicator): needs at least Period
        ///    bars to produce one value — the mathematical minimum, not an extra-converged buffer.
        ///    This matches the earlier "pull last 7-8 days" estimate for EMA(550) on 5-min almost
        ///    exactly (550 bars * 5 min / 375 trading-min/day ≈ 7.3 → 8 days), which is why the
        ///    minimum was chosen over a larger multiple.
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
            // Mirrors AdaptiveVolatilityClusterer.TrainingWindow (WarmUpService/AggregationService) —
            // kept as a local constant rather than a shared reference, consistent with how every
            // other cross-service constant in this codebase (IndicatorStateTtlDays, the manifest key
            // name, ...) is independently duplicated rather than shared.
            const int adaptiveSupertrendKMeansTrainingWindow = 100;

            if (reference.Period > 0 && string.Equals(reference.Indicator, "Adaptive Supertrend", StringComparison.OrdinalIgnoreCase))
            {
                var timeframeMinutes = ParseTimeframeToMinutes(timeframe);
                if (timeframeMinutes == null) return -1;
                return (int)Math.Ceiling((double)(reference.Period + adaptiveSupertrendKMeansTrainingWindow) * timeframeMinutes.Value / TradingMinutesPerDay);
            }

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
