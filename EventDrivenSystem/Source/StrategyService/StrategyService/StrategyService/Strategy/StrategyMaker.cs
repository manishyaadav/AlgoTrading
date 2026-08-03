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
        // detail that changes on every save (old-version file is removed, new one written) — this
        // is NOT a version history, just one current file per strategy with a self-documenting name.
        private static readonly Regex VersionedFileNamePattern = new(@"^(?<slug>.+)-(?<version>\d+\.\d+\.\d+)$", RegexOptions.Compiled);

        private static string ConfigFolder =>
            Path.Combine(AppContext.BaseDirectory, "config", "strategies");

        private static string SlugFromFileName(string fileNameWithoutExtension)
        {
            var match = VersionedFileNamePattern.Match(fileNameWithoutExtension);
            // tolerates a legacy/hand-dropped file with no version suffix — falls back to the whole name
            return match.Success ? match.Groups["slug"].Value : fileNameWithoutExtension;
        }

        private static string? FindFileForId(string id)
        {
            if (!Directory.Exists(ConfigFolder)) return null;
            var sanitized = SanitizeId(id);
            return Directory.EnumerateFiles(ConfigFolder, "*.json")
                .FirstOrDefault(f => string.Equals(SlugFromFileName(Path.GetFileNameWithoutExtension(f)), sanitized, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerable<(string Id, Strategy Strategy)> LoadAll()
        {
            if (!Directory.Exists(ConfigFolder))
                yield break;

            foreach (var file in Directory.EnumerateFiles(ConfigFolder, "*.json").OrderBy(f => f))
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

        public static Strategy? GetById(string id)
            => LoadAll().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)).Strategy;

        /// <summary>
        /// Validates the JSON deserializes to a Strategy, then writes it to config/strategies/{id}-{version}.json,
        /// removing whatever file previously held this id (if any) so saves don't accumulate one file per version.
        /// The Version field in the incoming JSON is ignored and replaced with a server-computed auto-increment
        /// (1.0.0 for a brand new id, otherwise the existing file's patch version + 1) — the client can't set an
        /// arbitrary or backwards version number. DeployedVersion is likewise always carried over from the
        /// existing file untouched; only DeployById changes it.
        /// </summary>
        public static Strategy SaveById(string id, string json)
        {
            var strategy = JsonSerializer.Deserialize<Strategy>(json, JsonOptions)
                ?? throw new JsonException("Strategy JSON deserialized to null.");

            var existing = GetById(id);
            strategy.Version = NextVersion(existing?.Version);
            strategy.DeployedVersion = existing?.DeployedVersion;

            Directory.CreateDirectory(ConfigFolder);

            var oldFile = FindFileForId(id);
            if (oldFile != null) File.Delete(oldFile);

            var path = Path.Combine(ConfigFolder, $"{SanitizeId(id)}-{strategy.Version}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(strategy, JsonOptions));
            return strategy;
        }

        /// <summary>Marks the strategy's current Version as the DeployedVersion. No cleanup of a previously-deployed version happens here (yet).</summary>
        public static Strategy? DeployById(string id)
        {
            var strategy = GetById(id);
            var file = FindFileForId(id);
            if (strategy == null || file == null) return null;

            strategy.DeployedVersion = strategy.Version;
            File.WriteAllText(file, JsonSerializer.Serialize(strategy, JsonOptions));
            return strategy;
        }

        public static bool DeleteById(string id)
        {
            var file = FindFileForId(id);
            if (file == null) return false;
            File.Delete(file);
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
