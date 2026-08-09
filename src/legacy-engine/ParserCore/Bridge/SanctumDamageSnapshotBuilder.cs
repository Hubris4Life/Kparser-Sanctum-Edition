// Created for KParser - Sanctum Edition, 2026. See /MODIFICATIONS.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using WaywardGamers.KParser.Database;
using WaywardGamers.KParser.Monitoring;
using WaywardGamers.KParser.Utility;

namespace WaywardGamers.KParser.Bridge
{
    internal static class SanctumDamageSnapshotBuilder
    {
        private static readonly Regex PlayerJob =
            new Regex(@"^\[(?<job>[^]]{3,15})[^]]*]", RegexOptions.Compiled);
        private static readonly string[] JobAbbreviations =
        {
            "-", "WAR", "MNK", "WHM", "BLM", "RDM", "THF", "PLD", "DRK",
            "BST", "BRD", "RNG", "SAM", "NIN", "DRG", "SMN", "BLU", "COR",
            "PUP", "DNC", "SCH", "GEO", "RUN", "MON", "-"
        };
        private static readonly Dictionary<string, string> MainJobActionSignatures =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Mighty Strikes", "WAR" }, { "Hundred Fists", "MNK" },
                { "Benediction", "WHM" }, { "Manafont", "BLM" },
                { "Chainspell", "RDM" }, { "Perfect Dodge", "THF" },
                { "Invincible", "PLD" }, { "Blood Weapon", "DRK" },
                { "Familiar", "BST" }, { "Soul Voice", "BRD" },
                { "Eagle Eye Shot", "RNG" }, { "Meikyo Shisui", "SAM" },
                { "Mijin Gakure", "NIN" }, { "Spirit Surge", "DRG" },
                { "Astral Flow", "SMN" }, { "Azure Lore", "BLU" },
                { "Wild Card", "COR" }, { "Overdrive", "PUP" },
                { "Trance", "DNC" }, { "Tabula Rasa", "SCH" },
                { "Bolster", "GEO" }, { "Elemental Sforzo", "RUN" },
                { "Blood Rage", "WAR" }, { "Brazen Rush", "WAR" },
                { "Impetus", "MNK" }, { "Inner Strength", "MNK" },
                { "Divine Caress", "WHM" }, { "Sacrosanctity", "WHM" },
                { "Cure VI", "WHM" }, { "Arise", "WHM" },
                { "Manawell", "BLM" }, { "Subtle Sorcery", "BLM" },
                { "Fire IV", "BLM" }, { "Blizzard IV", "BLM" },
                { "Aero IV", "BLM" }, { "Stone IV", "BLM" },
                { "Thunder IV", "BLM" }, { "Water IV", "BLM" },
                { "Composure", "RDM" }, { "Temper", "RDM" },
                { "Temper II", "RDM" }, { "Frazzle II", "RDM" },
                { "Frazzle III", "RDM" }, { "Distract II", "RDM" },
                { "Distract III", "RDM" },
                { "Conspirator", "THF" }, { "Larceny", "THF" },
                { "Majesty", "PLD" }, { "Palisade", "PLD" },
                { "Intervene", "PLD" }, { "Consume Mana", "DRK" },
                { "Soul Enslavement", "DRK" }, { "Unleash", "BST" },
                { "Clarion Call", "BRD" }, { "Marcato", "BRD" },
                { "Double Shot", "RNG" }, { "Overkill", "RNG" },
                { "Sengikori", "SAM" }, { "Yaegasumi", "SAM" },
                { "Futae", "NIN" }, { "Mikage", "NIN" },
                { "Spirit Bond", "DRG" }, { "Fly High", "DRG" },
                { "Apogee", "SMN" }, { "Astral Conduit", "SMN" },
                { "Unbridled Learning", "BLU" }, { "Unbridled Wisdom", "BLU" },
                { "Crooked Cards", "COR" }, { "Heady Artifice", "PUP" },
                { "Climactic Flourish", "DNC" }, { "Grand Pas", "DNC" },
                { "Perpetuance", "SCH" }, { "Caper Emissarius", "SCH" },
                { "Embrava", "SCH" }, { "Kaustra", "SCH" },
                { "Blaze of Glory", "GEO" }, { "Widened Compass", "GEO" },
                { "Battuta", "RUN" }, { "Gambit", "RUN" },
                { "Rayke", "RUN" }, { "Odyllic Subterfuge", "RUN" }
            };
        private static readonly object SnapshotCacheLock = new object();
        private static readonly Dictionary<string, SanctumBridgeSnapshot> SnapshotCache =
            new Dictionary<string, SanctumBridgeSnapshot>(StringComparer.Ordinal);
        private static long snapshotCacheRevision = -1;
        private static readonly Regex HelmHarvest = new Regex(
            @"^You (?:successfully )?harvest (?<item>(?:(?:a|an|the) )?\w+(?: \w+)*)(?:, but your sickle breaks(?: in the process)?)?[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HelmLogging = new Regex(
            @"^You (?:successfully )?cut off (?<item>(?:(?:a|an|the) )?\w+(?: \w+)*)(?:, but your hatchet breaks(?: in the process)?)?[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HelmMining = new Regex(
            @"^You (?:successfully )?dig up (?<item>(?:(?:a|an|the) )?\w+(?: \w+)*)(?:, but your pickaxe breaks(?: in the process)?)?[.!]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HelmDigging = new Regex(
            @"^Obtained: (?<item>(?:(?:a|an|the) )?\w+(?: \w+)*)\.$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static SanctumBridgeSnapshot Build(
            string requestedScope,
            int requestedBattleId,
            string requestedMobName,
            string requestedReport,
            string requestedCombatantScope,
            string requestedDisplayMode,
            string requestedGroupMode,
            string requestedSearchText,
            bool excludeCommonDrops)
        {
            SanctumBridgeSnapshot snapshot = new SanctumBridgeSnapshot();
            snapshot.EngineVersion = GetEngineVersion();
            snapshot.Report = NormalizeReport(requestedReport);
            snapshot.CombatantScope = NormalizeCombatantScope(
                snapshot.Report,
                requestedCombatantScope);
            snapshot.DisplayMode = NormalizeDisplayMode(snapshot.Report, requestedDisplayMode);
            snapshot.GroupMode = NormalizeGroupMode(
                snapshot.Report,
                snapshot.DisplayMode,
                requestedGroupMode);

            try
            {
                snapshot.ParserRunning = Monitor.Instance.IsRunning;
                snapshot.ParseMode = Monitor.Instance.ParseMode.ToString();
            }
            catch (Exception ex)
            {
                snapshot.Error = "Parser status unavailable: " + ex.Message;
            }

            KPDatabaseDataSet dataSet;
            bool acquired = DatabaseManager.Instance.TryGetDatabaseForReading(out dataSet);
            if (acquired == false)
            {
                snapshot.DatabaseOpen = DatabaseManager.Instance.IsDatabaseOpen;
                snapshot.Error = "KParser database is busy.";
                return snapshot;
            }

            try
            {
                snapshot.DatabaseOpen = (dataSet != null);
                if (dataSet == null)
                    return snapshot;

                long revision = DatabaseManager.Instance.SnapshotRevision;
                string cacheKey = BuildSnapshotCacheKey(
                    requestedScope,
                    requestedBattleId,
                    requestedMobName,
                    snapshot.Report,
                    snapshot.CombatantScope,
                    snapshot.DisplayMode,
                    snapshot.GroupMode,
                    requestedSearchText,
                    excludeCommonDrops,
                    snapshot.ParserRunning,
                    snapshot.ParseMode);
                bool cacheable = snapshot.DisplayMode != "dots" || snapshot.ParserRunning == false;
                if (cacheable)
                {
                    SanctumBridgeSnapshot cachedSnapshot = GetCachedSnapshot(revision, cacheKey);
                    if (cachedSnapshot != null)
                        return cachedSnapshot;
                }

                if (snapshot.Report == "chat")
                {
                    BuildChatReport(
                        dataSet,
                        snapshot,
                        requestedScope,
                        requestedMobName,
                        requestedSearchText);
                }
                else if (snapshot.Report == "crafting")
                {
                    BuildCraftingReport(
                        dataSet,
                        snapshot,
                        requestedScope,
                        requestedBattleId,
                        requestedSearchText);
                }
                else if (snapshot.Report == "loot" && snapshot.DisplayMode == "helm")
                {
                    BuildHelmReport(dataSet, snapshot, requestedSearchText);
                }
                else
                {
                    BuildSelectedEncounters(
                        dataSet,
                        snapshot,
                        requestedScope,
                        requestedBattleId,
                        requestedMobName,
                        requestedSearchText,
                        excludeCommonDrops);
                }
                if (cacheable)
                    StoreCachedSnapshot(revision, cacheKey, snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                snapshot.Error = "Could not build report snapshot: " + ex.Message;
                return snapshot;
            }
            finally
            {
                DatabaseManager.Instance.DoneReadingDatabase();
            }
        }

        private static string BuildSnapshotCacheKey(
            string scope,
            int battleId,
            string mobName,
            string report,
            string combatantScope,
            string displayMode,
            string groupMode,
            string searchText,
            bool excludeCommonDrops,
            bool parserRunning,
            string parseMode)
        {
            return string.Join("|", new[]
            {
                scope ?? string.Empty,
                battleId.ToString(CultureInfo.InvariantCulture),
                mobName ?? string.Empty,
                report ?? string.Empty,
                combatantScope ?? string.Empty,
                displayMode ?? string.Empty,
                groupMode ?? string.Empty,
                NormalizeSearchText(searchText),
                excludeCommonDrops ? "exclude-common" : "include-common",
                parserRunning ? "running" : "stopped",
                parseMode ?? string.Empty,
                SanctumDotProfileStore.Revision.ToString(CultureInfo.InvariantCulture)
            });
        }

        private static SanctumBridgeSnapshot GetCachedSnapshot(long revision, string key)
        {
            lock (SnapshotCacheLock)
            {
                if (snapshotCacheRevision != revision)
                {
                    SnapshotCache.Clear();
                    snapshotCacheRevision = revision;
                    return null;
                }

                SanctumBridgeSnapshot cached;
                return SnapshotCache.TryGetValue(key, out cached) ? cached : null;
            }
        }

        private static void StoreCachedSnapshot(
            long revision,
            string key,
            SanctumBridgeSnapshot snapshot)
        {
            lock (SnapshotCacheLock)
            {
                if (snapshotCacheRevision != revision)
                {
                    SnapshotCache.Clear();
                    snapshotCacheRevision = revision;
                }

                SnapshotCache[key] = snapshot;
            }
        }

        private static void BuildSelectedEncounters(
            KPDatabaseDataSet dataSet,
            SanctumBridgeSnapshot snapshot,
            string requestedScope,
            int requestedBattleId,
            string requestedMobName,
            string requestedSearchText,
            bool excludeCommonDrops)
        {
            List<KPDatabaseDataSet.BattlesRow> allBattles = dataSet.Battles
                .Where(b => b.DefaultBattle == false &&
                            b.IsEnemyIDNull() == false &&
                            b.IsStartTimeNull() == false &&
                            ((EntityType)b.CombatantsRowByEnemyCombatantRelation.CombatantType == EntityType.Mob ||
                             (EntityType)b.CombatantsRowByEnemyCombatantRelation.CombatantType == EntityType.CharmedPlayer) &&
                            HasPartyDamage(b))
                .OrderBy(b => b.StartTime)
                .ThenBy(b => b.BattleID)
                .ToList();

            AddEncounterFilters(snapshot, allBattles);
            if (allBattles.Count == 0)
                return;

            string scope = NormalizeScope(requestedScope);
            List<KPDatabaseDataSet.BattlesRow> selectedBattles = SelectBattles(
                allBattles,
                ref scope,
                requestedBattleId,
                requestedMobName);
            Dictionary<int, int> enemyIds = selectedBattles.ToDictionary(
                battle => battle.BattleID,
                battle => battle.EnemyID);
            KPDatabaseDataSet.InteractionsRow[] selectedEvents = selectedBattles
                .SelectMany(battle => battle.GetInteractionsRows())
                .ToArray();
            AddCombatantFilters(dataSet, snapshot, selectedBattles, selectedEvents);
            double durationSeconds = selectedBattles.Sum(battle =>
                GetDurationSeconds(
                    battle,
                    battle.GetInteractionsRows(),
                    snapshot.ParserRunning));

            List<SanctumCombatantSnapshot> combatants = BuildReport(
                snapshot,
                selectedBattles,
                selectedEvents,
                enemyIds,
                selectedBattles.Count,
                durationSeconds,
                dataSet,
                requestedSearchText,
                excludeCommonDrops);

            long total = combatants.Sum(row => row.Damage);
            if ((snapshot.Report == "fights" || snapshot.Report == "experience") &&
                snapshot.DisplayMode == "history")
            {
                combatants = combatants
                    .OrderBy(row => row.Rank)
                    .ToList();
            }
            else
            {
                combatants = combatants
                    .OrderByDescending(row => row.Damage)
                    .ThenBy(row => row.Name)
                    .ToList();
            }

            for (int index = 0; index < combatants.Count; index++)
            {
                SanctumCombatantSnapshot combatant = combatants[index];
                combatant.Rank = index + 1;
                combatant.SharePercent = total > 0
                    ? (double)combatant.Damage * 100.0 / total
                    : 0.0;
            }

            double aggregateRate = GetAggregateRate(
                snapshot.Report,
                snapshot.DisplayMode,
                total,
                combatants,
                selectedBattles.Count,
                durationSeconds);

            snapshot.Combatants = combatants;
            snapshot.Encounter = new SanctumEncounterSnapshot
            {
                BattleId = selectedBattles.Count == 1 ? selectedBattles[0].BattleID : 0,
                Name = scope == "all" ? "All Encounters" : GetEnemyName(selectedBattles[0]),
                Scope = scope,
                StartUtc = AsUtc(selectedBattles.Min(battle => battle.StartTime))
                    .ToString("o", CultureInfo.InvariantCulture),
                DurationSeconds = durationSeconds,
                IsActive = selectedBattles.Any(battle => battle.IsOver == false),
                FightCount = selectedBattles.Count,
                EventCount = selectedEvents.Length,
                TotalDamage = total,
                AllianceDps = aggregateRate
            };
        }

        private static List<SanctumCombatantSnapshot> BuildReport(
            SanctumBridgeSnapshot snapshot,
            IList<KPDatabaseDataSet.BattlesRow> battles,
            KPDatabaseDataSet.InteractionsRow[] events,
            IDictionary<int, int> enemyIds,
            int fightCount,
            double durationSeconds,
            KPDatabaseDataSet dataSet,
            string requestedSearchText,
            bool excludeCommonDrops)
        {
            switch (snapshot.Report)
            {
                case "damageTaken":
                    snapshot.Columns = CreateDamageTakenColumns(
                        snapshot.DisplayMode,
                        snapshot.GroupMode);
                    if (snapshot.DisplayMode == "buffperformance")
                    {
                        return BuildBuffPerformance(
                            dataSet,
                            battles,
                            events,
                            enemyIds,
                            snapshot.CombatantScope,
                            snapshot.ParserRunning,
                            durationSeconds,
                            true);
                    }
                    if (snapshot.GroupMode == "action")
                    {
                        return BuildDamageTakenByAction(
                            events,
                            snapshot.CombatantScope,
                            snapshot.DisplayMode);
                    }
                    return BuildDamageTaken(
                        events,
                        snapshot.CombatantScope,
                        snapshot.DisplayMode,
                        durationSeconds);

                case "healing":
                    snapshot.Columns = CreateHealingColumns(
                        snapshot.DisplayMode,
                        snapshot.GroupMode);
                    if (snapshot.DisplayMode == "recipients")
                        return BuildHealingRecipients(events, snapshot.CombatantScope, durationSeconds);
                    if (snapshot.DisplayMode == "recovery")
                        return BuildResourceRecovery(events, snapshot.CombatantScope, durationSeconds);
                    if (snapshot.DisplayMode == "efficiency")
                        return BuildHealingEfficiency(events, snapshot.CombatantScope, durationSeconds);
                    if (snapshot.GroupMode == "action")
                    {
                        return BuildHealingByAction(
                            events,
                            snapshot.CombatantScope,
                            snapshot.DisplayMode,
                            fightCount);
                    }
                    return BuildHealing(
                        events,
                        snapshot.CombatantScope,
                        snapshot.DisplayMode,
                        fightCount,
                        durationSeconds);

                case "buffs":
                    snapshot.Columns = CreateBuffColumns(
                        snapshot.DisplayMode,
                        snapshot.GroupMode);
                    if (snapshot.DisplayMode == "performance")
                    {
                        return BuildBuffPerformance(
                            dataSet,
                            battles,
                            events,
                            enemyIds,
                            snapshot.CombatantScope,
                            snapshot.ParserRunning,
                            durationSeconds,
                            false);
                    }
                    if (snapshot.DisplayMode == "corsair")
                        return BuildCorsairRolls(events, snapshot.CombatantScope);
                    if (snapshot.DisplayMode == "uptime")
                    {
                        return BuildBuffUptime(
                            dataSet,
                            battles,
                            events,
                            snapshot.CombatantScope,
                            snapshot.ParserRunning,
                            durationSeconds);
                    }
                    if (snapshot.GroupMode == "action")
                    {
                        return BuildBuffsByAction(
                            events,
                            snapshot.CombatantScope,
                            snapshot.DisplayMode,
                            fightCount);
                    }
                    return BuildBuffs(
                        events,
                        snapshot.CombatantScope,
                        snapshot.DisplayMode,
                        fightCount);

                case "debuffs":
                    snapshot.Columns = CreateDebuffColumns(
                        snapshot.DisplayMode,
                        snapshot.GroupMode);
                    if (snapshot.GroupMode == "action")
                    {
                        return BuildDebuffsByAction(
                            events,
                            enemyIds,
                            snapshot.CombatantScope,
                            snapshot.DisplayMode,
                            fightCount);
                    }
                    return BuildDebuffs(
                        events,
                        enemyIds,
                        snapshot.CombatantScope,
                        snapshot.DisplayMode,
                        fightCount);

                case "deaths":
                    snapshot.Columns = CreateDeathColumns();
                    return BuildDeaths(
                        events,
                        snapshot.CombatantScope,
                        fightCount);

                case "fights":
                    if (snapshot.DisplayMode == "performance")
                    {
                        snapshot.Columns = CreatePlayerPerformanceColumns();
                        return BuildPlayerPerformance(
                            battles,
                            events,
                            enemyIds,
                            snapshot.CombatantScope,
                            snapshot.ParserRunning,
                            durationSeconds);
                    }

                    snapshot.Columns = CreateFightHistoryColumns();
                    return BuildFightHistory(
                        battles,
                        snapshot.CombatantScope,
                        snapshot.ParserRunning);

                case "loot":
                    snapshot.Columns = CreateLootColumns(snapshot.DisplayMode);
                    return BuildLoot(
                        dataSet,
                        battles,
                        snapshot.DisplayMode,
                        snapshot.CombatantScope,
                        requestedSearchText,
                        excludeCommonDrops);

                case "experience":
                    snapshot.Columns = CreateExperienceColumns(snapshot.DisplayMode);
                    return BuildExperience(
                        battles,
                        snapshot.DisplayMode,
                        snapshot.ParserRunning);

                default:
                    snapshot.Columns = CreateDamageDealtColumns(
                        snapshot.DisplayMode,
                        snapshot.GroupMode);
                    if (snapshot.DisplayMode == "multiattacks")
                        return BuildMultiAttacks(events, enemyIds, snapshot.CombatantScope);
                    if (snapshot.DisplayMode == "dots")
                    {
                        return BuildDamageOverTime(
                            battles,
                            events,
                            enemyIds,
                            snapshot.CombatantScope,
                            snapshot.GroupMode,
                            snapshot.ParserRunning,
                            durationSeconds);
                    }
                    List<SanctumDotAggregate> integratedDots = GetIntegratedDotEstimates(
                        battles,
                        events,
                        enemyIds,
                        snapshot.CombatantScope,
                        snapshot.DisplayMode,
                        snapshot.ParserRunning);
                    if (snapshot.GroupMode == "action")
                    {
                        return BuildDamageDealtByAction(
                            events,
                            enemyIds,
                            snapshot.CombatantScope,
                            snapshot.DisplayMode,
                            integratedDots);
                    }
                    return BuildDamageDealt(
                        events,
                        enemyIds,
                        snapshot.CombatantScope,
                        snapshot.DisplayMode,
                        durationSeconds,
                        integratedDots);
            }
        }

        private static void BuildChatReport(
            KPDatabaseDataSet dataSet,
            SanctumBridgeSnapshot snapshot,
            string requestedScope,
            string requestedSpeaker,
            string requestedSearchText)
        {
            snapshot.Columns = CreateChatColumns();
            snapshot.Filters.Clear();
            snapshot.Filters.Add(new SanctumEncounterFilterSnapshot
            {
                Scope = "all",
                Label = "All speakers"
            });

            foreach (KPDatabaseDataSet.ChatSpeakersRow speaker in dataSet.ChatSpeakers
                .OrderBy(row => row.SpeakerName))
            {
                if (speaker.GetChatMessagesRows().Length == 0)
                    continue;

                snapshot.Filters.Add(new SanctumEncounterFilterSnapshot
                {
                    Scope = "speaker",
                    MobName = speaker.SpeakerName,
                    Label = speaker.SpeakerName
                });
            }

            string selectedSpeaker = string.Equals(
                requestedScope,
                "speaker",
                StringComparison.OrdinalIgnoreCase)
                ? (requestedSpeaker ?? string.Empty).Trim()
                : string.Empty;
            string searchText = NormalizeSearchText(requestedSearchText);

            IEnumerable<KPDatabaseDataSet.ChatMessagesRow> query = dataSet.ChatMessages;
            if (selectedSpeaker.Length > 0)
            {
                query = query.Where(row => string.Equals(
                    row.ChatSpeakersRow.SpeakerName,
                    selectedSpeaker,
                    StringComparison.OrdinalIgnoreCase));
            }
            if (snapshot.DisplayMode != "all")
            {
                query = query.Where(row => ChatTypeMatches(
                    snapshot.DisplayMode,
                    (ChatMessageType)row.ChatType));
            }
            if (searchText.Length > 0)
            {
                query = query.Where(row =>
                    ContainsIgnoreCase(row.Message, searchText) ||
                    ContainsIgnoreCase(row.ChatSpeakersRow.SpeakerName, searchText));
            }

            List<KPDatabaseDataSet.ChatMessagesRow> filtered = query
                .OrderByDescending(row => row.Timestamp)
                .ThenByDescending(row => row.ChatMessageID)
                .ToList();
            List<KPDatabaseDataSet.ChatMessagesRow> visible = filtered
                .Take(500)
                .ToList();

            List<SanctumCombatantSnapshot> rows = new List<SanctumCombatantSnapshot>();
            for (int index = 0; index < visible.Count; index++)
            {
                KPDatabaseDataSet.ChatMessagesRow message = visible[index];
                string channel = GetChatTypeLabel((ChatMessageType)message.ChatType);
                rows.Add(new SanctumCombatantSnapshot
                {
                    Key = "chat:" + message.ChatMessageID.ToString(CultureInfo.InvariantCulture),
                    Rank = index + 1,
                    Name = message.ChatSpeakersRow.SpeakerName,
                    Job = channel,
                    CombatantType = "Chat",
                    PrimaryText = message.Timestamp.ToLocalTime().ToString("g"),
                    Detail1Text = message.Message,
                    TopAction = message.Message,
                    Accuracy = channel,
                    CriticalRate = string.Empty
                });
            }

            DateTime start = visible.Count > 0
                ? visible.Min(row => row.Timestamp)
                : DateTime.UtcNow;
            DateTime end = visible.Count > 0
                ? visible.Max(row => row.Timestamp)
                : start;
            int speakerCount = filtered
                .Select(row => row.SpeakerID)
                .Distinct()
                .Count();

            snapshot.Combatants = rows;
            snapshot.Encounter = new SanctumEncounterSnapshot
            {
                Name = selectedSpeaker.Length > 0 ? selectedSpeaker : "All Chat",
                Scope = selectedSpeaker.Length > 0 ? "speaker" : "chat",
                StartUtc = AsUtc(start).ToString("o", CultureInfo.InvariantCulture),
                DurationSeconds = Math.Max(0.0, (end - start).TotalSeconds),
                IsActive = snapshot.ParserRunning,
                FightCount = speakerCount,
                EventCount = filtered.Count,
                TotalDamage = visible.Count,
                AllianceDps = speakerCount
            };
        }

        private sealed class HelmActivityAggregate
        {
            internal HelmActivityAggregate(string name)
            {
                Name = name;
                Items = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            internal string Name { get; private set; }
            internal Dictionary<string, int> Items { get; private set; }
            internal int Failures { get; set; }
            internal int Breaks { get; set; }
            internal int FoundWithEase { get; set; }
            internal int Attempts { get { return Items.Sum(item => item.Value) + Failures; } }
        }

        private static void BuildHelmReport(
            KPDatabaseDataSet dataSet,
            SanctumBridgeSnapshot snapshot,
            string requestedSearchText)
        {
            snapshot.Columns = CreateLootColumns("helm");
            snapshot.Filters.Clear();
            snapshot.Filters.Add(new SanctumEncounterFilterSnapshot
            {
                Scope = "all",
                Label = "All HELM activity"
            });
            snapshot.CombatantFilters.Clear();

            HelmActivityAggregate harvesting = new HelmActivityAggregate("Harvesting");
            HelmActivityAggregate logging = new HelmActivityAggregate("Logging");
            HelmActivityAggregate mining = new HelmActivityAggregate("Mining");
            HelmActivityAggregate digging = new HelmActivityAggregate("Chocobo digging");
            HelmActivityAggregate[] activities = { harvesting, logging, mining, digging };
            List<KPDatabaseDataSet.ChatMessagesRow> arena = dataSet.ChatMessages
                .Where(row => (ChatMessageType)row.ChatType == ChatMessageType.Arena)
                .OrderBy(row => row.Timestamp)
                .ToList();

            foreach (KPDatabaseDataSet.ChatMessagesRow messageRow in arena)
            {
                string message = messageRow.Message ?? string.Empty;
                Match match = HelmHarvest.Match(message);
                HelmActivityAggregate activity = harvesting;
                if (match.Success == false)
                {
                    match = HelmLogging.Match(message);
                    activity = logging;
                }
                if (match.Success == false)
                {
                    match = HelmMining.Match(message);
                    activity = mining;
                }
                if (match.Success == false)
                {
                    match = HelmDigging.Match(message);
                    activity = digging;
                }
                if (match.Success)
                {
                    string item = match.Groups["item"].Value.Trim();
                    int count;
                    activity.Items.TryGetValue(item, out count);
                    activity.Items[item] = count + 1;
                }

                if (string.Equals(message, "You are unable to harvest anything.", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(message, "Your sickle breaks!", StringComparison.OrdinalIgnoreCase))
                    harvesting.Failures++;
                if (string.Equals(message, "You are unable to log anything.", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(message, "Your hatchet breaks!", StringComparison.OrdinalIgnoreCase))
                    logging.Failures++;
                if (string.Equals(message, "You are unable to dig up anything.", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(message, "Your pickaxe breaks!", StringComparison.OrdinalIgnoreCase))
                    mining.Failures++;
                if (string.Equals(message, "You dig and you dig, but find nothing.", StringComparison.OrdinalIgnoreCase))
                    digging.Failures++;
                if (Regex.IsMatch(message, @"sickle breaks(?: in the process)?[.!]$", RegexOptions.IgnoreCase))
                    harvesting.Breaks++;
                if (Regex.IsMatch(message, @"hatchet breaks(?: in the process)?[.!]$", RegexOptions.IgnoreCase))
                    logging.Breaks++;
                if (Regex.IsMatch(message, @"pickaxe breaks(?: in the process)?[.!]$", RegexOptions.IgnoreCase))
                    mining.Breaks++;
                if (string.Equals(message, "It appears your chocobo found this item with ease.", StringComparison.OrdinalIgnoreCase))
                    digging.FoundWithEase++;
            }

            string searchText = NormalizeSearchText(requestedSearchText);
            List<SanctumCombatantSnapshot> rows = new List<SanctumCombatantSnapshot>();
            foreach (HelmActivityAggregate activity in activities)
            {
                foreach (KeyValuePair<string, int> item in activity.Items
                    .Where(item => searchText.Length == 0 ||
                                   ContainsIgnoreCase(item.Key, searchText) ||
                                   ContainsIgnoreCase(activity.Name, searchText))
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Key))
                {
                    double findRate = activity.Attempts == 0
                        ? 0.0
                        : (double)item.Value * 100.0 / activity.Attempts;
                    rows.Add(new SanctumCombatantSnapshot
                    {
                        Key = "helm:" + activity.Name + ":" + item.Key,
                        Name = item.Key,
                        Job = activity.Name,
                        CombatantType = "HELM",
                        Damage = item.Value,
                        Dps = findRate,
                        Melee = activity.Attempts,
                        WeaponSkills = activity.Failures,
                        Magic = activity.Breaks,
                        Other = activity.FoundWithEase,
                        RateText = findRate.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                        TopAction = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:N0} found in {1:N0} {2} attempts",
                            item.Value,
                            activity.Attempts,
                            activity.Name.ToLowerInvariant()),
                        Accuracy = "Nothing found: " + activity.Failures.ToString("N0", CultureInfo.InvariantCulture),
                        CriticalRate = "Tool breaks: " + activity.Breaks.ToString("N0", CultureInfo.InvariantCulture)
                    });
                }
            }

            long total = rows.Sum(row => row.Damage);
            rows = rows.OrderByDescending(row => row.Damage).ThenBy(row => row.Name).ToList();
            for (int index = 0; index < rows.Count; index++)
            {
                rows[index].Rank = index + 1;
                rows[index].SharePercent = total == 0 ? 0.0 :
                    (double)rows[index].Damage * 100.0 / total;
            }
            DateTime start = arena.Count == 0 ? DateTime.UtcNow : arena.Min(item => item.Timestamp);
            DateTime end = arena.Count == 0 ? start : arena.Max(item => item.Timestamp);
            int attempts = activities.Sum(item => item.Attempts);
            snapshot.Combatants = rows;
            snapshot.Encounter = new SanctumEncounterSnapshot
            {
                Name = "HELM Activity",
                Scope = "helm",
                StartUtc = AsUtc(start).ToString("o", CultureInfo.InvariantCulture),
                DurationSeconds = Math.Max(0.0, (end - start).TotalSeconds),
                IsActive = snapshot.ParserRunning,
                FightCount = attempts,
                EventCount = arena.Count,
                TotalDamage = total,
                AllianceDps = rows.Count == 0 ? 0.0 : rows.Average(row => row.Dps)
            };
        }

        private static void BuildCraftingReport(
            KPDatabaseDataSet dataSet,
            SanctumBridgeSnapshot snapshot,
            string requestedScope,
            int requestedSessionId,
            string requestedSearchText)
        {
            snapshot.Columns = CreateCraftingColumns(snapshot.DisplayMode);
            List<SanctumCraftingAttempt> allAttempts = SanctumCraftingTracker.Parse(dataSet.RecordLog);
            string localPlayerName = SanctumDotProfileStore.CurrentPlayerName;
            if (string.IsNullOrEmpty(localPlayerName))
            {
                localPlayerName = dataSet.Interactions
                    .Where(row => row.IsActorIDNull() == false &&
                                  (ActorPlayerType)row.ActorType == ActorPlayerType.Self &&
                                  row.CombatantsRowByActorCombatantRelation != null)
                    .OrderByDescending(row => row.Timestamp)
                    .Select(row => row.CombatantsRowByActorCombatantRelation.CombatantName)
                    .FirstOrDefault();
            }
            if (string.IsNullOrEmpty(localPlayerName) == false)
            {
                foreach (SanctumCraftingAttempt attempt in allAttempts.Where(item =>
                    string.Equals(item.Crafter, "You", StringComparison.OrdinalIgnoreCase)))
                {
                    attempt.Crafter = localPlayerName;
                }
            }
            List<SanctumCraftingSession> allSessions = SanctumCraftingTracker.CreateSessions(allAttempts);

            snapshot.Filters.Clear();
            snapshot.Filters.Add(new SanctumEncounterFilterSnapshot
            {
                Scope = "all",
                Label = string.Format(
                    CultureInfo.InvariantCulture,
                    "All crafting sessions - {0:N0} attempt{1}",
                    allAttempts.Count,
                    allAttempts.Count == 1 ? string.Empty : "s")
            });
            foreach (SanctumCraftingSession session in allSessions
                .OrderByDescending(item => item.Start)
                .Take(30))
            {
                int successful = session.Attempts.Count(item => item.Success);
                double successRate = session.Attempts.Count == 0
                    ? 0.0
                    : (double)successful * 100.0 / session.Attempts.Count;
                snapshot.Filters.Add(new SanctumEncounterFilterSnapshot
                {
                    Scope = "craftingSession",
                    BattleId = session.Id,
                    Label = string.Format(
                        CultureInfo.InvariantCulture,
                        "Crafting session - {0:g} ({1:N0} attempts, {2:0.0}% success)",
                        AsUtc(session.Start).ToLocalTime(),
                        session.Attempts.Count,
                        successRate)
                });
            }

            snapshot.CombatantFilters.Clear();
            snapshot.CombatantFilters.Add(new SanctumCombatantFilterSnapshot
            {
                Key = "all",
                Label = "All crafters"
            });
            foreach (string crafter in allAttempts
                .Select(item => item.Crafter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                snapshot.CombatantFilters.Add(new SanctumCombatantFilterSnapshot
                {
                    Key = "crafter:" + crafter,
                    Label = crafter
                });
            }

            if (allAttempts.Count == 0)
                return;

            SanctumCraftingSession selectedSession = null;
            if (string.Equals(requestedScope, "craftingSession", StringComparison.OrdinalIgnoreCase) &&
                requestedSessionId > 0)
            {
                selectedSession = allSessions.FirstOrDefault(item => item.Id == requestedSessionId);
            }

            IEnumerable<SanctumCraftingAttempt> query = selectedSession == null
                ? allAttempts
                : selectedSession.Attempts;
            if (snapshot.DisplayMode == "mine")
            {
                string currentCrafter = string.IsNullOrEmpty(localPlayerName)
                    ? "You"
                    : localPlayerName;
                query = query.Where(item => string.Equals(
                    item.Crafter,
                    currentCrafter,
                    StringComparison.OrdinalIgnoreCase));
            }
            if (snapshot.DisplayMode != "mine" &&
                snapshot.CombatantScope.StartsWith("crafter:", StringComparison.Ordinal))
            {
                string crafter = snapshot.CombatantScope.Substring("crafter:".Length);
                query = query.Where(item => string.Equals(
                    item.Crafter,
                    crafter,
                    StringComparison.OrdinalIgnoreCase));
            }

            string searchText = NormalizeSearchText(requestedSearchText);
            if (searchText.Length > 0)
            {
                query = query.Where(item =>
                    ContainsIgnoreCase(item.Crafter, searchText) ||
                    ContainsIgnoreCase(item.Item, searchText) ||
                    ContainsIgnoreCase(item.SkillName, searchText) ||
                    ContainsIgnoreCase(item.ResultText, searchText) ||
                    item.LostMaterials.Any(material => ContainsIgnoreCase(material, searchText)));
            }

            List<SanctumCraftingAttempt> attempts = query
                .OrderBy(item => item.Timestamp)
                .ThenBy(item => item.RecordLogId)
                .ToList();
            List<SanctumCombatantSnapshot> rows;
            switch (snapshot.DisplayMode)
            {
                case "history":
                    rows = BuildCraftingHistory(attempts);
                    break;
                case "skillups":
                    rows = BuildCraftingSkillUps(attempts);
                    break;
                case "materials":
                    rows = BuildCraftingMaterials(attempts, allSessions);
                    break;
                default:
                    rows = BuildCraftingSummary(attempts);
                    break;
            }

            for (int index = 0; index < rows.Count; index++)
                rows[index].Rank = index + 1;

            int successes = attempts.Count(item => item.Success);
            double aggregateSuccessRate = attempts.Count == 0
                ? 0.0
                : (double)successes * 100.0 / attempts.Count;
            DateTime start = attempts.Count > 0
                ? attempts.Min(item => item.Timestamp)
                : selectedSession == null ? DateTime.UtcNow : selectedSession.Start;
            DateTime end = attempts.Count > 0
                ? attempts.Max(item => item.Timestamp)
                : start;
            HashSet<int> attemptIds = new HashSet<int>(attempts.Select(item => item.RecordLogId));
            int matchingSessions = allSessions.Count(session =>
                session.Attempts.Any(attempt => attemptIds.Contains(attempt.RecordLogId)));

            snapshot.Combatants = rows;
            snapshot.Encounter = new SanctumEncounterSnapshot
            {
                BattleId = selectedSession == null ? 0 : selectedSession.Id,
                Name = selectedSession == null
                    ? snapshot.DisplayMode == "mine" ? "My Crafting" : "All Crafting"
                    : AsUtc(selectedSession.Start).ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                Scope = selectedSession == null ? "crafting" : "craftingSession",
                StartUtc = AsUtc(start).ToString("o", CultureInfo.InvariantCulture),
                DurationSeconds = Math.Max(0.0, (end - start).TotalSeconds),
                IsActive = snapshot.ParserRunning &&
                           (DateTime.UtcNow - AsUtc(end)).TotalMinutes < 30.0,
                FightCount = matchingSessions,
                EventCount = attempts.Count,
                TotalDamage = attempts.Count,
                AllianceDps = aggregateSuccessRate
            };
        }

        private static List<SanctumCombatantSnapshot> BuildCraftingSummary(
            IList<SanctumCraftingAttempt> attempts)
        {
            int totalAttempts = attempts.Count;
            List<SanctumCombatantSnapshot> rows = new List<SanctumCombatantSnapshot>();
            foreach (IGrouping<string, SanctumCraftingAttempt> group in attempts
                .GroupBy(item => item.Item, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(item => item.Count())
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                int successes = group.Count(item => item.Success);
                int failures = group.Count() - successes;
                int highQuality = group.Count(item => item.HighQuality);
                int skillUps = group.Count(item => item.SkillGain > 0.0 || item.SkillLevel > 0.0);
                int materialsLost = group.Sum(item => item.LostMaterials.Count);
                long yield = group.Sum(item => (long)item.Yield);
                double successRate = group.Any()
                    ? (double)successes * 100.0 / group.Count()
                    : 0.0;
                string crafters = JoinCraftValues(group.Select(item => item.Crafter), 3);
                bool desynthesis = group.Any(item => item.IsDesynthesis);

                rows.Add(new SanctumCombatantSnapshot
                {
                    Key = "craft:recipe:" + group.Key.ToLowerInvariant(),
                    Name = group.Key,
                    Job = crafters,
                    CombatantType = "Crafting",
                    Damage = group.Count(),
                    SharePercent = totalAttempts == 0
                        ? 0.0
                        : (double)group.Count() * 100.0 / totalAttempts,
                    Dps = successRate,
                    Melee = successes,
                    WeaponSkills = failures,
                    Magic = highQuality,
                    Other = yield,
                    RateText = successRate.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                    TopAction = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:N0} success, {1:N0} break{2}, {3:N0} total yield",
                        successes,
                        failures,
                        failures == 1 ? string.Empty : "s",
                        yield),
                    Accuracy = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:N0} skill-up{1} · {2:N0} material{3} lost{4}",
                        skillUps,
                        skillUps == 1 ? string.Empty : "s",
                        materialsLost,
                        materialsLost == 1 ? string.Empty : "s",
                        desynthesis ? " · desynthesis" : string.Empty),
                    CriticalRate = highQuality.ToString(CultureInfo.InvariantCulture) + " HQ"
                });
            }
            return rows;
        }

        private static List<SanctumCombatantSnapshot> BuildCraftingHistory(
            IEnumerable<SanctumCraftingAttempt> attempts)
        {
            List<SanctumCombatantSnapshot> rows = new List<SanctumCombatantSnapshot>();
            foreach (SanctumCraftingAttempt attempt in attempts
                .OrderByDescending(item => item.Timestamp)
                .ThenByDescending(item => item.RecordLogId)
                .Take(750))
            {
                string skillUp = attempt.SkillGain > 0.0
                    ? attempt.SkillName + " +" + attempt.SkillGain.ToString("0.0", CultureInfo.InvariantCulture)
                    : attempt.SkillLevel > 0.0
                        ? attempt.SkillName + " level " + attempt.SkillLevel.ToString("0.#", CultureInfo.InvariantCulture)
                        : "-";
                string materials = attempt.LostMaterials.Count > 0
                    ? JoinCraftValues(attempt.LostMaterials, 4)
                    : "-";
                rows.Add(new SanctumCombatantSnapshot
                {
                    Key = "craft:attempt:" + attempt.RecordLogId.ToString(CultureInfo.InvariantCulture),
                    Name = attempt.Item,
                    Job = attempt.Crafter,
                    CombatantType = "Crafting",
                    Damage = 1,
                    PrimaryText = AsUtc(attempt.Timestamp).ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    ShareText = "-",
                    RateText = attempt.Success ? "Success" : "Break",
                    Detail1Text = attempt.Success
                        ? attempt.Yield.ToString("N0", CultureInfo.InvariantCulture)
                        : "-",
                    Detail2Text = attempt.Success
                        ? attempt.HighQuality ? "HQ" : "NQ"
                        : "-",
                    Detail3Text = skillUp,
                    Detail4Text = materials,
                    TopAction = attempt.ResultText,
                    Accuracy = attempt.IsDesynthesis ? "Desynthesis" : "Synthesis",
                    CriticalRate = attempt.Success ? (attempt.HighQuality ? "HQ" : "NQ") : "Failed"
                });
            }
            return rows;
        }

        private static List<SanctumCombatantSnapshot> BuildCraftingSkillUps(
            IEnumerable<SanctumCraftingAttempt> attempts)
        {
            List<SanctumCraftingAttempt> skillAttempts = attempts
                .Where(item => item.SkillName.Length > 0 &&
                               (item.SkillGain > 0.0 || item.SkillLevel > 0.0))
                .ToList();
            List<SanctumCombatantSnapshot> rows = new List<SanctumCombatantSnapshot>();
            foreach (var group in skillAttempts
                .GroupBy(item => new { item.SkillName, item.Crafter })
                .OrderBy(item => item.Key.SkillName)
                .ThenBy(item => item.Key.Crafter))
            {
                double totalGain = group.Sum(item => item.SkillGain);
                double latestLevel = group.Max(item => item.SkillLevel);
                int successes = group.Count(item => item.Success);
                double successRate = group.Any()
                    ? (double)successes * 100.0 / group.Count()
                    : 0.0;
                SanctumCraftingAttempt latest = group.OrderByDescending(item => item.Timestamp).First();
                rows.Add(new SanctumCombatantSnapshot
                {
                    Key = "craft:skill:" + group.Key.SkillName.ToLowerInvariant() + ":" + group.Key.Crafter.ToLowerInvariant(),
                    Name = group.Key.SkillName,
                    Job = group.Key.Crafter,
                    CombatantType = "Crafting",
                    Damage = group.Count(),
                    PrimaryText = "+" + totalGain.ToString("0.0", CultureInfo.InvariantCulture),
                    ShareText = "-",
                    RateText = successRate.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                    Detail1Text = group.Count().ToString("N0", CultureInfo.InvariantCulture),
                    Detail2Text = group.Select(item => item.RecordLogId).Distinct().Count().ToString("N0", CultureInfo.InvariantCulture),
                    Detail3Text = latestLevel > 0.0
                        ? latestLevel.ToString("0.#", CultureInfo.InvariantCulture)
                        : "-",
                    Detail4Text = AsUtc(latest.Timestamp).ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    TopAction = "Latest skill-up while crafting " + latest.Item,
                    Accuracy = group.Count().ToString(CultureInfo.InvariantCulture) + " skill-up events",
                    CriticalRate = latest.Item
                });
            }
            return rows;
        }

        private static List<SanctumCombatantSnapshot> BuildCraftingMaterials(
            IEnumerable<SanctumCraftingAttempt> attempts,
            IEnumerable<SanctumCraftingSession> sessions)
        {
            var losses = attempts.SelectMany(attempt => attempt.LostMaterials.Select(material => new
            {
                Attempt = attempt,
                Material = material
            })).ToList();
            int totalLosses = losses.Count;
            List<SanctumCombatantSnapshot> rows = new List<SanctumCombatantSnapshot>();
            foreach (var group in losses
                .GroupBy(item => item.Material, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(item => item.Count())
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                SanctumCraftingAttempt latest = group
                    .OrderByDescending(item => item.Attempt.Timestamp)
                    .First().Attempt;
                HashSet<int> recordIds = new HashSet<int>(group.Select(item => item.Attempt.RecordLogId));
                int sessionCount = sessions.Count(session =>
                    session.Attempts.Any(attempt => recordIds.Contains(attempt.RecordLogId)));
                int failedCrafts = group.Select(item => item.Attempt.RecordLogId).Distinct().Count();
                string crafters = JoinCraftValues(group.Select(item => item.Attempt.Crafter), 3);
                string recipes = JoinCraftValues(group.Select(item => item.Attempt.Item), 3);
                double perBreak = failedCrafts == 0 ? 0.0 : (double)group.Count() / failedCrafts;
                rows.Add(new SanctumCombatantSnapshot
                {
                    Key = "craft:material:" + group.Key.ToLowerInvariant(),
                    Name = group.Key,
                    Job = crafters,
                    CombatantType = "Crafting",
                    Damage = group.Count(),
                    SharePercent = totalLosses == 0
                        ? 0.0
                        : (double)group.Count() * 100.0 / totalLosses,
                    Dps = perBreak,
                    RateText = perBreak.ToString("0.00", CultureInfo.InvariantCulture),
                    Melee = failedCrafts,
                    WeaponSkills = group.Select(item => item.Attempt.Item).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Magic = sessionCount,
                    Detail2Text = recipes,
                    Detail4Text = AsUtc(latest.Timestamp).ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    TopAction = "Most recent loss while crafting " + latest.Item,
                    Accuracy = failedCrafts.ToString(CultureInfo.InvariantCulture) + " affected breaks",
                    CriticalRate = latest.Crafter
                });
            }
            return rows;
        }

        private static string JoinCraftValues(IEnumerable<string> values, int maximum)
        {
            string[] distinct = values
                .Where(item => string.IsNullOrEmpty(item) == false)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinct.Length <= maximum)
                return string.Join(", ", distinct);
            return string.Join(", ", distinct.Take(maximum).ToArray()) +
                   " +" + (distinct.Length - maximum).ToString(CultureInfo.InvariantCulture);
        }

        private static List<SanctumCombatantSnapshot> BuildLoot(
            KPDatabaseDataSet dataSet,
            IList<KPDatabaseDataSet.BattlesRow> battles,
            string displayMode,
            string combatantScope,
            string requestedSearchText,
            bool excludeCommonDrops)
        {
            HashSet<int> battleIds = new HashSet<int>(battles.Select(row => row.BattleID));
            KPDatabaseDataSet.InteractionsRow[] events = battles
                .SelectMany(row => row.GetInteractionsRows())
                .ToArray();
            string searchText = NormalizeSearchText(requestedSearchText);

            IEnumerable<KPDatabaseDataSet.LootRow> query = dataSet.Loot.Where(row =>
                row.IsBattleIDNull() == false && battleIds.Contains(row.BattleID));

            if (excludeCommonDrops)
                query = query.Where(row => IsCommonDrop(GetLootItemName(row)) == false);

            if (combatantScope.StartsWith("recipient:", StringComparison.Ordinal))
            {
                string recipient = combatantScope.Substring("recipient:".Length);
                query = query.Where(row => string.Equals(
                    GetLootRecipient(row),
                    recipient,
                    StringComparison.OrdinalIgnoreCase));
            }
            else if (combatantScope == "players")
            {
                query = query.Where(row => row.IsPlayerIDNull() == false &&
                    (EntityType)row.CombatantsRow.CombatantType == EntityType.Player);
            }
            else if (combatantScope == "party")
            {
                query = query.Where(row => row.IsPlayerIDNull() == false &&
                    IsInCombatantScope(
                        row.CombatantsRow,
                        (EntityType)row.CombatantsRow.CombatantType,
                        events,
                        "party"));
            }

            if (searchText.Length > 0)
            {
                query = query.Where(row =>
                    ContainsIgnoreCase(GetLootItemName(row), searchText) ||
                    ContainsIgnoreCase(GetLootRecipient(row), searchText) ||
                    ContainsIgnoreCase(GetLootMobName(row), searchText));
            }

            List<KPDatabaseDataSet.LootRow> loot = query.ToList();
            if (displayMode == "distribution")
                return BuildLootDistribution(loot, battles.Count);
            if (displayMode == "rates")
                return BuildLootRates(loot, battles, false);
            if (displayMode == "treasurehunter")
                return BuildLootRates(loot, battles, true);
            return BuildLootSummary(loot, battles.Count);
        }

        private static List<SanctumCombatantSnapshot> BuildLootSummary(
            IEnumerable<KPDatabaseDataSet.LootRow> loot,
            int fightCount)
        {
            List<SanctumCombatantSnapshot> rows = new List<SanctumCombatantSnapshot>();
            foreach (IGrouping<string, KPDatabaseDataSet.LootRow> itemGroup in loot
                .GroupBy(GetLootItemName)
                .OrderBy(group => GetDisplayItemName(group.Key)))
            {
                bool currency = IsCurrencyItem(itemGroup.Key);
                long quantity = currency
                    ? itemGroup.Sum(row => (long)row.GilDropped)
                    : itemGroup.LongCount();
                int recipients = itemGroup
                    .Where(row => row.IsPlayerIDNull() == false)
                    .Select(row => row.PlayerID)
                    .Distinct()
                    .Count();
                int lost = itemGroup.Count(row => row.Lost);
                int mobCount = itemGroup
                    .Where(row => row.IsBattleIDNull() == false)
                    .Select(GetLootMobName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                rows.Add(new SanctumCombatantSnapshot
                {
                    Key = "loot:item:" + itemGroup.Key,
                    Name = GetDisplayItemName(itemGroup.Key),
                    Job = currency ? "Currency" : "Item",
                    CombatantType = "Loot",
                    Damage = quantity,
                    Dps = fightCount > 0 ? (double)quantity / fightCount : 0.0,
                    Melee = recipients,
                    WeaponSkills = lost,
                    Magic = mobCount,
                    Other = itemGroup.Count(),
                    TopAction = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:N0} total from {1:N0} recorded drop{2}",
                        quantity,
                        itemGroup.Count(),
                        itemGroup.Count() == 1 ? string.Empty : "s"),
                    Accuracy = lost > 0 ? lost.ToString(CultureInfo.InvariantCulture) + " lost" : "No lost drops",
                    CriticalRate = recipients.ToString(CultureInfo.InvariantCulture) + " recipients"
                });
            }
            return rows;
        }

        private static List<SanctumCombatantSnapshot> BuildLootDistribution(
            IEnumerable<KPDatabaseDataSet.LootRow> loot,
            int fightCount)
        {
            List<SanctumCombatantSnapshot> rows = new List<SanctumCombatantSnapshot>();
            var groups = loot
                .GroupBy(row => new
                {
                    Recipient = GetLootRecipient(row),
                    Item = GetLootItemName(row)
                })
                .OrderBy(group => group.Key.Recipient)
                .ThenBy(group => GetDisplayItemName(group.Key.Item));

            foreach (var group in groups)
            {
                bool currency = IsCurrencyItem(group.Key.Item);
                long quantity = currency
                    ? group.Sum(row => (long)row.GilDropped)
                    : group.LongCount();
                int lost = group.Count(row => row.Lost);
                int mobs = group.Select(GetLootMobName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                rows.Add(new SanctumCombatantSnapshot
                {
                    Key = "loot:recipient:" + group.Key.Recipient + ":" + group.Key.Item,
                    Name = group.Key.Recipient,
                    Job = GetDisplayItemName(group.Key.Item),
                    CombatantType = "Loot",
                    Damage = quantity,
                    Dps = fightCount > 0 ? (double)quantity / fightCount : 0.0,
                    Melee = group.Count(),
                    WeaponSkills = lost,
                    Magic = mobs,
                    Other = 0,
                    TopAction = GetDisplayItemName(group.Key.Item) + " received by " + group.Key.Recipient,
                    Accuracy = lost > 0 ? lost.ToString(CultureInfo.InvariantCulture) + " lost" : "Received",
                    CriticalRate = mobs.ToString(CultureInfo.InvariantCulture) + " mob types"
                });
            }
            return rows;
        }

        private static List<SanctumCombatantSnapshot> BuildLootRates(
            IEnumerable<KPDatabaseDataSet.LootRow> loot,
            IList<KPDatabaseDataSet.BattlesRow> battles,
            bool groupByTreasureHunter)
        {
            Dictionary<int, int> treasureHunter = battles.ToDictionary(
                battle => battle.BattleID,
                battle => battle.GetInteractionsRows()
                    .Where(row => (HarmType)row.SecondHarmType == HarmType.TreasureHunter)
                    .Select(row => row.SecondAmount)
                    .DefaultIfEmpty(0)
                    .Max());

            var battleGroups = battles
                .Where(battle => battle.Killed)
                .GroupBy(battle => new
                {
                    Mob = GetEnemyName(battle),
                    TreasureHunter = groupByTreasureHunter
                        ? treasureHunter[battle.BattleID]
                        : -1
                })
                .ToDictionary(
                    group => group.Key.Mob + "\u001f" + group.Key.TreasureHunter.ToString(CultureInfo.InvariantCulture),
                    group => group.Select(battle => battle.BattleID).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            List<SanctumCombatantSnapshot> rows = new List<SanctumCombatantSnapshot>();
            foreach (var battleGroup in battleGroups.OrderBy(pair => pair.Key))
            {
                string[] keyParts = battleGroup.Key.Split('\u001f');
                string mob = keyParts[0];
                int th = int.Parse(keyParts[1], CultureInfo.InvariantCulture);
                HashSet<int> ids = new HashSet<int>(battleGroup.Value);
                List<KPDatabaseDataSet.LootRow> groupLoot = loot
                    .Where(row => row.IsBattleIDNull() == false && ids.Contains(row.BattleID))
                    .ToList();

                foreach (IGrouping<string, KPDatabaseDataSet.LootRow> itemGroup in groupLoot
                    .Where(row => IsCurrencyItem(GetLootItemName(row)) == false)
                    .GroupBy(GetLootItemName)
                    .OrderBy(group => GetDisplayItemName(group.Key)))
                {
                    int kills = battleGroup.Value.Count;
                    int drops = itemGroup.Count();
                    int successfulKills = itemGroup.Select(row => row.BattleID).Distinct().Count();
                    int lost = itemGroup.Count(row => row.Lost);
                    double rate = kills > 0 ? (double)drops * 100.0 / kills : 0.0;
                    string thLabel = groupByTreasureHunter
                        ? (th > 0 ? "TH " + th.ToString(CultureInfo.InvariantCulture) : "TH unknown")
                        : GetDisplayItemName(itemGroup.Key);

                    rows.Add(new SanctumCombatantSnapshot
                    {
                        Key = "loot:rate:" + mob + ":" + th.ToString(CultureInfo.InvariantCulture) + ":" + itemGroup.Key,
                        Name = mob,
                        Job = groupByTreasureHunter
                            ? thLabel + " - " + GetDisplayItemName(itemGroup.Key)
                            : GetDisplayItemName(itemGroup.Key),
                        CombatantType = "Loot",
                        Damage = drops,
                        Dps = rate,
                        RateText = rate.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                        Melee = kills,
                        WeaponSkills = successfulKills,
                        Magic = lost,
                        Other = itemGroup
                            .Where(row => row.IsPlayerIDNull() == false)
                            .Select(row => row.PlayerID)
                            .Distinct()
                            .Count(),
                        TopAction = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:N0} drops across {1:N0} kills ({2:0.0}% drops per kill)",
                            drops,
                            kills,
                            rate),
                        Accuracy = successfulKills.ToString(CultureInfo.InvariantCulture) + " successful kills",
                        CriticalRate = lost.ToString(CultureInfo.InvariantCulture) + " lost"
                    });
                }
            }
            return rows;
        }

        private static bool ChatTypeMatches(string mode, ChatMessageType chatType)
        {
            if (mode == "shout")
                return chatType == ChatMessageType.Shout || chatType == ChatMessageType.Yell;
            return string.Equals(
                mode,
                chatType.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetChatTypeLabel(ChatMessageType chatType)
        {
            return chatType == ChatMessageType.Yell ? "Shout / Yell" : chatType.ToString();
        }

        private static string GetLootItemName(KPDatabaseDataSet.LootRow row)
        {
            return row.ItemsRow == null ? "Unknown item" : row.ItemsRow.ItemName;
        }

        private static string GetDisplayItemName(string itemName)
        {
            if (string.Equals(itemName, ":gil", StringComparison.OrdinalIgnoreCase))
                return "Gil";
            if (string.Equals(itemName, ":cruor", StringComparison.OrdinalIgnoreCase))
                return "Cruor";
            if (string.Equals(itemName, ":treasurechest", StringComparison.OrdinalIgnoreCase))
                return "Treasure chest";
            return itemName;
        }

        private static bool IsCurrencyItem(string itemName)
        {
            return string.Equals(itemName, ":gil", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(itemName, ":cruor", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCommonDrop(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return false;

            string normalized = itemName.Trim().ToLowerInvariant();
            return normalized.EndsWith(" crystal") || normalized.EndsWith(" cluster") ||
                   normalized.EndsWith(" seal") || normalized.EndsWith(" crest");
        }

        private static string GetLootRecipient(KPDatabaseDataSet.LootRow row)
        {
            return row.IsPlayerIDNull() ? "Unclaimed" : row.CombatantsRow.CombatantName;
        }

        private static string GetLootMobName(KPDatabaseDataSet.LootRow row)
        {
            return row.IsBattleIDNull() ? "Unknown source" : GetEnemyName(row.BattlesRow);
        }

        private static string NormalizeSearchText(string value)
        {
            string normalized = string.IsNullOrEmpty(value) ? string.Empty : value.Trim();
            return normalized.Length > 128 ? normalized.Substring(0, 128) : normalized;
        }

        private static bool ContainsIgnoreCase(string value, string searchText)
        {
            return string.IsNullOrEmpty(value) == false &&
                   value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<SanctumDotAggregate> GetIntegratedDotEstimates(
            IList<KPDatabaseDataSet.BattlesRow> battles,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            IDictionary<int, int> enemyIds,
            string combatantScope,
            string displayMode,
            bool parserRunning)
        {
            if (displayMode == "melee" || displayMode == "ranged" ||
                displayMode == "skillchains" || displayMode == "additional" ||
                displayMode == "reactive")
            {
                return new List<SanctumDotAggregate>();
            }

            KPDatabaseDataSet.InteractionsRow[] eventRows = events.ToArray();
            IEnumerable<SanctumDotAggregate> estimates = SanctumDotEstimator.Estimate(
                battles,
                eventRows,
                enemyIds,
                parserRunning)
                .Where(estimate =>
                    IsDamageActor(estimate.EntityType) &&
                    IsInCombatantScope(
                        estimate.Actor,
                        estimate.EntityType,
                        eventRows,
                        combatantScope));

            if (displayMode == "magic")
                estimates = estimates.Where(estimate => estimate.ActionType == ActionType.Spell);
            else if (displayMode == "weaponskills")
                estimates = estimates.Where(estimate => estimate.ActionType == ActionType.Weaponskill);
            else if (displayMode == "abilities")
                estimates = estimates.Where(estimate => estimate.ActionType == ActionType.Ability);

            return estimates.ToList();
        }

        private static List<SanctumCombatantSnapshot> BuildDamageOverTime(
            IList<KPDatabaseDataSet.BattlesRow> battles,
            KPDatabaseDataSet.InteractionsRow[] events,
            IDictionary<int, int> enemyIds,
            string combatantScope,
            string groupMode,
            bool parserRunning,
            double durationSeconds)
        {
            List<SanctumDotAggregate> estimates = SanctumDotEstimator.Estimate(
                battles,
                events,
                enemyIds,
                parserRunning)
                .Where(estimate =>
                    IsDamageActor(estimate.EntityType) &&
                    IsInCombatantScope(
                        estimate.Actor,
                        estimate.EntityType,
                        events,
                        combatantScope))
                .ToList();

            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            if (groupMode == "action")
            {
                foreach (SanctumDotAggregate estimate in estimates)
                {
                    SanctumCombatantSnapshot row = CreateActionCombatant(
                        estimate.Actor,
                        estimate.EntityType,
                        estimate.ActionName);
                    ApplyDotEstimateToRow(
                        row,
                        estimate.Damage,
                        estimate.ApplicationCount,
                        estimate.TickCount,
                        estimate.ActiveSeconds,
                        estimate.LowestPower,
                        estimate.HighestPower,
                        durationSeconds);
                    row.TopAction = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}Calculated {1}: {2:N0} damage from {3:N0} estimated tick{4}",
                        estimate.UsedCapturedStats ? "Captured stats active - " : string.Empty,
                        estimate.ActionName,
                        estimate.Damage,
                        estimate.TickCount,
                        estimate.TickCount == 1 ? string.Empty : "s");
                    result.Add(row);
                }
                return result;
            }

            foreach (var actorGroup in estimates.GroupBy(estimate => estimate.Actor.CombatantID))
            {
                SanctumDotAggregate first = actorGroup.First();
                long damage = actorGroup.Sum(estimate => estimate.Damage);
                long applications = actorGroup.Sum(estimate => estimate.ApplicationCount);
                long ticks = actorGroup.Sum(estimate => estimate.TickCount);
                double activeSeconds = actorGroup.Sum(estimate => estimate.ActiveSeconds);
                long lowPower = actorGroup.Min(estimate => estimate.LowestPower);
                long highPower = actorGroup.Max(estimate => estimate.HighestPower);
                SanctumDotAggregate top = actorGroup
                    .OrderByDescending(estimate => estimate.Damage)
                    .ThenBy(estimate => estimate.ActionName)
                    .First();

                SanctumCombatantSnapshot row = CreateCombatant(first.Actor, first.EntityType);
                ApplyDotEstimateToRow(
                    row,
                    damage,
                    applications,
                    ticks,
                    activeSeconds,
                    lowPower,
                    highPower,
                    durationSeconds);
                row.TopAction = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}Top calculated DoT: {1} - {2:N0}",
                    actorGroup.Any(estimate => estimate.UsedCapturedStats)
                        ? "Captured stats active - "
                        : string.Empty,
                    top.ActionName,
                    top.Damage);
                result.Add(row);
            }

            return result;
        }

        private static void ApplyDotEstimateToRow(
            SanctumCombatantSnapshot row,
            long damage,
            long applications,
            long ticks,
            double activeSeconds,
            long lowPower,
            long highPower,
            double durationSeconds)
        {
            long averagePower = ticks > 0 ? (long)Math.Round((double)damage / ticks) : 0;
            row.Damage = damage;
            row.Dps = damage / Math.Max(1.0, durationSeconds);
            row.Melee = applications;
            row.WeaponSkills = ticks;
            row.Magic = averagePower;
            row.Other = (long)Math.Round(activeSeconds);
            row.Detail1Text = applications.ToString("N0", CultureInfo.InvariantCulture);
            row.Detail2Text = ticks.ToString("N0", CultureInfo.InvariantCulture);
            row.Detail3Text = averagePower.ToString("N0", CultureInfo.InvariantCulture);
            row.Detail4Text = FormatReportDuration(activeSeconds);
            row.Accuracy = lowPower == highPower
                ? "Estimated potency/tick: " + lowPower.ToString("N0", CultureInfo.InvariantCulture)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Estimated potency/tick: {0:N0}-{1:N0}",
                    lowPower,
                    highPower);
            row.CriticalRate = "Calculated estimate; Stoneskin and hidden modifiers excluded";
        }

        private static List<SanctumCombatantSnapshot> BuildExperience(
            IList<KPDatabaseDataSet.BattlesRow> battles,
            string displayMode,
            bool parserRunning)
        {
            if (displayMode == "history")
            {
                List<SanctumCombatantSnapshot> history = new List<SanctumCombatantSnapshot>();
                int rank = 1;
                foreach (KPDatabaseDataSet.BattlesRow battle in battles.OrderBy(item => item.StartTime))
                {
                    double seconds = GetDurationSeconds(
                        battle,
                        battle.GetInteractionsRows(),
                        parserRunning);
                    long experience = battle.ExperiencePoints;
                    string difficulty = GetDifficultyLabel((MobDifficulty)battle.TargetDifficulty);
                    history.Add(new SanctumCombatantSnapshot
                    {
                        Key = "experience:battle:" + battle.BattleID.ToString(CultureInfo.InvariantCulture),
                        Rank = rank++,
                        Name = GetEnemyName(battle),
                        Job = (battle.Killed ? "Defeated" : "Incomplete") + " / " + difficulty,
                        CombatantType = "ExperienceFight",
                        Damage = experience,
                        Dps = experience * 3600.0 / Math.Max(1.0, seconds),
                        Melee = (long)Math.Round(seconds),
                        WeaponSkills = battle.ExperienceChain,
                        Magic = battle.GetInteractionsRows().Length,
                        Other = battle.StartTime.ToLocalTime().Ticks,
                        Detail1Text = FormatReportDuration(seconds),
                        Detail2Text = battle.ExperienceChain <= 0
                            ? "-"
                            : "#" + battle.ExperienceChain.ToString(CultureInfo.InvariantCulture),
                        Detail4Text = battle.StartTime.ToLocalTime().ToString("g"),
                        TopAction = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:N0} EXP in {1} ({2})",
                            experience,
                            FormatReportDuration(seconds),
                            difficulty),
                        Accuracy = "Battle ID: " + battle.BattleID.ToString(CultureInfo.InvariantCulture),
                        CriticalRate = battle.Killed ? "Defeated" : "Fight not marked complete"
                    });
                }
                return history;
            }

            if (displayMode == "chains")
            {
                return battles
                    .GroupBy(battle => battle.ExperienceChain)
                    .Select(group =>
                    {
                        KPDatabaseDataSet.BattlesRow[] grouped = group.ToArray();
                        long total = grouped.Sum(item => (long)item.ExperiencePoints);
                        double avgDuration = grouped.Average(item => GetDurationSeconds(
                            item,
                            item.GetInteractionsRows(),
                            parserRunning));
                        return new SanctumCombatantSnapshot
                        {
                            Key = "experience:chain:" + group.Key.ToString(CultureInfo.InvariantCulture),
                            Name = group.Key <= 0 ? "No chain" : "Chain #" + group.Key.ToString(CultureInfo.InvariantCulture),
                            Job = group.Key <= 0 ? "Unchained fights" : "Experience chain",
                            CombatantType = "ExperienceChain",
                            Damage = total,
                            Dps = grouped.Length == 0 ? 0.0 : grouped.Average(item => (double)item.ExperiencePoints),
                            Melee = grouped.Length,
                            WeaponSkills = grouped.Min(item => (long)item.ExperiencePoints),
                            Magic = grouped.Max(item => (long)item.ExperiencePoints),
                            Other = (long)Math.Round(avgDuration),
                            Detail4Text = FormatReportDuration(avgDuration),
                            TopAction = string.Format(
                                CultureInfo.InvariantCulture,
                                "{0:N0} fights produced {1:N0} EXP",
                                grouped.Length,
                                total),
                            Accuracy = "Average EXP: " + (grouped.Length == 0 ? 0.0 : grouped.Average(item => (double)item.ExperiencePoints)).ToString("N1", CultureInfo.InvariantCulture),
                            CriticalRate = "Average fight: " + FormatReportDuration(avgDuration)
                        };
                    })
                    .ToList();
            }

            IEnumerable<IGrouping<string, KPDatabaseDataSet.BattlesRow>> groups;
            if (displayMode == "difficulty")
            {
                groups = battles.GroupBy(battle =>
                    GetDifficultyLabel((MobDifficulty)battle.TargetDifficulty));
            }
            else
            {
                groups = battles.GroupBy(GetEnemyName, StringComparer.OrdinalIgnoreCase);
            }

            return groups.Select(group =>
            {
                KPDatabaseDataSet.BattlesRow[] grouped = group.ToArray();
                long total = grouped.Sum(item => (long)item.ExperiencePoints);
                double totalSeconds = grouped.Sum(item => GetDurationSeconds(
                    item,
                    item.GetInteractionsRows(),
                    parserRunning));
                double averageDuration = totalSeconds / Math.Max(1, grouped.Length);
                return new SanctumCombatantSnapshot
                {
                    Key = "experience:" + displayMode + ":" + group.Key,
                    Name = group.Key,
                    Job = displayMode == "difficulty" ? "Difficulty band" : "Enemy EXP summary",
                    CombatantType = "ExperienceSummary",
                    Damage = total,
                    Dps = total * 3600.0 / Math.Max(1.0, totalSeconds),
                    Melee = grouped.Length,
                    WeaponSkills = grouped.Length == 0 ? 0 :
                        (long)Math.Round(grouped.Average(item => (double)item.ExperiencePoints)),
                    Magic = grouped.Max(item => (long)item.ExperienceChain),
                    Other = (long)Math.Round(averageDuration),
                    Detail4Text = FormatReportDuration(averageDuration),
                    TopAction = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:N0} EXP from {1:N0} fight{2}",
                        total,
                        grouped.Length,
                        grouped.Length == 1 ? string.Empty : "s"),
                    Accuracy = "EXP/hour while fighting: " +
                        (total * 3600.0 / Math.Max(1.0, totalSeconds)).ToString("N0", CultureInfo.InvariantCulture),
                    CriticalRate = "Highest chain: " + grouped.Max(item => item.ExperienceChain).ToString(CultureInfo.InvariantCulture)
                };
            }).ToList();
        }

        private static string GetDifficultyLabel(MobDifficulty difficulty)
        {
            switch (difficulty)
            {
                case MobDifficulty.TooWeakToBeWorthwhile: return "Too weak";
                case MobDifficulty.EasyPrey: return "Easy prey";
                case MobDifficulty.DecentChallenge: return "Decent challenge";
                case MobDifficulty.EvenMatch: return "Even match";
                case MobDifficulty.Tough: return "Tough";
                case MobDifficulty.VeryTough: return "Very tough";
                case MobDifficulty.IncrediblyTough: return "Incredibly tough";
                case MobDifficulty.ImpossibleToGauge: return "Impossible to gauge";
                default: return "Unknown";
            }
        }

        private static List<SanctumCombatantSnapshot> BuildFightHistory(
            IEnumerable<KPDatabaseDataSet.BattlesRow> battles,
            string combatantScope,
            bool parserRunning)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            int rank = 1;

            foreach (KPDatabaseDataSet.BattlesRow battle in battles
                .OrderByDescending(item => item.StartTime)
                .ThenByDescending(item => item.BattleID))
            {
                KPDatabaseDataSet.InteractionsRow[] battleEvents = battle.GetInteractionsRows();
                double durationSeconds = GetDurationSeconds(battle, battleEvents, parserRunning);
                Dictionary<int, int> enemyIds = new Dictionary<int, int>();
                enemyIds[battle.BattleID] = battle.EnemyID;
                List<SanctumDotAggregate> dots = GetIntegratedDotEstimates(
                    new List<KPDatabaseDataSet.BattlesRow> { battle },
                    battleEvents,
                    enemyIds,
                    combatantScope,
                    "sources",
                    parserRunning);
                long totalDamage = BuildDamageDealt(
                        battleEvents,
                        enemyIds,
                        combatantScope,
                        "sources",
                        durationSeconds,
                        dots)
                    .Sum(row => row.Damage);
                string resultLabel = battle.IsOver
                    ? (battle.Killed ? "Killed" : "Ended")
                    : "Active";
                string killer = "Unknown";
                if (battle.IsKillerIDNull() == false &&
                    battle.CombatantsRowByBattleKillerRelation != null)
                {
                    killer = battle.CombatantsRowByBattleKillerRelation.CombatantName;
                }

                string started = AsUtc(battle.StartTime)
                    .ToLocalTime()
                    .ToString("g", CultureInfo.CurrentCulture);
                string ended = battle.IsOver && battle.IsEndTimeNull() == false
                    ? AsUtc(battle.EndTime).ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    : "In progress";

                result.Add(new SanctumCombatantSnapshot
                {
                    Key = "fight:" + battle.BattleID.ToString(CultureInfo.InvariantCulture),
                    Rank = rank++,
                    Name = GetEnemyName(battle),
                    Job = resultLabel,
                    CombatantType = "Fight",
                    Damage = totalDamage,
                    Dps = totalDamage / Math.Max(1.0, durationSeconds),
                    Melee = (long)Math.Round(durationSeconds),
                    WeaponSkills = battle.ExperiencePoints,
                    Magic = battle.ExperienceChain,
                    Other = battleEvents.Length,
                    Detail1Text = FormatReportDuration(durationSeconds),
                    Detail2Text = battle.ExperiencePoints > 0
                        ? battle.ExperiencePoints.ToString("N0", CultureInfo.InvariantCulture)
                        : "—",
                    Detail3Text = battle.ExperienceChain > 0
                        ? "#" + battle.ExperienceChain.ToString(CultureInfo.InvariantCulture)
                        : "—",
                    TopAction = "Started " + started + " · Ended " + ended +
                                (battle.Killed ? " · Killing blow: " + killer : string.Empty),
                    Accuracy = battle.Killed ? "Killing blow: " + killer : resultLabel,
                    CriticalRate = battle.ExperiencePoints > 0
                        ? "Experience: " + battle.ExperiencePoints.ToString("N0", CultureInfo.InvariantCulture)
                        : "Experience: —"
                });
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildPlayerPerformance(
            IList<KPDatabaseDataSet.BattlesRow> battles,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            IDictionary<int, int> enemyIds,
            string combatantScope,
            bool parserRunning,
            double durationSeconds)
        {
            List<SanctumDotAggregate> dots = GetIntegratedDotEstimates(
                battles,
                events,
                enemyIds,
                combatantScope,
                "sources",
                parserRunning);
            List<SanctumCombatantSnapshot> result = BuildDamageDealt(
                events,
                enemyIds,
                combatantScope,
                "sources",
                durationSeconds,
                dots);
            result.RemoveAll(row => string.Equals(
                row.CombatantType,
                EntityType.Skillchain.ToString(),
                StringComparison.OrdinalIgnoreCase));

            Dictionary<int, HashSet<int>> participation = new Dictionary<int, HashSet<int>>();
            foreach (KPDatabaseDataSet.InteractionsRow row in events)
            {
                if (row.IsActorIDNull() || row.IsBattleIDNull() || row.IsTargetIDNull() ||
                    enemyIds.ContainsKey(row.BattleID) == false ||
                    row.TargetID != enemyIds[row.BattleID] ||
                    GetOutgoingDamage(row) <= 0)
                {
                    continue;
                }

                HashSet<int> actorBattles;
                if (participation.TryGetValue(row.ActorID, out actorBattles) == false)
                {
                    actorBattles = new HashSet<int>();
                    participation[row.ActorID] = actorBattles;
                }
                actorBattles.Add(row.BattleID);
            }

            Dictionary<int, double> battleDurations = battles.ToDictionary(
                battle => battle.BattleID,
                battle => GetDurationSeconds(battle, battle.GetInteractionsRows(), parserRunning));
            for (int index = result.Count - 1; index >= 0; index--)
            {
                SanctumCombatantSnapshot row = result[index];
                int actorId;
                HashSet<int> actorBattles;
                if (int.TryParse(row.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out actorId) == false ||
                    participation.TryGetValue(actorId, out actorBattles) == false ||
                    actorBattles.Count == 0)
                {
                    result.RemoveAt(index);
                    continue;
                }

                double activeSeconds = actorBattles.Sum(battleId =>
                    battleDurations.ContainsKey(battleId) ? battleDurations[battleId] : 0.0);
                double participationPercent = battles.Count == 0
                    ? 0.0
                    : (double)actorBattles.Count * 100.0 / battles.Count;
                string topAction = row.TopAction;

                row.Dps = row.Damage / Math.Max(1.0, activeSeconds);
                row.Melee = actorBattles.Count;
                row.WeaponSkills = (long)Math.Round(participationPercent);
                row.Magic = (long)Math.Round(activeSeconds);
                row.Other = row.Damage / Math.Max(1, actorBattles.Count);
                row.Detail1Text = actorBattles.Count.ToString("N0", CultureInfo.InvariantCulture);
                row.Detail2Text = participationPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                row.Detail3Text = FormatReportDuration(activeSeconds);
                row.Detail4Text = row.Other.ToString("N0", CultureInfo.InvariantCulture);
                row.TopAction = string.Format(
                    CultureInfo.InvariantCulture,
                    "Active in {0:N0} of {1:N0} fights · {2}",
                    actorBattles.Count,
                    battles.Count,
                    topAction);
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildDamageDealt(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            IDictionary<int, int> enemyIds,
            string combatantScope,
            string displayMode,
            double durationSeconds,
            IList<SanctumDotAggregate> dotEstimates)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var actorGroups = events
                .Where(row => row.IsActorIDNull() == false &&
                              row.IsBattleIDNull() == false &&
                              row.IsTargetIDNull() == false &&
                              enemyIds.ContainsKey(row.BattleID) &&
                              row.TargetID == enemyIds[row.BattleID])
                .GroupBy(row => row.ActorID);

            foreach (var actorGroup in actorGroups)
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    actorGroup.First().CombatantsRowByActorCombatantRelation;
                if (actor == null)
                    continue;

                EntityType entityType = (EntityType)actor.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] actorEvents = actorGroup.ToArray();
                List<SanctumDotAggregate> actorDots = dotEstimates
                    .Where(dot => dot.Actor.CombatantID == actor.CombatantID)
                    .ToList();
                if (IsDamageActor(entityType) == false ||
                    IsInCombatantScope(actor, entityType, actorEvents, combatantScope) == false)
                {
                    continue;
                }

                long melee = SumPrimaryDamage(actorEvents, ActionType.Melee);
                long ranged = SumPrimaryDamage(actorEvents, ActionType.Ranged);
                long magicDots = actorDots
                    .Where(dot => dot.ActionType == ActionType.Spell)
                    .Sum(dot => dot.Damage);
                long weaponSkillDots = actorDots
                    .Where(dot => dot.ActionType == ActionType.Weaponskill)
                    .Sum(dot => dot.Damage);
                long abilityDots = actorDots
                    .Where(dot => dot.ActionType == ActionType.Ability)
                    .Sum(dot => dot.Damage);
                long uncategorizedDots = actorDots.Sum(dot => dot.Damage) -
                    magicDots - weaponSkillDots - abilityDots;
                long magic = SumPrimaryDamage(actorEvents, ActionType.Spell) + magicDots;
                long weaponSkills = SumPrimaryDamage(actorEvents, ActionType.Weaponskill) + weaponSkillDots;
                long abilities = SumPrimaryDamage(actorEvents, ActionType.Ability) + abilityDots;
                long skillchains = SumPrimaryDamage(actorEvents, ActionType.Skillchain);
                long counters = SumPrimaryDamage(actorEvents, ActionType.Counterattack);
                long retaliation = SumPrimaryDamage(actorEvents, ActionType.Retaliation);
                long spikes = SumPrimaryDamage(actorEvents, ActionType.Spikes);
                long additionalEffects = SumAdditionalEffectDamage(actorEvents);
                long other = ranged + abilities + skillchains + counters + retaliation +
                             spikes + additionalEffects + uncategorizedDots;
                long fullTotal = melee + weaponSkills + magic + other;
                bool categoryMode = IsDamageCategoryDisplay(displayMode);
                KPDatabaseDataSet.InteractionsRow[] categoryEvents = categoryMode
                    ? actorEvents
                        .Where(rowEvent => rowEvent.Preparing == false &&
                                           IsDamageCategoryEvent(rowEvent, displayMode))
                        .ToArray()
                    : new KPDatabaseDataSet.InteractionsRow[0];
                long[] categoryAmounts = categoryMode
                    ? categoryEvents
                        .Select(rowEvent => GetDamageCategoryAmount(rowEvent, displayMode))
                        .Where(amount => amount > 0)
                        .ToArray()
                    : new long[0];
                long categoryDotDamage = categoryMode
                    ? actorDots.Sum(dot => dot.Damage)
                    : 0;
                long reportTotal = categoryMode
                    ? categoryAmounts.Sum() + categoryDotDamage
                    : fullTotal;
                if (reportTotal <= 0)
                    continue;

                SanctumCombatantSnapshot row = CreateCombatant(actor, entityType);
                row.Damage = reportTotal;
                row.Dps = reportTotal / Math.Max(1.0, durationSeconds);
                row.TopAction = categoryMode
                    ? GetTopDamageCategoryAction(categoryEvents, displayMode)
                    : GetTopAction(actorEvents, entityType);
                row.Accuracy = categoryMode
                    ? GetDamageCategorySuccessRate(categoryEvents, displayMode)
                    : GetAccuracy(actorEvents);
                row.CriticalRate = categoryMode
                    ? GetDamageCategoryCriticalRate(categoryEvents, displayMode)
                    : GetCriticalRate(actorEvents);

                KPDatabaseDataSet.InteractionsRow[] attempts = actorEvents
                    .Where(IsPhysicalAttempt)
                    .ToArray();
                long hits = attempts.Count(rowEvent => IsEvaded(rowEvent) == false);
                long criticalHits = attempts.Count(rowEvent =>
                    (DamageModifier)rowEvent.DamageModifier == DamageModifier.Critical);

                row.MeleeDamage = melee;
                row.WeaponSkillDamage = weaponSkills;
                row.MagicDamage = magic;
                row.Ranged = ranged;
                row.Abilities = abilities;
                row.Skillchains = skillchains;
                row.AdditionalEffects = additionalEffects;
                row.Counters = counters;
                row.Retaliation = retaliation;
                row.Spikes = spikes;
                row.PhysicalAttempts = attempts.Length;
                row.PhysicalHits = hits;
                row.PhysicalMisses = attempts.Length - hits;
                row.CriticalHits = criticalHits;

                if (displayMode == "accuracy")
                {
                    row.Melee = attempts.Length;
                    row.WeaponSkills = hits;
                    row.Magic = attempts.Length - hits;
                    row.Other = criticalHits;
                }
                else if (categoryMode)
                {
                    long dotTicks = actorDots.Sum(dot => dot.TickCount);
                    long sampleCount = categoryAmounts.Length + dotTicks;
                    row.Melee = categoryEvents.Length;
                    row.WeaponSkills = categoryAmounts.Length + dotTicks;
                    row.Magic = sampleCount > 0
                        ? (long)Math.Round((double)reportTotal / sampleCount)
                        : 0;
                    row.Other = Math.Max(
                        categoryAmounts.Length > 0 ? categoryAmounts.Max() : 0,
                        actorDots.Count > 0 ? actorDots.Max(dot => dot.HighestPower) : 0);
                }
                else
                {
                    row.Melee = melee;
                    row.WeaponSkills = weaponSkills;
                    row.Magic = magic;
                    row.Other = other;
                }

                result.Add(row);
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildDamageDealtByAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            IDictionary<int, int> enemyIds,
            string combatantScope,
            string displayMode,
            IList<SanctumDotAggregate> dotEstimates)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            KPDatabaseDataSet.InteractionsRow[] eventRows = events.ToArray();
            var actorGroups = eventRows
                .Where(row => row.IsActorIDNull() == false &&
                              row.IsBattleIDNull() == false &&
                              row.IsTargetIDNull() == false &&
                              enemyIds.ContainsKey(row.BattleID) &&
                              row.TargetID == enemyIds[row.BattleID] &&
                              row.Preparing == false &&
                              IsDamageCategoryEvent(row, displayMode))
                .GroupBy(row => row.ActorID);

            foreach (var actorGroup in actorGroups)
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    actorGroup.First().CombatantsRowByActorCombatantRelation;
                if (actor == null)
                    continue;

                EntityType entityType = (EntityType)actor.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] actorEvents = actorGroup.ToArray();
                if (IsDamageActor(entityType) == false ||
                    IsInCombatantScope(actor, entityType, actorEvents, combatantScope) == false)
                {
                    continue;
                }

                foreach (var actionGroup in actorEvents.GroupBy(GetDetailedActionName))
                {
                    KPDatabaseDataSet.InteractionsRow[] actionEvents = actionGroup.ToArray();
                    long[] amounts = actionEvents
                        .Select(actionEvent => GetDamageCategoryAmount(actionEvent, displayMode))
                        .Where(amount => amount > 0)
                        .ToArray();
                    long total = amounts.Sum();
                    if (total <= 0)
                        continue;

                    SanctumCombatantSnapshot row = CreateActionCombatant(
                        actor,
                        entityType,
                        actionGroup.Key);
                    row.Damage = total;
                    row.Dps = amounts.Length == 0 ? 0.0 : amounts.Average();
                    row.Melee = actionEvents.Length;
                    row.WeaponSkills = amounts.Length;
                    row.Magic = amounts.Length == 0 ? 0 : amounts.Min();
                    row.Other = amounts.Length == 0 ? 0 : amounts.Max();
                    row.TopAction = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: {1:N0} total from {2:N0} damaging action{3}",
                        actionGroup.Key,
                        total,
                        amounts.Length,
                        amounts.Length == 1 ? string.Empty : "s");
                    row.Accuracy = GetDamageCategorySuccessRate(actionEvents, displayMode);
                    row.CriticalRate = GetDamageCategoryCriticalRate(actionEvents, displayMode);
                    result.Add(row);
                }
            }

            foreach (SanctumDotAggregate estimate in dotEstimates
                .OrderBy(item => item.Actor.CombatantName)
                .ThenBy(item => item.ActionName))
            {
                if (estimate.Actor == null ||
                    IsDamageActor(estimate.EntityType) == false ||
                    IsInCombatantScope(
                        estimate.Actor,
                        estimate.EntityType,
                        eventRows,
                        combatantScope) == false)
                {
                    continue;
                }

                SanctumCombatantSnapshot row = CreateActionCombatant(
                    estimate.Actor,
                    estimate.EntityType,
                    estimate.ActionName + " (DoT)");
                row.Damage = estimate.Damage;
                row.Dps = estimate.TickCount > 0
                    ? (double)estimate.Damage / estimate.TickCount
                    : 0.0;
                row.Melee = estimate.ApplicationCount;
                row.WeaponSkills = estimate.TickCount;
                row.Magic = estimate.LowestPower;
                row.Other = estimate.HighestPower;
                row.TopAction = string.Format(
                    CultureInfo.InvariantCulture,
                    "Calculated {0}: {1:N0} damage from {2:N0} estimated tick{3}",
                    estimate.ActionName,
                    estimate.Damage,
                    estimate.TickCount,
                    estimate.TickCount == 1 ? string.Empty : "s");
                row.Accuracy = "Calculated DoT estimate";
                row.CriticalRate = estimate.UsedCapturedStats
                    ? "Captured player stats applied"
                    : "Server base potency applied";
                result.Add(row);
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildDamageTaken(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            string displayMode,
            double durationSeconds)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var targetGroups = events
                .Where(row => row.IsTargetIDNull() == false)
                .GroupBy(row => row.TargetID);

            foreach (var targetGroup in targetGroups)
            {
                KPDatabaseDataSet.CombatantsRow target =
                    targetGroup.First().CombatantsRowByTargetCombatantRelation;
                if (target == null)
                    continue;

                EntityType entityType = (EntityType)target.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] targetEvents = targetGroup.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(target, entityType, events, combatantScope) == false)
                {
                    continue;
                }

                long melee = SumIncomingDamage(targetEvents, ActionType.Melee);
                long ranged = SumIncomingDamage(targetEvents, ActionType.Ranged);
                long magic = SumIncomingDamage(targetEvents, ActionType.Spell);
                long other = targetEvents.Sum(item => GetIncomingDamage(item)) - melee - ranged - magic;
                long fullTotal = melee + ranged + magic + other;
                bool categoryMode = IsIncomingDamageCategoryDisplay(displayMode);
                KPDatabaseDataSet.InteractionsRow[] categoryEvents = categoryMode
                    ? targetEvents
                        .Where(evt => evt.Preparing == false &&
                                      IsIncomingDamageCategoryEvent(evt, displayMode))
                        .ToArray()
                    : new KPDatabaseDataSet.InteractionsRow[0];
                long[] categoryAmounts = categoryMode
                    ? categoryEvents.Select(GetIncomingDamage).Where(amount => amount > 0).ToArray()
                    : new long[0];
                long reportTotal = categoryMode ? categoryAmounts.Sum() : fullTotal;
                if (reportTotal <= 0)
                    continue;

                SanctumCombatantSnapshot row = CreateCombatant(target, entityType);
                row.Damage = reportTotal;
                row.Dps = reportTotal / Math.Max(1.0, durationSeconds);
                row.TopAction = GetTopIncomingAction(categoryMode ? categoryEvents : targetEvents);
                row.Accuracy = "Hits taken: " + (categoryMode ? categoryAmounts.Length : targetEvents.Count(evt => GetIncomingDamage(evt) > 0))
                    .ToString("N0", CultureInfo.InvariantCulture);
                row.CriticalRate = GetIncomingCriticalRate(categoryMode ? categoryEvents : targetEvents);

                if (displayMode == "defense")
                {
                    row.Melee = targetEvents.Count(IsEvaded);
                    row.WeaponSkills = targetEvents.Count(evt =>
                        (DefenseType)evt.DefenseType == DefenseType.Shadow);
                    row.Magic = targetEvents.Count(evt =>
                        (DefenseType)evt.DefenseType == DefenseType.Parry ||
                        (DefenseType)evt.DefenseType == DefenseType.Guard ||
                        (DefenseType)evt.DefenseType == DefenseType.Block);
                    row.Other = targetEvents.Count(evt =>
                        (DefenseType)evt.DefenseType == DefenseType.Resist ||
                        (DefenseType)evt.DefenseType == DefenseType.Absorb);
                }
                else if (categoryMode)
                {
                    row.Melee = categoryEvents.Length;
                    row.WeaponSkills = categoryAmounts.Length;
                    row.Magic = categoryAmounts.Length > 0
                        ? (long)categoryAmounts.Average()
                        : 0;
                    row.Other = categoryAmounts.Length > 0
                        ? categoryAmounts.Max()
                        : 0;
                }
                else
                {
                    row.Melee = melee;
                    row.WeaponSkills = ranged;
                    row.Magic = magic;
                    row.Other = other;
                }

                result.Add(row);
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildDamageTakenByAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            string displayMode)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var targetGroups = events
                .Where(row => row.IsTargetIDNull() == false &&
                              row.Preparing == false &&
                              IsIncomingDamageCategoryEvent(row, displayMode))
                .GroupBy(row => row.TargetID);

            foreach (var targetGroup in targetGroups)
            {
                KPDatabaseDataSet.CombatantsRow target =
                    targetGroup.First().CombatantsRowByTargetCombatantRelation;
                if (target == null)
                    continue;

                EntityType entityType = (EntityType)target.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] targetEvents = targetGroup.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(target, entityType, events, combatantScope) == false)
                {
                    continue;
                }

                var actionGroups = targetEvents.GroupBy(row => new
                {
                    Source = row.IsActorIDNull()
                        ? "Unknown"
                        : row.CombatantsRowByActorCombatantRelation.CombatantName,
                    Action = GetDetailedActionName(row)
                });

                foreach (var actionGroup in actionGroups)
                {
                    KPDatabaseDataSet.InteractionsRow[] actionEvents = actionGroup.ToArray();
                    long[] amounts = actionEvents
                        .Select(GetIncomingDamage)
                        .Where(amount => amount > 0)
                        .ToArray();
                    long total = amounts.Sum();
                    if (total <= 0)
                        continue;

                    string detail = actionGroup.Key.Source + " / " + actionGroup.Key.Action;
                    SanctumCombatantSnapshot row = CreateActionCombatant(
                        target,
                        entityType,
                        detail);
                    row.Damage = total;
                    row.Dps = amounts.Length == 0 ? 0.0 : amounts.Average();
                    row.Melee = actionEvents.Length;
                    row.WeaponSkills = amounts.Length;
                    row.Magic = amounts.Length == 0 ? 0 : amounts.Min();
                    row.Other = amounts.Length == 0 ? 0 : amounts.Max();
                    row.TopAction = "Incoming source: " + detail;
                    row.Accuracy = string.Format(
                        CultureInfo.InvariantCulture,
                        "Hit rate: {0:0.0}%",
                        actionEvents.Length == 0
                            ? 0.0
                            : (double)amounts.Length * 100.0 / actionEvents.Length);
                    row.CriticalRate = GetIncomingCriticalRate(actionEvents);
                    result.Add(row);
                }
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildMultiAttacks(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            IDictionary<int, int> enemyIds,
            string combatantScope)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var actorGroups = events
                .Where(row => row.IsActorIDNull() == false &&
                              row.IsBattleIDNull() == false &&
                              row.IsTargetIDNull() == false &&
                              enemyIds.ContainsKey(row.BattleID) &&
                              row.TargetID == enemyIds[row.BattleID] &&
                              (ActionType)row.ActionType == ActionType.Melee)
                .GroupBy(row => row.ActorID);

            foreach (var actorGroup in actorGroups)
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    actorGroup.First().CombatantsRowByActorCombatantRelation;
                if (actor == null)
                    continue;
                EntityType entityType = (EntityType)actor.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] actorEvents = actorGroup.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(actor, entityType, actorEvents, combatantScope) == false)
                {
                    continue;
                }

                var rounds = actorEvents
                    .OrderBy(row => row.Timestamp)
                    .GroupBy(row => row.Timestamp)
                    .Select(group => group.Count())
                    .ToArray();
                if (rounds.Length == 0)
                    continue;

                int one = rounds.Count(count => count == 1);
                int two = rounds.Count(count => count == 2);
                int three = rounds.Count(count => count == 3);
                int fourPlus = rounds.Count(count => count >= 4);
                int multi = two + three + fourPlus;
                double multiRate = (double)multi * 100.0 / rounds.Length;
                double average = rounds.Average();

                SanctumCombatantSnapshot reportRow = CreateCombatant(actor, entityType);
                reportRow.Damage = rounds.Length;
                reportRow.Dps = multiRate;
                reportRow.Melee = one;
                reportRow.WeaponSkills = two;
                reportRow.Magic = three;
                reportRow.Other = fourPlus;
                reportRow.RateText = multiRate.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                reportRow.TopAction = string.Format(
                    CultureInfo.InvariantCulture,
                    "Inferred from timestamp-grouped melee events: {0:0.00} attacks per round",
                    average);
                reportRow.Accuracy = "Observed melee events: " + actorEvents.Length.ToString("N0", CultureInfo.InvariantCulture);
                reportRow.CriticalRate = "Round inference; pet/retaliation events remain separate";
                result.Add(reportRow);
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildHealingRecipients(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            double durationSeconds)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var groups = events
                .Where(row => IsHpRecovery(row) && row.IsTargetIDNull() == false)
                .GroupBy(row => row.TargetID);
            foreach (var group in groups)
            {
                KPDatabaseDataSet.CombatantsRow target =
                    group.First().CombatantsRowByTargetCombatantRelation;
                if (target == null)
                    continue;
                EntityType entityType = (EntityType)target.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] cures = group.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(target, entityType, events, combatantScope) == false)
                {
                    continue;
                }

                long spell = cures.Where(item => (ActionType)item.ActionType == ActionType.Spell)
                    .Sum(item => (long)item.Amount);
                long ability = cures.Where(item => (ActionType)item.ActionType == ActionType.Ability)
                    .Sum(item => (long)item.Amount);
                long total = cures.Sum(item => (long)item.Amount);
                SanctumCombatantSnapshot row = CreateCombatant(target, entityType);
                row.Damage = total;
                row.Dps = total / Math.Max(1.0, durationSeconds);
                row.Melee = cures.Length;
                row.WeaponSkills = spell;
                row.Magic = ability;
                row.Other = cures.Max(item => (long)item.Amount);
                row.TopAction = GetTopHealingAction(cures, new KPDatabaseDataSet.InteractionsRow[0]);
                row.Accuracy = "Healers: " + cures
                    .Where(item => item.IsActorIDNull() == false)
                    .Select(item => item.ActorID)
                    .Distinct()
                    .Count()
                    .ToString("N0", CultureInfo.InvariantCulture);
                row.CriticalRate = "Observed recovery only; overheal is not available in the log";
                result.Add(row);
            }
            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildResourceRecovery(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            double durationSeconds)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var groups = events
                .Where(row => row.IsActorIDNull() == false && row.Preparing == false &&
                              ((AidType)row.AidType == AidType.Recovery ||
                               (AidType)row.SecondAidType == AidType.Recovery))
                .GroupBy(row => row.ActorID);
            foreach (var group in groups)
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    group.First().CombatantsRowByActorCombatantRelation;
                if (actor == null)
                    continue;
                EntityType entityType = (EntityType)actor.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] recovery = group.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(actor, entityType, recovery, combatantScope) == false)
                {
                    continue;
                }

                long hp = SumRecovery(recovery, RecoveryType.RecoverHP);
                long mp = SumRecovery(recovery, RecoveryType.RecoverMP);
                long tp = SumRecovery(recovery, RecoveryType.RecoverTP);
                long total = hp + mp + tp;
                if (total <= 0)
                    continue;
                SanctumCombatantSnapshot row = CreateCombatant(actor, entityType);
                row.Damage = total;
                row.Dps = total / Math.Max(1.0, durationSeconds);
                row.Melee = hp;
                row.WeaponSkills = mp;
                row.Magic = tp;
                row.Other = recovery.Length;
                row.TopAction = GetTopCountedAction(recovery, "Most-used recovery action");
                row.Accuracy = "HP, MP and TP are combined only in the primary total";
                row.CriticalRate = "Use the detail columns for each resource";
                result.Add(row);
            }
            return result;
        }

        private static long SumRecovery(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            RecoveryType recoveryType)
        {
            return events.Sum(row =>
                ((AidType)row.AidType == AidType.Recovery &&
                 (RecoveryType)row.RecoveryType == recoveryType ? (long)row.Amount : 0L) +
                ((AidType)row.SecondAidType == AidType.Recovery &&
                 (RecoveryType)row.SecondRecoveryType == recoveryType ? (long)row.SecondAmount : 0L));
        }

        private static List<SanctumCombatantSnapshot> BuildHealingEfficiency(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            double durationSeconds)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            foreach (var group in events
                .Where(row => row.IsActorIDNull() == false && IsHpRecovery(row))
                .GroupBy(row => row.ActorID))
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    group.First().CombatantsRowByActorCombatantRelation;
                if (actor == null)
                    continue;
                EntityType entityType = (EntityType)actor.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] cures = group.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(actor, entityType, cures, combatantScope) == false)
                {
                    continue;
                }
                long total = cures.Sum(item => (long)item.Amount);
                SanctumCombatantSnapshot row = CreateCombatant(actor, entityType);
                row.Damage = total;
                row.Dps = cures.Average(item => (double)item.Amount);
                row.Melee = cures.Length;
                row.WeaponSkills = cures.Where(item => item.IsTargetIDNull() == false)
                    .Select(item => item.TargetID).Distinct().Count();
                row.Magic = cures.Min(item => (long)item.Amount);
                row.Other = cures.Max(item => (long)item.Amount);
                row.TopAction = GetTopHealingAction(cures, new KPDatabaseDataSet.InteractionsRow[0]);
                row.Accuracy = "Observed HPS: " +
                    (total / Math.Max(1.0, durationSeconds)).ToString("N1", CultureInfo.InvariantCulture);
                row.CriticalRate = "Overheal and MP cost are not exposed by combat messages";
                result.Add(row);
            }
            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildHealing(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            string displayMode,
            int fightCount,
            double durationSeconds)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var actorGroups = events
                .Where(row => row.IsActorIDNull() == false && row.Preparing == false)
                .GroupBy(row => row.ActorID);

            foreach (var actorGroup in actorGroups)
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    actorGroup.First().CombatantsRowByActorCombatantRelation;
                if (actor == null)
                    continue;

                EntityType entityType = (EntityType)actor.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] actorEvents = actorGroup.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(actor, entityType, actorEvents, combatantScope) == false)
                {
                    continue;
                }

                KPDatabaseDataSet.InteractionsRow[] allCures = actorEvents
                    .Where(IsHpRecovery)
                    .ToArray();
                KPDatabaseDataSet.InteractionsRow[] statusCures = actorEvents
                    .Where(IsStatusRecovery)
                    .ToArray();
                bool categoryMode = displayMode == "spells" || displayMode == "abilities";
                KPDatabaseDataSet.InteractionsRow[] cures = categoryMode
                    ? allCures.Where(row => (ActionType)row.ActionType ==
                        (displayMode == "spells" ? ActionType.Spell : ActionType.Ability)).ToArray()
                    : allCures;
                long spellHealing = cures
                    .Where(row => (ActionType)row.ActionType == ActionType.Spell)
                    .Sum(row => (long)row.Amount);
                long abilityHealing = cures
                    .Where(row => (ActionType)row.ActionType == ActionType.Ability)
                    .Sum(row => (long)row.Amount);
                long otherHealing = cures.Sum(row => (long)row.Amount) - spellHealing - abilityHealing;
                long totalHealing = spellHealing + abilityHealing + otherHealing;
                long fullHealing = allCures.Sum(row => (long)row.Amount);

                if ((categoryMode && totalHealing <= 0) ||
                    (categoryMode == false && totalHealing <= 0 && statusCures.Length == 0))
                    continue;

                SanctumCombatantSnapshot combatantRow = CreateCombatant(actor, entityType);
                combatantRow.TopAction = GetTopHealingAction(
                    cures,
                    categoryMode ? new KPDatabaseDataSet.InteractionsRow[0] : statusCures);

                if (displayMode == "status")
                {
                    combatantRow.Damage = statusCures.Length;
                    combatantRow.Dps = (double)statusCures.Length / Math.Max(1, fightCount);
                    combatantRow.Melee = statusCures.Count(evt => (ActionType)evt.ActionType == ActionType.Spell);
                    combatantRow.WeaponSkills = statusCures.Count(evt => (ActionType)evt.ActionType == ActionType.Ability);
                    combatantRow.Magic = statusCures.Select(GetActionName).Distinct().Count();
                    combatantRow.Other = statusCures
                        .Where(evt => evt.IsTargetIDNull() == false)
                        .Select(evt => evt.TargetID)
                        .Distinct()
                        .Count();
                    combatantRow.Accuracy = "HP restored: " + fullHealing.ToString("N0", CultureInfo.InvariantCulture);
                    combatantRow.CriticalRate = "Status actions: " + statusCures.Length.ToString("N0", CultureInfo.InvariantCulture);
                }
                else if (categoryMode)
                {
                    combatantRow.Damage = totalHealing;
                    combatantRow.Dps = totalHealing / Math.Max(1.0, durationSeconds);
                    combatantRow.Melee = cures.Length;
                    combatantRow.WeaponSkills = cures
                        .Where(evt => evt.IsTargetIDNull() == false)
                        .Select(evt => evt.TargetID)
                        .Distinct()
                        .Count();
                    combatantRow.Magic = cures.Length == 0 ? 0 : (long)cures.Average(evt => evt.Amount);
                    combatantRow.Other = cures.Length == 0 ? 0 : cures.Max(evt => (long)evt.Amount);
                    combatantRow.Accuracy = "Healing actions: " + cures.Length.ToString("N0", CultureInfo.InvariantCulture);
                    combatantRow.CriticalRate = "Average heal: " + combatantRow.Magic.ToString("N0", CultureInfo.InvariantCulture);
                }
                else
                {
                    combatantRow.Damage = totalHealing;
                    combatantRow.Dps = totalHealing / Math.Max(1.0, durationSeconds);
                    combatantRow.Melee = spellHealing;
                    combatantRow.WeaponSkills = abilityHealing + otherHealing;
                    combatantRow.Magic = displayMode == "cures"
                        ? (cures.Length == 0 ? 0 : (long)cures.Average(evt => evt.Amount))
                        : cures.Length;
                    combatantRow.Other = displayMode == "cures"
                        ? (cures.Length == 0 ? 0 : cures.Max(evt => (long)evt.Amount))
                        : statusCures.Length;
                    combatantRow.Accuracy = "Cure actions: " + cures.Length.ToString("N0", CultureInfo.InvariantCulture);
                    combatantRow.CriticalRate = "Average cure: " +
                        (cures.Length == 0 ? 0 : cures.Average(evt => evt.Amount))
                        .ToString("N0", CultureInfo.InvariantCulture);
                }

                if (combatantRow.Damage > 0 || statusCures.Length > 0)
                    result.Add(combatantRow);
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildHealingByAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            string displayMode,
            int fightCount)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var actorGroups = events
                .Where(row => row.IsActorIDNull() == false && row.Preparing == false)
                .GroupBy(row => row.ActorID);

            foreach (var actorGroup in actorGroups)
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    actorGroup.First().CombatantsRowByActorCombatantRelation;
                if (actor == null)
                    continue;

                EntityType entityType = (EntityType)actor.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] actorEvents = actorGroup.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(actor, entityType, actorEvents, combatantScope) == false)
                {
                    continue;
                }

                IEnumerable<KPDatabaseDataSet.InteractionsRow> selected = displayMode == "status"
                    ? actorEvents.Where(IsStatusRecovery)
                    : actorEvents.Where(IsHpRecovery);
                if (displayMode == "spells")
                    selected = selected.Where(row => (ActionType)row.ActionType == ActionType.Spell);
                else if (displayMode == "abilities")
                    selected = selected.Where(row => (ActionType)row.ActionType == ActionType.Ability);

                foreach (var actionGroup in selected.GroupBy(GetActionName))
                {
                    KPDatabaseDataSet.InteractionsRow[] actionEvents = actionGroup.ToArray();
                    SanctumCombatantSnapshot row = CreateActionCombatant(
                        actor,
                        entityType,
                        actionGroup.Key);

                    if (displayMode == "status")
                    {
                        row.Damage = actionEvents.Length;
                        row.Dps = (double)actionEvents.Length / Math.Max(1, fightCount);
                        row.Melee = actionEvents
                            .Where(evt => evt.IsTargetIDNull() == false)
                            .Select(evt => evt.TargetID)
                            .Distinct()
                            .Count();
                        row.WeaponSkills = actionEvents
                            .Where(evt => evt.IsBattleIDNull() == false)
                            .Select(evt => evt.BattleID)
                            .Distinct()
                            .Count();
                        row.Magic = actionEvents.Count(evt => (ActionType)evt.ActionType == ActionType.Spell);
                        row.Other = actionEvents.Count(evt => (ActionType)evt.ActionType == ActionType.Ability);
                        row.Accuracy = "Status removals: " + actionEvents.Length.ToString("N0", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        long[] amounts = actionEvents.Select(evt => (long)evt.Amount).Where(amount => amount > 0).ToArray();
                        row.Damage = amounts.Sum();
                        row.Dps = amounts.Length == 0 ? 0.0 : amounts.Average();
                        row.Melee = actionEvents.Length;
                        row.WeaponSkills = actionEvents
                            .Where(evt => evt.IsTargetIDNull() == false)
                            .Select(evt => evt.TargetID)
                            .Distinct()
                            .Count();
                        row.Magic = amounts.Length == 0 ? 0 : amounts.Min();
                        row.Other = amounts.Length == 0 ? 0 : amounts.Max();
                        row.Accuracy = "Average heal: " + row.Dps.ToString("N1", CultureInfo.InvariantCulture);
                    }

                    row.TopAction = actionGroup.Key + " used " + actionEvents.Length.ToString("N0", CultureInfo.InvariantCulture) + " times";
                    row.CriticalRate = "Targets: " +
                        (displayMode == "status" ? row.Melee : row.WeaponSkills)
                        .ToString("N0", CultureInfo.InvariantCulture);
                    if (row.Damage > 0)
                        result.Add(row);
                }
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildBuffPerformance(
            KPDatabaseDataSet dataSet,
            IList<KPDatabaseDataSet.BattlesRow> battles,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            IDictionary<int, int> enemyIds,
            string combatantScope,
            bool parserRunning,
            double durationSeconds,
            bool defensive)
        {
            KPDatabaseDataSet.InteractionsRow[] eventRows = events.ToArray();
            List<KPDatabaseDataSet.CombatantsRow> players = dataSet.Combatants
                .Where(combatant =>
                    (EntityType)combatant.CombatantType == EntityType.Player &&
                    eventRows.Any(row =>
                        (row.IsActorIDNull() == false && row.ActorID == combatant.CombatantID) ||
                        (row.IsTargetIDNull() == false && row.TargetID == combatant.CombatantID)) &&
                    IsInCombatantScope(combatant, EntityType.Player, eventRows, combatantScope))
                .ToList();
            if (players.Count == 0 || battles.Count == 0)
                return new List<SanctumCombatantSnapshot>();

            List<PlayerTimeIntervalSets> playerIntervals = CollectTimeIntervals.GetTimeIntervals(
                dataSet,
                players.Select(player => player.CombatantName).ToList());
            List<TimeInterval> fightWindows = battles.Select(battle =>
            {
                DateTime start = AsUtc(battle.StartTime);
                return new TimeInterval(
                    start,
                    start.AddSeconds(GetDurationSeconds(
                        battle,
                        battle.GetInteractionsRows(),
                        parserRunning)));
            }).ToList();
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();

            foreach (PlayerTimeIntervalSets playerSet in playerIntervals)
            {
                KPDatabaseDataSet.CombatantsRow player = players.FirstOrDefault(item =>
                    string.Equals(item.CombatantName, playerSet.PlayerName,
                        StringComparison.OrdinalIgnoreCase));
                if (player == null)
                    continue;

                foreach (TimeIntervalSet buffSet in playerSet.TimeIntervalSets)
                {
                    if (defensive && CollectTimeIntervals.TrackedDefenseBuffNames.Any(name =>
                        string.Equals(name, buffSet.SetName, StringComparison.OrdinalIgnoreCase)) == false)
                    {
                        continue;
                    }

                    List<TimeInterval> clipped = ClipAndMergeIntervals(
                        buffSet.TimeIntervals,
                        fightWindows);
                    double activeSeconds = clipped.Sum(interval => interval.Duration.TotalSeconds);
                    if (activeSeconds <= 0.0)
                        continue;

                    KPDatabaseDataSet.InteractionsRow[] activeEvents = eventRows
                        .Where(row => IsWithinIntervals(row.Timestamp, clipped))
                        .Where(row => defensive
                            ? row.IsTargetIDNull() == false &&
                              row.TargetID == player.CombatantID &&
                              row.IsBattleIDNull() == false &&
                              row.IsActorIDNull() == false &&
                              enemyIds.ContainsKey(row.BattleID) &&
                              row.ActorID == enemyIds[row.BattleID]
                            : row.IsActorIDNull() == false &&
                              row.ActorID == player.CombatantID &&
                              row.IsBattleIDNull() == false &&
                              row.IsTargetIDNull() == false &&
                              enemyIds.ContainsKey(row.BattleID) &&
                              row.TargetID == enemyIds[row.BattleID])
                        .ToArray();
                    long damage = activeEvents.Sum(row => GetIncomingDamage(row));
                    KPDatabaseDataSet.InteractionsRow[] physical = activeEvents
                        .Where(IsPhysicalAttempt)
                        .ToArray();
                    int hits = physical.Count(row => IsEvaded(row) == false);
                    int criticals = physical.Count(row =>
                        IsEvaded(row) == false &&
                        (DamageModifier)row.DamageModifier == DamageModifier.Critical);
                    if (damage <= 0 && physical.Length == 0)
                        continue;

                    SanctumCombatantSnapshot reportRow = CreateActionCombatant(
                        player,
                        EntityType.Player,
                        buffSet.SetName);
                    reportRow.Damage = damage;
                    reportRow.Dps = damage / Math.Max(1.0, activeSeconds);
                    reportRow.Melee = physical.Length;
                    reportRow.WeaponSkills = hits;
                    if (defensive)
                    {
                        int avoided = Math.Max(0, physical.Length - hits);
                        reportRow.Magic = avoided;
                        reportRow.Other = hits == 0 ? 0 : damage / hits;
                        reportRow.Detail3Text = physical.Length == 0
                            ? "-"
                            : ((double)avoided * 100.0 / physical.Length)
                                .ToString("0.0", CultureInfo.InvariantCulture) + "%";
                    }
                    else
                    {
                        reportRow.Magic = physical.Length == 0
                            ? 0
                            : (long)Math.Round((double)hits * 100.0 / physical.Length);
                        reportRow.Other = hits == 0
                            ? 0
                            : (long)Math.Round((double)criticals * 100.0 / hits);
                        reportRow.Detail3Text = physical.Length == 0
                            ? "-"
                            : ((double)hits * 100.0 / physical.Length)
                                .ToString("0.0", CultureInfo.InvariantCulture) + "%";
                        reportRow.Detail4Text = hits == 0
                            ? "-"
                            : ((double)criticals * 100.0 / hits)
                                .ToString("0.0", CultureInfo.InvariantCulture) + "%";
                    }
                    reportRow.TopAction = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} measured during {1} of selected fight time",
                        defensive ? "Incoming performance" : "Outgoing performance",
                        FormatReportDuration(activeSeconds));
                    reportRow.Accuracy = defensive
                        ? "Observed avoidance includes evasion, shadows, parry and similar defenses"
                        : "Accuracy and critical rate use melee/ranged attempts while the buff was active";
                    reportRow.CriticalRate = "Correlation view; it does not claim the buff caused every difference";
                    result.Add(reportRow);
                }
            }
            return result;
        }

        private static bool IsWithinIntervals(DateTime timestamp, IEnumerable<TimeInterval> intervals)
        {
            DateTime normalized = AsUtc(timestamp);
            return intervals.Any(interval =>
                normalized >= interval.StartTime && normalized <= interval.EndTime);
        }

        private sealed class SanctumRollSequence
        {
            internal SanctumRollSequence(string playerName, string rollName)
            {
                PlayerName = playerName;
                RollName = rollName;
            }

            internal string PlayerName { get; private set; }
            internal string RollName { get; private set; }
            internal KPDatabaseDataSet.CombatantsRow Player { get; set; }
            internal int InitialRolls { get; set; }
            internal int DoubleUps { get; set; }
            internal int FinalValue { get; set; }
            internal bool Bust { get; set; }
        }

        private static List<SanctumCombatantSnapshot> BuildCorsairRolls(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope)
        {
            List<SanctumRollSequence> sequences = new List<SanctumRollSequence>();
            foreach (var actorGroup in events
                .Where(row => row.IsActorIDNull() == false && row.Preparing == false)
                .GroupBy(row => row.ActorID))
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    actorGroup.First().CombatantsRowByActorCombatantRelation;
                if (actor == null || (EntityType)actor.CombatantType != EntityType.Player ||
                    IsInCombatantScope(actor, EntityType.Player, actorGroup, combatantScope) == false)
                {
                    continue;
                }

                KPDatabaseDataSet.InteractionsRow[] rollEvents = actorGroup
                    .Where(IsCorsairRollEvent)
                    .GroupBy(row => new
                    {
                        row.Timestamp,
                        Name = GetActionName(row),
                        row.Amount
                    })
                    .Select(group => group.First())
                    .OrderBy(row => row.Timestamp)
                    .ToArray();
                SanctumRollSequence current = null;
                foreach (KPDatabaseDataSet.InteractionsRow rollEvent in rollEvents)
                {
                    string actionName = GetActionName(rollEvent);
                    bool followup = string.Equals(actionName, "Double-Up", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(actionName, "Bust", StringComparison.OrdinalIgnoreCase);
                    if (followup == false)
                    {
                        if (current != null)
                            sequences.Add(current);
                        current = new SanctumRollSequence(actor.CombatantName, actionName);
                        current.Player = actor;
                        current.InitialRolls = 1;
                    }
                    else if (current == null)
                    {
                        string secondary = GetSecondaryActionName(rollEvent);
                        current = new SanctumRollSequence(
                            actor.CombatantName,
                            string.IsNullOrEmpty(secondary) ? "Unknown roll" : secondary);
                        current.Player = actor;
                    }

                    if (current == null)
                        continue;
                    if (string.Equals(actionName, "Double-Up", StringComparison.OrdinalIgnoreCase))
                        current.DoubleUps++;
                    current.Bust = string.Equals(actionName, "Bust", StringComparison.OrdinalIgnoreCase) ||
                                   rollEvent.Amount <= 0;
                    current.FinalValue = current.Bust ? 0 : rollEvent.Amount;
                }
                if (current != null)
                    sequences.Add(current);
            }

            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            foreach (var group in sequences.GroupBy(item => item.PlayerName + "\u001f" + item.RollName))
            {
                SanctumRollSequence first = group.First();
                SanctumRollSequence[] rolls = group.ToArray();
                SanctumCombatantSnapshot row = CreateActionCombatant(
                    first.Player,
                    EntityType.Player,
                    first.RollName);
                row.Damage = rolls.Length;
                row.Dps = rolls.Length == 0 ? 0.0 : rolls.Average(item => (double)item.FinalValue);
                row.Melee = rolls.Sum(item => item.InitialRolls);
                row.WeaponSkills = rolls.Sum(item => item.DoubleUps);
                row.Magic = rolls.Count(item => item.Bust);
                row.Other = rolls.Count(item => item.FinalValue == 11);
                row.TopAction = string.Format(
                    CultureInfo.InvariantCulture,
                    "Final values: {0}; average {1:0.00}",
                    string.Join(", ", rolls.GroupBy(item => item.FinalValue)
                        .OrderBy(item => item.Key)
                        .Select(item => item.Key + " x" + item.Count()).ToArray()),
                    row.Dps);
                row.Accuracy = "Double-Ups: " + row.WeaponSkills.ToString("N0", CultureInfo.InvariantCulture);
                row.CriticalRate = "Roll targets are deduplicated by timestamp";
                result.Add(row);
            }
            return result;
        }

        private static bool IsCorsairRollEvent(KPDatabaseDataSet.InteractionsRow row)
        {
            string name = GetActionName(row);
            return name.EndsWith(" Roll", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "Double-Up", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "Bust", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSecondaryActionName(KPDatabaseDataSet.InteractionsRow row)
        {
            return row.IsSecondActionIDNull() == false &&
                   row.ActionsRowBySecondaryActionNameRelation != null
                ? row.ActionsRowBySecondaryActionNameRelation.ActionName
                : string.Empty;
        }

        private static List<SanctumCombatantSnapshot> BuildBuffs(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            string displayMode,
            int fightCount)
        {
            bool received = displayMode == "received";
            IEnumerable<IGrouping<int, KPDatabaseDataSet.InteractionsRow>> groups = events
                .Where(IsBuffApplication)
                .Where(row => received ? row.IsTargetIDNull() == false : row.IsActorIDNull() == false)
                .GroupBy(row => received ? row.TargetID : row.ActorID);
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();

            foreach (var group in groups)
            {
                KPDatabaseDataSet.InteractionsRow first = group.First();
                KPDatabaseDataSet.CombatantsRow combatant = received
                    ? first.CombatantsRowByTargetCombatantRelation
                    : first.CombatantsRowByActorCombatantRelation;
                if (combatant == null)
                    continue;

                EntityType entityType = (EntityType)combatant.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] buffEvents = group.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(combatant, entityType, events, combatantScope) == false)
                {
                    continue;
                }

                SanctumCombatantSnapshot row = CreateCombatant(combatant, entityType);
                row.Damage = buffEvents.Length;
                row.Dps = (double)buffEvents.Length / Math.Max(1, fightCount);
                row.Melee = buffEvents.Count(evt => (ActionType)evt.ActionType == ActionType.Spell);
                row.WeaponSkills = buffEvents.Count(evt => (ActionType)evt.ActionType == ActionType.Ability);
                row.Magic = buffEvents.Count(evt => (ActionType)evt.ActionType != ActionType.Spell &&
                                                    (ActionType)evt.ActionType != ActionType.Ability);
                row.Other = buffEvents.Select(GetActionName).Distinct().Count();
                row.TopAction = GetTopCountedAction(buffEvents, received ? "Most received" : "Most used");
                row.Accuracy = "Unique buffs: " + row.Other.ToString("N0", CultureInfo.InvariantCulture);
                row.CriticalRate = received
                    ? "Buff applications received: " + row.Damage.ToString("N0", CultureInfo.InvariantCulture)
                    : "Buff applications used: " + row.Damage.ToString("N0", CultureInfo.InvariantCulture);
                result.Add(row);
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildBuffsByAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            string displayMode,
            int fightCount)
        {
            bool received = displayMode == "received";
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var combatantGroups = events
                .Where(IsBuffApplication)
                .Where(row => received ? row.IsTargetIDNull() == false : row.IsActorIDNull() == false)
                .GroupBy(row => received ? row.TargetID : row.ActorID);

            foreach (var combatantGroup in combatantGroups)
            {
                KPDatabaseDataSet.InteractionsRow first = combatantGroup.First();
                KPDatabaseDataSet.CombatantsRow combatant = received
                    ? first.CombatantsRowByTargetCombatantRelation
                    : first.CombatantsRowByActorCombatantRelation;
                if (combatant == null)
                    continue;

                EntityType entityType = (EntityType)combatant.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] combatantEvents = combatantGroup.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(combatant, entityType, events, combatantScope) == false)
                {
                    continue;
                }

                foreach (var actionGroup in combatantEvents.GroupBy(GetActionName))
                {
                    KPDatabaseDataSet.InteractionsRow[] actionEvents = actionGroup.ToArray();
                    SanctumCombatantSnapshot row = CreateActionCombatant(
                        combatant,
                        entityType,
                        actionGroup.Key);
                    row.Damage = actionEvents.Length;
                    row.Dps = (double)actionEvents.Length / Math.Max(1, fightCount);
                    row.Melee = actionEvents
                        .Where(evt => evt.IsTargetIDNull() == false)
                        .Select(evt => evt.TargetID)
                        .Distinct()
                        .Count();
                    row.WeaponSkills = actionEvents
                        .Where(evt => evt.IsBattleIDNull() == false)
                        .Select(evt => evt.BattleID)
                        .Distinct()
                        .Count();
                    row.Magic = actionEvents.Count(evt => (ActionType)evt.ActionType == ActionType.Spell);
                    row.Other = actionEvents.Count(evt => (ActionType)evt.ActionType == ActionType.Ability);
                    row.TopAction = (received ? "Received " : "Used ") + actionGroup.Key + " " +
                        actionEvents.Length.ToString("N0", CultureInfo.InvariantCulture) + " times";
                    row.Accuracy = "Type: " + GetActionCategory((ActionType)actionEvents[0].ActionType);
                    row.CriticalRate = "Targets: " + row.Melee.ToString("N0", CultureInfo.InvariantCulture);
                    result.Add(row);
                }
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildBuffUptime(
            KPDatabaseDataSet dataSet,
            IList<KPDatabaseDataSet.BattlesRow> battles,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            bool parserRunning,
            double durationSeconds)
        {
            KPDatabaseDataSet.InteractionsRow[] eventRows = events.ToArray();
            List<KPDatabaseDataSet.CombatantsRow> players = dataSet.Combatants
                .Where(combatant =>
                    (EntityType)combatant.CombatantType == EntityType.Player &&
                    eventRows.Any(row =>
                        (row.IsActorIDNull() == false && row.ActorID == combatant.CombatantID) ||
                        (row.IsTargetIDNull() == false && row.TargetID == combatant.CombatantID)) &&
                    IsInCombatantScope(
                        combatant,
                        EntityType.Player,
                        eventRows,
                        combatantScope))
                .ToList();
            if (players.Count == 0 || battles.Count == 0)
                return new List<SanctumCombatantSnapshot>();

            List<PlayerTimeIntervalSets> playerIntervals = CollectTimeIntervals.GetTimeIntervals(
                dataSet,
                players.Select(player => player.CombatantName).ToList());
            List<TimeInterval> fightWindows = battles.Select(battle =>
            {
                DateTime start = AsUtc(battle.StartTime);
                return new TimeInterval(
                    start,
                    start.AddSeconds(GetDurationSeconds(
                        battle,
                        battle.GetInteractionsRows(),
                        parserRunning)));
            }).ToList();

            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            foreach (PlayerTimeIntervalSets playerSet in playerIntervals)
            {
                KPDatabaseDataSet.CombatantsRow player = players.FirstOrDefault(row =>
                    string.Equals(
                        row.CombatantName,
                        playerSet.PlayerName,
                        StringComparison.OrdinalIgnoreCase));
                if (player == null)
                    continue;

                foreach (TimeIntervalSet buffSet in playerSet.TimeIntervalSets)
                {
                    List<TimeInterval> clipped = ClipAndMergeIntervals(
                        buffSet.TimeIntervals,
                        fightWindows);
                    double activeSeconds = clipped.Sum(interval => interval.Duration.TotalSeconds);
                    if (activeSeconds <= 0.0)
                        continue;

                    int applications = eventRows.Count(row =>
                        IsBuffApplication(row) &&
                        row.IsTargetIDNull() == false &&
                        row.TargetID == player.CombatantID &&
                        string.Equals(
                            GetActionName(row),
                            buffSet.SetName,
                            StringComparison.OrdinalIgnoreCase));
                    int coveredFights = fightWindows.Count(window =>
                        clipped.Any(interval => interval.Intersection(window) != TimeInterval.Zero));
                    double longest = clipped.Count == 0
                        ? 0.0
                        : clipped.Max(interval => interval.Duration.TotalSeconds);
                    double average = clipped.Count == 0
                        ? 0.0
                        : activeSeconds / clipped.Count;
                    double uptime = activeSeconds * 100.0 / Math.Max(1.0, durationSeconds);

                    SanctumCombatantSnapshot buffRow = CreateActionCombatant(
                        player,
                        EntityType.Player,
                        buffSet.SetName);
                    buffRow.Damage = (long)Math.Round(activeSeconds);
                    buffRow.Dps = uptime;
                    buffRow.Melee = applications;
                    buffRow.WeaponSkills = coveredFights;
                    buffRow.Magic = (long)Math.Round(longest);
                    buffRow.Other = (long)Math.Round(average);
                    buffRow.PrimaryText = FormatReportDuration(activeSeconds);
                    buffRow.RateText = uptime.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                    buffRow.Detail1Text = applications.ToString("N0", CultureInfo.InvariantCulture);
                    buffRow.Detail2Text = coveredFights.ToString("N0", CultureInfo.InvariantCulture);
                    buffRow.Detail3Text = FormatReportDuration(longest);
                    buffRow.Detail4Text = FormatReportDuration(average);
                    buffRow.TopAction = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} active for {1} ({2:0.0}% uptime)",
                        buffSet.SetName,
                        FormatReportDuration(activeSeconds),
                        uptime);
                    buffRow.Accuracy = "Applications: " + applications.ToString("N0", CultureInfo.InvariantCulture);
                    buffRow.CriticalRate = "Measured across selected mob-fight time";
                    result.Add(buffRow);
                }
            }

            return result;
        }

        private static List<TimeInterval> ClipAndMergeIntervals(
            IEnumerable<TimeInterval> source,
            IEnumerable<TimeInterval> windows)
        {
            List<TimeInterval> clipped = new List<TimeInterval>();
            foreach (TimeInterval interval in source)
            {
                TimeInterval normalized = new TimeInterval(
                    AsUtc(interval.StartTime),
                    AsUtc(interval.EndTime));
                foreach (TimeInterval window in windows)
                {
                    TimeInterval overlap = normalized.Intersection(window);
                    if (overlap != TimeInterval.Zero && overlap.Duration > TimeSpan.Zero)
                        clipped.Add(overlap);
                }
            }

            List<TimeInterval> merged = new List<TimeInterval>();
            foreach (TimeInterval interval in clipped.OrderBy(item => item.StartTime))
            {
                if (merged.Count == 0 || interval.StartTime > merged[merged.Count - 1].EndTime)
                {
                    merged.Add(interval);
                    continue;
                }

                TimeInterval previous = merged[merged.Count - 1];
                DateTime end = interval.EndTime > previous.EndTime
                    ? interval.EndTime
                    : previous.EndTime;
                merged[merged.Count - 1] = new TimeInterval(previous.StartTime, end);
            }
            return merged;
        }

        private static List<SanctumCombatantSnapshot> BuildDebuffs(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            IDictionary<int, int> enemyIds,
            string combatantScope,
            string displayMode,
            int fightCount)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var actorGroups = events
                .Where(row => IsDebuffAttempt(row) &&
                              IsDebuffDisplayEvent(row, displayMode) &&
                              row.IsBattleIDNull() == false &&
                              row.IsTargetIDNull() == false &&
                              enemyIds.ContainsKey(row.BattleID) &&
                              row.TargetID == enemyIds[row.BattleID] &&
                              row.IsActorIDNull() == false)
                .GroupBy(row => row.ActorID);

            foreach (var actorGroup in actorGroups)
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    actorGroup.First().CombatantsRowByActorCombatantRelation;
                if (actor == null)
                    continue;

                EntityType entityType = (EntityType)actor.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] debuffs = actorGroup.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(actor, entityType, debuffs, combatantScope) == false)
                {
                    continue;
                }

                long successful = debuffs.Count(IsSuccessfulDebuff);
                long noEffect = debuffs.Count(evt =>
                    (FailedActionType)evt.FailedActionType == FailedActionType.NoEffect);
                long failed = debuffs.Length - successful - noEffect;
                SanctumCombatantSnapshot row = CreateCombatant(actor, entityType);
                row.Damage = debuffs.Length;
                row.Dps = debuffs.Length == 0 ? 0.0 : (double)successful * 100.0 / debuffs.Length;
                row.Melee = successful;
                row.WeaponSkills = noEffect;
                row.Magic = Math.Max(0, failed);
                row.Other = debuffs.Select(GetActionName).Distinct().Count();
                row.TopAction = GetTopCountedAction(debuffs, "Most used debuff");
                row.Accuracy = string.Format(
                    CultureInfo.InvariantCulture,
                    "Success rate: {0:0.0}%",
                    row.Dps);
                row.CriticalRate = "Attempts per fight: " +
                    ((double)debuffs.Length / Math.Max(1, fightCount))
                    .ToString("N1", CultureInfo.InvariantCulture);
                result.Add(row);
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildDebuffsByAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            IDictionary<int, int> enemyIds,
            string combatantScope,
            string displayMode,
            int fightCount)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var actorGroups = events
                .Where(row => IsDebuffAttempt(row) &&
                              IsDebuffDisplayEvent(row, displayMode) &&
                              row.IsBattleIDNull() == false &&
                              row.IsTargetIDNull() == false &&
                              enemyIds.ContainsKey(row.BattleID) &&
                              row.TargetID == enemyIds[row.BattleID] &&
                              row.IsActorIDNull() == false)
                .GroupBy(row => row.ActorID);

            foreach (var actorGroup in actorGroups)
            {
                KPDatabaseDataSet.CombatantsRow actor =
                    actorGroup.First().CombatantsRowByActorCombatantRelation;
                if (actor == null)
                    continue;

                EntityType entityType = (EntityType)actor.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] actorEvents = actorGroup.ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(actor, entityType, actorEvents, combatantScope) == false)
                {
                    continue;
                }

                foreach (var actionGroup in actorEvents.GroupBy(GetActionName))
                {
                    KPDatabaseDataSet.InteractionsRow[] actionEvents = actionGroup.ToArray();
                    long successful = actionEvents.Count(IsSuccessfulDebuff);
                    long noEffect = actionEvents.Count(evt =>
                        (FailedActionType)evt.FailedActionType == FailedActionType.NoEffect);
                    long failed = actionEvents.Length - successful - noEffect;
                    SanctumCombatantSnapshot row = CreateActionCombatant(
                        actor,
                        entityType,
                        actionGroup.Key);
                    row.Damage = actionEvents.Length;
                    row.Dps = actionEvents.Length == 0
                        ? 0.0
                        : (double)successful * 100.0 / actionEvents.Length;
                    row.Melee = successful;
                    row.WeaponSkills = noEffect;
                    row.Magic = Math.Max(0, failed);
                    row.Other = actionEvents
                        .Where(evt => evt.IsTargetIDNull() == false)
                        .Select(evt => evt.TargetID)
                        .Distinct()
                        .Count();
                    row.TopAction = actionGroup.Key + ": " + successful.ToString("N0", CultureInfo.InvariantCulture) +
                        "/" + actionEvents.Length.ToString("N0", CultureInfo.InvariantCulture) + " landed";
                    row.Accuracy = string.Format(
                        CultureInfo.InvariantCulture,
                        "Success rate: {0:0.0}%",
                        row.Dps);
                    row.CriticalRate = "Attempts per fight: " +
                        ((double)actionEvents.Length / Math.Max(1, fightCount))
                        .ToString("N1", CultureInfo.InvariantCulture);
                    result.Add(row);
                }
            }

            return result;
        }

        private static List<SanctumCombatantSnapshot> BuildDeaths(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string combatantScope,
            int fightCount)
        {
            List<SanctumCombatantSnapshot> result = new List<SanctumCombatantSnapshot>();
            var targetGroups = events
                .Where(row => (ActionType)row.ActionType == ActionType.Death &&
                              row.IsTargetIDNull() == false)
                .GroupBy(row => row.TargetID);

            foreach (var targetGroup in targetGroups)
            {
                KPDatabaseDataSet.CombatantsRow target =
                    targetGroup.First().CombatantsRowByTargetCombatantRelation;
                if (target == null)
                    continue;

                EntityType entityType = (EntityType)target.CombatantType;
                KPDatabaseDataSet.InteractionsRow[] deaths = targetGroup
                    .OrderBy(evt => evt.Timestamp)
                    .ToArray();
                if (IsFriendlyCombatant(entityType) == false ||
                    IsInCombatantScope(target, entityType, events, combatantScope) == false)
                {
                    continue;
                }

                long mobKills = deaths.Count(evt =>
                    evt.IsActorIDNull() == false &&
                    IsEnemyEntity((EntityType)evt.CombatantsRowByActorCombatantRelation.CombatantType));
                long knownKills = deaths.Count(evt => evt.IsActorIDNull() == false);
                KPDatabaseDataSet.InteractionsRow latest = deaths[deaths.Length - 1];
                string killer = latest.IsActorIDNull()
                    ? "Unknown"
                    : latest.CombatantsRowByActorCombatantRelation.CombatantName;

                SanctumCombatantSnapshot row = CreateCombatant(target, entityType);
                row.Damage = deaths.Length;
                row.Dps = (double)deaths.Length / Math.Max(1, fightCount);
                row.Melee = mobKills;
                row.WeaponSkills = deaths.Length - mobKills;
                row.Magic = deaths
                    .Where(evt => evt.IsBattleIDNull() == false)
                    .Select(evt => evt.BattleID)
                    .Distinct()
                    .Count();
                row.Other = deaths
                    .Where(evt => evt.IsActorIDNull() == false)
                    .Select(evt => evt.ActorID)
                    .Distinct()
                    .Count();
                row.TopAction = "Last killed by: " + killer + " at " +
                    AsUtc(latest.Timestamp).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                row.Accuracy = "Known killers: " + knownKills.ToString("N0", CultureInfo.InvariantCulture);
                row.CriticalRate = "Fights with a death: " + row.Magic.ToString("N0", CultureInfo.InvariantCulture);
                result.Add(row);
            }

            return result;
        }

        private static SanctumCombatantSnapshot CreateCombatant(
            KPDatabaseDataSet.CombatantsRow combatant,
            EntityType entityType)
        {
            return new SanctumCombatantSnapshot
            {
                Key = combatant.CombatantID.ToString(CultureInfo.InvariantCulture),
                Name = combatant.CombatantName,
                Job = GetJob(combatant, entityType),
                CombatantType = entityType.ToString(),
                IsLocalPlayer = entityType == EntityType.Player &&
                    combatant.GetInteractionsRowsByActorCombatantRelation().Any(interaction =>
                        (ActorPlayerType)interaction.ActorType == ActorPlayerType.Self),
                TopAction = "No action summary available",
                Accuracy = "-",
                CriticalRate = "-"
            };
        }

        private static SanctumCombatantSnapshot CreateActionCombatant(
            KPDatabaseDataSet.CombatantsRow combatant,
            EntityType entityType,
            string actionName)
        {
            SanctumCombatantSnapshot row = CreateCombatant(combatant, entityType);
            row.Key = row.Key + "|" + (actionName ?? string.Empty);
            row.Job = string.IsNullOrEmpty(actionName) ? "Unknown action" : actionName;
            return row;
        }

        private static void AddEncounterFilters(
            SanctumBridgeSnapshot snapshot,
            IList<KPDatabaseDataSet.BattlesRow> battles)
        {
            snapshot.Filters.Add(new SanctumEncounterFilterSnapshot
            {
                Scope = "all",
                MobName = string.Empty,
                Label = "Running total - all mob fights"
            });

            if (battles.Count == 0)
                return;

            KPDatabaseDataSet.BattlesRow latest = battles[battles.Count - 1];
            snapshot.Filters.Add(new SanctumEncounterFilterSnapshot
            {
                Scope = "current",
                MobName = string.Empty,
                Label = "Current fight - " + GetEnemyName(latest)
            });

            foreach (KPDatabaseDataSet.BattlesRow battle in battles
                .OrderByDescending(item => item.StartTime)
                .ThenByDescending(item => item.BattleID)
                .Take(25))
            {
                double durationSeconds = GetDurationSeconds(
                    battle,
                    battle.GetInteractionsRows(),
                    snapshot.ParserRunning);
                string state = battle.IsOver ? FormatFilterDuration(durationSeconds) : "active";

                snapshot.Filters.Add(new SanctumEncounterFilterSnapshot
                {
                    Scope = "battle",
                    BattleId = battle.BattleID,
                    MobName = GetEnemyName(battle),
                    Label = string.Format(
                        CultureInfo.InvariantCulture,
                        "Fight history - {0} - {1:g} ({2})",
                        GetEnemyName(battle),
                        AsUtc(battle.StartTime).ToLocalTime(),
                        state)
                });
            }

            foreach (var mobGroup in battles
                .GroupBy(GetEnemyName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                snapshot.Filters.Add(new SanctumEncounterFilterSnapshot
                {
                    Scope = "mob",
                    MobName = mobGroup.Key,
                    Label = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} - {1} fight{2}",
                        mobGroup.Key,
                        mobGroup.Count(),
                        mobGroup.Count() == 1 ? string.Empty : "s")
                });
            }
        }

        private static void AddCombatantFilters(
            KPDatabaseDataSet dataSet,
            SanctumBridgeSnapshot snapshot,
            IList<KPDatabaseDataSet.BattlesRow> battles,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events)
        {
            snapshot.CombatantFilters.Clear();
            if (snapshot.Report == "loot")
            {
                snapshot.CombatantFilters.Add(new SanctumCombatantFilterSnapshot
                {
                    Key = "all",
                    Label = "All recipients"
                });

                HashSet<int> battleIds = new HashSet<int>(
                    battles.Select(battle => battle.BattleID));
                foreach (string recipient in dataSet.Loot
                    .Where(row => row.IsBattleIDNull() == false &&
                                  battleIds.Contains(row.BattleID) &&
                                  row.IsPlayerIDNull() == false &&
                                  row.CombatantsRow != null &&
                                  (EntityType)row.CombatantsRow.CombatantType == EntityType.Player)
                    .Select(row => row.CombatantsRow.CombatantName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    snapshot.CombatantFilters.Add(new SanctumCombatantFilterSnapshot
                    {
                        Key = "recipient:" + recipient,
                        Label = recipient
                    });
                }
                return;
            }

            snapshot.CombatantFilters.Add(new SanctumCombatantFilterSnapshot
            {
                Key = "all",
                Label = "Entire alliance"
            });
            snapshot.CombatantFilters.Add(new SanctumCombatantFilterSnapshot
            {
                Key = "party",
                Label = "Party only"
            });
            snapshot.CombatantFilters.Add(new SanctumCombatantFilterSnapshot
            {
                Key = "players",
                Label = "Players only"
            });
        }

        private static bool HasPartyDamage(KPDatabaseDataSet.BattlesRow battle)
        {
            if (battle == null || battle.IsEnemyIDNull())
                return false;

            return battle.GetInteractionsRows().Any(row =>
                row.IsActorIDNull() == false &&
                row.IsTargetIDNull() == false &&
                row.TargetID == battle.EnemyID &&
                GetOutgoingDamage(row) > 0 &&
                ((ActorPlayerType)row.ActorType == ActorPlayerType.Self ||
                 (ActorPlayerType)row.ActorType == ActorPlayerType.Party));
        }

        private static List<KPDatabaseDataSet.BattlesRow> SelectBattles(
            IList<KPDatabaseDataSet.BattlesRow> allBattles,
            ref string scope,
            int requestedBattleId,
            string requestedMobName)
        {
            if (scope == "current")
            {
                return new List<KPDatabaseDataSet.BattlesRow>
                {
                    allBattles[allBattles.Count - 1]
                };
            }

            if (scope == "battle" && requestedBattleId > 0)
            {
                KPDatabaseDataSet.BattlesRow selectedBattle = allBattles
                    .FirstOrDefault(battle => battle.BattleID == requestedBattleId);
                if (selectedBattle != null)
                {
                    return new List<KPDatabaseDataSet.BattlesRow>
                    {
                        selectedBattle
                    };
                }
            }

            if (scope == "mob" && string.IsNullOrEmpty(requestedMobName) == false)
            {
                List<KPDatabaseDataSet.BattlesRow> matches = allBattles
                    .Where(battle => string.Equals(
                        GetEnemyName(battle),
                        requestedMobName,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count > 0)
                    return matches;
            }

            scope = "all";
            return allBattles.ToList();
        }

        private static double GetAggregateRate(
            string report,
            string displayMode,
            long total,
            IEnumerable<SanctumCombatantSnapshot> rows,
            int fightCount,
            double durationSeconds)
        {
            if (report == "debuffs")
            {
                long successful = rows.Sum(row => row.Melee);
                return total == 0 ? 0.0 : (double)successful * 100.0 / total;
            }

            if (report == "buffs" && displayMode == "uptime")
                return rows.Any() ? rows.Average(row => row.Dps) : 0.0;

            if (report == "buffs" && displayMode == "corsair")
            {
                long rollCount = rows.Sum(row => row.Damage);
                return rollCount == 0 ? 0.0 :
                    rows.Sum(row => row.Dps * row.Damage) / rollCount;
            }

            if (report == "buffs" && displayMode == "performance")
                return rows.Any() ? rows.Average(row => row.Dps) : 0.0;

            if (report == "damageTaken" && displayMode == "buffperformance")
                return rows.Any() ? rows.Average(row => row.Dps) : 0.0;

            if (report == "damageDealt" && displayMode == "multiattacks")
            {
                long rounds = rows.Sum(row => row.Damage);
                long multiRounds = rows.Sum(row => row.WeaponSkills + row.Magic + row.Other);
                return rounds == 0 ? 0.0 : (double)multiRounds * 100.0 / rounds;
            }

            if (report == "experience")
                return displayMode == "chains"
                    ? (double)total / Math.Max(1, fightCount)
                    : total * 3600.0 / Math.Max(1.0, durationSeconds);

            if (report == "healing" && displayMode == "efficiency")
            {
                long actions = rows.Sum(row => row.Melee);
                return actions == 0 ? 0.0 : (double)total / actions;
            }

            if (report == "buffs" || report == "deaths" ||
                (report == "healing" && displayMode == "status"))
                return (double)total / Math.Max(1, fightCount);

            if (report == "loot")
            {
                if (displayMode == "rates" || displayMode == "treasurehunter")
                    return rows.Any() ? rows.Average(row => row.Dps) : 0.0;
                return (double)total / Math.Max(1, fightCount);
            }

            return total / Math.Max(1.0, durationSeconds);
        }

        private static SanctumReportColumnsSnapshot CreateDamageDealtColumns(
            string displayMode,
            string groupMode)
        {
            SanctumReportColumnsSnapshot columns = SanctumReportColumnsSnapshot.CreateDamageDealt();
            if (displayMode == "multiattacks")
            {
                columns.Primary = "Attack rounds";
                columns.Share = "Round share";
                columns.Rate = "Multi-hit rate";
                columns.Detail1 = "1 attack";
                columns.Detail2 = "2 attacks";
                columns.Detail3 = "3 attacks";
                columns.Detail4 = "4+ attacks";
                columns.Total = "INFERRED ATTACK ROUNDS";
                columns.TotalRate = "MULTI-HIT ROUND RATE";
                columns.RateSuffix = "%";
            }
            else if (displayMode == "dots")
            {
                columns.Primary = "Calculated DoT";
                columns.Rate = "Est. DPS";
                columns.Detail1 = "Applications";
                columns.Detail2 = "Est. ticks";
                columns.Detail3 = "Avg/tick";
                columns.Detail4 = "Effect time";
                columns.Total = "CALCULATED DOT";
                columns.TotalRate = "ESTIMATED DPS";
            }
            else if (displayMode == "accuracy")
            {
                columns.Detail1 = "Attempts";
                columns.Detail2 = "Hits";
                columns.Detail3 = "Misses";
                columns.Detail4 = "Critical hits";
            }
            else if (IsDamageCategoryDisplay(displayMode))
            {
                string label = GetDamageCategoryLabel(displayMode);
                columns.Primary = label + " damage";
                columns.Detail1 = GetDamageCategoryAttemptLabel(displayMode);
                columns.Detail2 = "Damaging";
                columns.Detail3 = "Average";
                columns.Detail4 = "Highest";
                columns.Total = label.ToUpperInvariant() + " DAMAGE";
                columns.TotalRate = label.ToUpperInvariant() + " DPS";
            }
            if (groupMode == "action")
            {
                columns.Name = "Player";
                columns.Secondary = "Action";
                if (displayMode != "dots")
                {
                    columns.Rate = "Average";
                    columns.Detail1 = GetDamageCategoryAttemptLabel(displayMode);
                    columns.Detail2 = "Damaging";
                    columns.Detail3 = "Lowest";
                    columns.Detail4 = "Highest";
                }
            }
            return columns;
        }

        private static SanctumReportColumnsSnapshot CreateDamageTakenColumns(
            string displayMode,
            string groupMode)
        {
            if (displayMode == "buffperformance")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Player",
                    Secondary = "Defensive buff",
                    Primary = "Damage taken",
                    Share = "Damage share",
                    Rate = "DTPS while active",
                    Detail1 = "Physical attempts",
                    Detail2 = "Hits taken",
                    Detail3 = "Avoided",
                    Detail4 = "Average hit",
                    Total = "DAMAGE DURING DEFENSIVE BUFFS",
                    TotalRate = "AVERAGE ACTIVE DTPS",
                    RateSuffix = string.Empty
                };
            }

            bool categoryMode = IsIncomingDamageCategoryDisplay(displayMode);
            string categoryLabel = categoryMode ? GetIncomingDamageCategoryLabel(displayMode) : string.Empty;
            SanctumReportColumnsSnapshot columns = new SanctumReportColumnsSnapshot
            {
                Name = "Combatant",
                Secondary = "Job",
                Primary = categoryMode ? categoryLabel + " damage taken" : "Damage taken",
                Share = "Share",
                Rate = "DTPS",
                Detail1 = displayMode == "defense" ? "Evaded" : categoryMode ? "Attempts" : "Melee",
                Detail2 = displayMode == "defense" ? "Shadows" : categoryMode ? "Hits taken" : "Ranged",
                Detail3 = displayMode == "defense" ? "Blocked/guarded" : categoryMode ? "Average hit" : "Magic",
                Detail4 = displayMode == "defense" ? "Resisted/absorbed" : categoryMode ? "Largest hit" : "Other",
                Total = categoryMode ? categoryLabel.ToUpperInvariant() + " DAMAGE TAKEN" : "TOTAL DAMAGE TAKEN",
                TotalRate = categoryMode ? categoryLabel.ToUpperInvariant() + " DTPS" : "ALLIANCE DTPS",
                RateSuffix = string.Empty
            };
            if (groupMode == "action")
            {
                columns.Name = "Target";
                columns.Secondary = "Source / action";
                columns.Rate = "Average hit";
                columns.Detail1 = "Attempts";
                columns.Detail2 = "Hits taken";
                columns.Detail3 = "Lowest hit";
                columns.Detail4 = "Largest hit";
            }
            return columns;
        }

        private static SanctumReportColumnsSnapshot CreateHealingColumns(
            string displayMode,
            string groupMode)
        {
            if (displayMode == "recipients")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Recipient",
                    Secondary = "Type",
                    Primary = "Healing received",
                    Share = "Share",
                    Rate = "HPS received",
                    Detail1 = "Healing actions",
                    Detail2 = "Spell healing",
                    Detail3 = "Ability healing",
                    Detail4 = "Largest heal",
                    Total = "HEALING RECEIVED",
                    TotalRate = "RECEIVED HPS",
                    RateSuffix = string.Empty
                };
            }
            if (displayMode == "recovery")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Player",
                    Secondary = "Job",
                    Primary = "Resources restored",
                    Share = "Share",
                    Rate = "Per second",
                    Detail1 = "HP",
                    Detail2 = "MP",
                    Detail3 = "TP",
                    Detail4 = "Actions",
                    Total = "RESOURCES RESTORED",
                    TotalRate = "RESTORE RATE",
                    RateSuffix = string.Empty
                };
            }
            if (displayMode == "efficiency")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Player",
                    Secondary = "Job",
                    Primary = "HP restored",
                    Share = "Share",
                    Rate = "Average heal",
                    Detail1 = "Healing actions",
                    Detail2 = "Recipients",
                    Detail3 = "Smallest heal",
                    Detail4 = "Largest heal",
                    Total = "OBSERVED HP RESTORED",
                    TotalRate = "AVERAGE HEAL",
                    RateSuffix = string.Empty
                };
            }
            if (displayMode == "status")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = groupMode == "action" ? "Player" : "Combatant",
                    Secondary = groupMode == "action" ? "Status removal" : "Job",
                    Primary = "Statuses cured",
                    Share = "Share",
                    Rate = "Per fight",
                    Detail1 = groupMode == "action" ? "Targets" : "Spells",
                    Detail2 = groupMode == "action" ? "Fights" : "Abilities",
                    Detail3 = groupMode == "action" ? "Spell uses" : "Unique cures",
                    Detail4 = groupMode == "action" ? "Ability uses" : "Targets",
                    Total = "STATUS CURES",
                    TotalRate = "CURES / FIGHT",
                    RateSuffix = string.Empty
                };
            }

            if (displayMode == "spells" || displayMode == "abilities")
            {
                string label = displayMode == "spells" ? "Magic healing" : "Ability healing";
                return new SanctumReportColumnsSnapshot
                {
                    Name = groupMode == "action" ? "Player" : "Combatant",
                    Secondary = groupMode == "action" ? "Healing action" : "Job",
                    Primary = label,
                    Share = "Share",
                    Rate = groupMode == "action" ? "Average heal" : "HPS",
                    Detail1 = displayMode == "spells" ? "Casts" : "Uses",
                    Detail2 = "Targets",
                    Detail3 = groupMode == "action" ? "Smallest heal" : "Average heal",
                    Detail4 = "Largest heal",
                    Total = label.ToUpperInvariant(),
                    TotalRate = "ALLIANCE HPS",
                    RateSuffix = string.Empty
                };
            }

            return new SanctumReportColumnsSnapshot
            {
                Name = groupMode == "action" ? "Player" : "Combatant",
                Secondary = groupMode == "action" ? "Healing action" : "Job",
                Primary = "HP restored",
                Share = "Share",
                Rate = groupMode == "action" ? "Average heal" : "HPS",
                Detail1 = groupMode == "action" ? "Uses" : "Spell healing",
                Detail2 = groupMode == "action" ? "Targets" : "Ability healing",
                Detail3 = groupMode == "action" ? "Smallest heal" : displayMode == "cures" ? "Average cure" : "Cure actions",
                Detail4 = groupMode == "action" ? "Largest heal" : displayMode == "cures" ? "Largest cure" : "Status cures",
                Total = "HP RESTORED",
                TotalRate = "ALLIANCE HPS",
                RateSuffix = string.Empty
            };
        }

        private static SanctumReportColumnsSnapshot CreateBuffColumns(
            string displayMode,
            string groupMode)
        {
            if (displayMode == "performance")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Player",
                    Secondary = "Buff",
                    Primary = "Damage while active",
                    Share = "Damage share",
                    Rate = "Active DPS",
                    Detail1 = "Melee attempts",
                    Detail2 = "Melee hits",
                    Detail3 = "Accuracy",
                    Detail4 = "Critical rate",
                    Total = "DAMAGE DURING BUFFS",
                    TotalRate = "AVERAGE ACTIVE DPS",
                    RateSuffix = string.Empty
                };
            }
            if (displayMode == "corsair")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Corsair",
                    Secondary = "Roll",
                    Primary = "Completed rolls",
                    Share = "Roll share",
                    Rate = "Average final value",
                    Detail1 = "Initial rolls",
                    Detail2 = "Double-Ups",
                    Detail3 = "Busts",
                    Detail4 = "Elevens",
                    Total = "COMPLETED ROLLS",
                    TotalRate = "AVERAGE FINAL VALUE",
                    RateSuffix = string.Empty
                };
            }
            if (displayMode == "uptime")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Player",
                    Secondary = "Buff",
                    Primary = "Active time",
                    Share = "Buff-time share",
                    Rate = "Uptime",
                    Detail1 = "Applications",
                    Detail2 = "Fights",
                    Detail3 = "Longest span",
                    Detail4 = "Average span",
                    Total = "TOTAL BUFF TIME",
                    TotalRate = "AVERAGE UPTIME",
                    RateSuffix = "%"
                };
            }

            bool received = displayMode == "received";
            return new SanctumReportColumnsSnapshot
            {
                Name = groupMode == "action" ? "Player" : "Combatant",
                Secondary = groupMode == "action" ? "Buff" : "Job",
                Primary = received ? "Buffs received" : "Buffs used",
                Share = "Share",
                Rate = "Per fight",
                Detail1 = groupMode == "action" ? "Targets" : "Spells",
                Detail2 = groupMode == "action" ? "Fights" : "Abilities",
                Detail3 = groupMode == "action" ? "Spell uses" : "Other",
                Detail4 = groupMode == "action" ? "Ability uses" : "Unique buffs",
                Total = received ? "BUFFS RECEIVED" : "BUFFS USED",
                TotalRate = "BUFFS / FIGHT",
                RateSuffix = string.Empty
            };
        }

        private static SanctumReportColumnsSnapshot CreateDebuffColumns(
            string displayMode,
            string groupMode)
        {
            string label = displayMode == "magic"
                ? "Magic debuff"
                : displayMode == "abilities" ? "Ability debuff" : "Debuff";
            return new SanctumReportColumnsSnapshot
            {
                Name = groupMode == "action" ? "Player" : "Combatant",
                Secondary = groupMode == "action" ? "Debuff" : "Job",
                Primary = label + " attempts",
                Share = "Share",
                Rate = "Success",
                Detail1 = "Landed",
                Detail2 = "No effect",
                Detail3 = "Resisted/failed",
                Detail4 = groupMode == "action" ? "Targets" : "Unique debuffs",
                Total = label.ToUpperInvariant() + " ATTEMPTS",
                TotalRate = "SUCCESS RATE",
                RateSuffix = "%"
            };
        }

        private static SanctumReportColumnsSnapshot CreateDeathColumns()
        {
            return new SanctumReportColumnsSnapshot
            {
                Name = "Combatant",
                Secondary = "Job",
                Primary = "Deaths",
                Share = "Share",
                Rate = "Per fight",
                Detail1 = "Mob kills",
                Detail2 = "Other/unknown",
                Detail3 = "Fights affected",
                Detail4 = "Unique killers",
                Total = "TOTAL DEATHS",
                TotalRate = "DEATHS / FIGHT",
                RateSuffix = string.Empty
            };
        }

        private static SanctumReportColumnsSnapshot CreateFightHistoryColumns()
        {
            return new SanctumReportColumnsSnapshot
            {
                Name = "Enemy",
                Secondary = "Result",
                Primary = "Total damage",
                Share = "Session share",
                Rate = "Alliance DPS",
                Detail1 = "Duration",
                Detail2 = "Experience",
                Detail3 = "Chain",
                Detail4 = "Events",
                Total = "FIGHT DAMAGE",
                TotalRate = "SESSION DPS",
                RateSuffix = string.Empty
            };
        }

        private static SanctumReportColumnsSnapshot CreatePlayerPerformanceColumns()
        {
            return new SanctumReportColumnsSnapshot
            {
                Name = "Player",
                Secondary = "Job",
                Primary = "Total damage",
                Share = "Damage share",
                Rate = "Active DPS",
                Detail1 = "Fights",
                Detail2 = "Participation",
                Detail3 = "Fight time",
                Detail4 = "Avg / fight",
                Total = "PLAYER DAMAGE",
                TotalRate = "SESSION DPS",
                RateSuffix = string.Empty
            };
        }

        private static SanctumReportColumnsSnapshot CreateChatColumns()
        {
            return new SanctumReportColumnsSnapshot
            {
                Name = "Speaker",
                Secondary = "Channel",
                Primary = "Time",
                Share = string.Empty,
                Rate = string.Empty,
                Detail1 = "Message",
                Detail2 = string.Empty,
                Detail3 = string.Empty,
                Detail4 = string.Empty,
                Total = "VISIBLE MESSAGES",
                TotalRate = "SPEAKERS",
                RateSuffix = string.Empty
            };
        }

        private static SanctumReportColumnsSnapshot CreateLootColumns(string displayMode)
        {
            if (displayMode == "helm")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Item / result",
                    Secondary = "Activity",
                    Primary = "Count",
                    Share = "Item share",
                    Rate = "Find rate",
                    Detail1 = "Attempts",
                    Detail2 = "Nothing found",
                    Detail3 = "Tool breaks",
                    Detail4 = "Found with ease",
                    Total = "HELM ITEMS FOUND",
                    TotalRate = "AVERAGE FIND RATE",
                    RateSuffix = "%"
                };
            }
            if (displayMode == "distribution")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Recipient",
                    Secondary = "Item",
                    Primary = "Quantity",
                    Share = "Share",
                    Rate = "Per fight",
                    Detail1 = "Records",
                    Detail2 = "Lost",
                    Detail3 = "Mob types",
                    Detail4 = string.Empty,
                    Total = "TOTAL QUANTITY",
                    TotalRate = "PER FIGHT",
                    RateSuffix = string.Empty
                };
            }

            if (displayMode == "rates" || displayMode == "treasurehunter")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Mob",
                    Secondary = displayMode == "treasurehunter" ? "TH / Item" : "Item",
                    Primary = "Drops",
                    Share = "Share",
                    Rate = "Drop rate",
                    Detail1 = "Kills",
                    Detail2 = "Kills with drop",
                    Detail3 = "Lost",
                    Detail4 = "Recipients",
                    Total = "TOTAL DROPS",
                    TotalRate = "AVERAGE RATE",
                    RateSuffix = string.Empty
                };
            }

            return new SanctumReportColumnsSnapshot
            {
                Name = "Item",
                Secondary = "Type",
                Primary = "Quantity",
                Share = "Share",
                Rate = "Per fight",
                Detail1 = "Recipients",
                Detail2 = "Lost",
                Detail3 = "Mob types",
                Detail4 = "Records",
                Total = "TOTAL QUANTITY",
                TotalRate = "PER FIGHT",
                RateSuffix = string.Empty
            };
        }

        private static SanctumReportColumnsSnapshot CreateCraftingColumns(string displayMode)
        {
            if (displayMode == "history")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Result item",
                    Secondary = "Crafter",
                    Primary = "Time",
                    Share = string.Empty,
                    Rate = "Result",
                    Detail1 = "Yield",
                    Detail2 = "Quality",
                    Detail3 = "Skill-up",
                    Detail4 = "Materials lost",
                    Total = "CRAFTING ATTEMPTS",
                    TotalRate = "SUCCESS RATE",
                    RateSuffix = "%"
                };
            }

            if (displayMode == "skillups")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Craft",
                    Secondary = "Crafter",
                    Primary = "Skill gain",
                    Share = string.Empty,
                    Rate = "Success",
                    Detail1 = "Skill-ups",
                    Detail2 = "Attempts",
                    Detail3 = "Latest level",
                    Detail4 = "Last skill-up",
                    Total = "CRAFTING ATTEMPTS",
                    TotalRate = "SUCCESS RATE",
                    RateSuffix = "%"
                };
            }

            if (displayMode == "materials")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Material",
                    Secondary = "Crafters",
                    Primary = "Quantity lost",
                    Share = "Loss share",
                    Rate = "Per break",
                    Detail1 = "Breaks",
                    Detail2 = "Recipes",
                    Detail3 = "Sessions",
                    Detail4 = "Last loss",
                    Total = "CRAFTING ATTEMPTS",
                    TotalRate = "SUCCESS RATE",
                    RateSuffix = "%"
                };
            }

            return new SanctumReportColumnsSnapshot
            {
                Name = "Recipe / result",
                Secondary = "Crafters",
                Primary = "Attempts",
                Share = "Attempt share",
                Rate = "Success",
                Detail1 = "Successes",
                Detail2 = "Breaks",
                Detail3 = "HQ",
                Detail4 = "Total yield",
                Total = "CRAFTING ATTEMPTS",
                TotalRate = "SUCCESS RATE",
                RateSuffix = "%"
            };
        }

        private static SanctumReportColumnsSnapshot CreateExperienceColumns(string displayMode)
        {
            if (displayMode == "history")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Enemy",
                    Secondary = "Result / difficulty",
                    Primary = "EXP",
                    Share = "EXP share",
                    Rate = "EXP/hour",
                    Detail1 = "Duration",
                    Detail2 = "Chain",
                    Detail3 = "Events",
                    Detail4 = "Time",
                    Total = "TOTAL EXPERIENCE",
                    TotalRate = "SESSION EXP/HOUR",
                    RateSuffix = string.Empty
                };
            }
            if (displayMode == "chains")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Chain",
                    Secondary = "Category",
                    Primary = "EXP",
                    Share = "EXP share",
                    Rate = "Average EXP",
                    Detail1 = "Fights",
                    Detail2 = "Lowest EXP",
                    Detail3 = "Highest EXP",
                    Detail4 = "Average duration",
                    Total = "TOTAL EXPERIENCE",
                    TotalRate = "AVERAGE EXP",
                    RateSuffix = string.Empty
                };
            }
            if (displayMode == "difficulty")
            {
                return new SanctumReportColumnsSnapshot
                {
                    Name = "Difficulty",
                    Secondary = "EXP category",
                    Primary = "EXP",
                    Share = "EXP share",
                    Rate = "EXP/hour",
                    Detail1 = "Fights",
                    Detail2 = "Average EXP",
                    Detail3 = "Highest chain",
                    Detail4 = "Average duration",
                    Total = "TOTAL EXPERIENCE",
                    TotalRate = "SESSION EXP/HOUR",
                    RateSuffix = string.Empty
                };
            }
            return new SanctumReportColumnsSnapshot
            {
                Name = "Enemy",
                Secondary = "EXP summary",
                Primary = "EXP",
                Share = "EXP share",
                Rate = "EXP/hour",
                Detail1 = "Fights",
                Detail2 = "Average EXP",
                Detail3 = "Highest chain",
                Detail4 = "Average duration",
                Total = "TOTAL EXPERIENCE",
                TotalRate = "SESSION EXP/HOUR",
                RateSuffix = string.Empty
            };
        }

        private static string NormalizeReport(string requestedReport)
        {
            string report = string.IsNullOrEmpty(requestedReport)
                ? "damageDealt"
                : requestedReport.Trim();
            return report == "damageTaken" || report == "healing" ||
                   report == "buffs" || report == "debuffs" || report == "deaths" ||
                   report == "fights" || report == "chat" || report == "loot" ||
                   report == "crafting" || report == "experience"
                ? report
                : "damageDealt";
        }

        private static string NormalizeCombatantScope(
            string report,
            string requestedScope)
        {
            string scope = string.IsNullOrEmpty(requestedScope)
                ? "all"
                : requestedScope.Trim().ToLowerInvariant();
            if (report == "loot" && scope.StartsWith("recipient:", StringComparison.Ordinal) &&
                scope.Length > "recipient:".Length && scope.Length <= 64)
            {
                return scope;
            }
            if (report == "crafting" && scope.StartsWith("crafter:", StringComparison.Ordinal) &&
                scope.Length > "crafter:".Length && scope.Length <= 64)
            {
                return scope;
            }
            return scope == "party" || scope == "players" ? scope : "all";
        }

        private static string NormalizeDisplayMode(string report, string requestedMode)
        {
            string mode = string.IsNullOrEmpty(requestedMode)
                ? string.Empty
                : requestedMode.Trim().ToLowerInvariant();

            switch (report)
            {
                case "damageDealt":
                    return mode == "accuracy" || mode == "sources" ||
                           mode == "melee" || mode == "ranged" ||
                           mode == "weaponskills" || mode == "abilities" ||
                           mode == "magic" || mode == "skillchains" ||
                           mode == "additional" || mode == "reactive" ||
                           mode == "dots" || mode == "multiattacks"
                        ? mode
                        : "summary";
                case "damageTaken":
                    return mode == "defense" || mode == "sources" ||
                           mode == "melee" || mode == "ranged" ||
                           mode == "magic" || mode == "other" ||
                           mode == "buffperformance"
                        ? mode
                        : "summary";
                case "healing":
                    return mode == "cures" || mode == "status" ||
                           mode == "spells" || mode == "abilities" ||
                           mode == "recipients" || mode == "recovery" ||
                           mode == "efficiency"
                        ? mode
                        : "summary";
                case "buffs":
                    return mode == "received" || mode == "uptime" ||
                           mode == "performance" || mode == "corsair"
                        ? mode
                        : "used";
                case "debuffs":
                    return mode == "magic" || mode == "abilities" ? mode : "summary";
                case "deaths":
                    return mode == "details" ? "details" : "summary";
                case "fights":
                    return mode == "performance" ? "performance" : "history";
                case "chat":
                    return mode == "say" || mode == "shout" || mode == "party" ||
                           mode == "linkshell" || mode == "tell" || mode == "emote" ||
                           mode == "npc" || mode == "arena" || mode == "echo"
                        ? mode
                        : "all";
                case "loot":
                    return mode == "distribution" || mode == "rates" ||
                           mode == "treasurehunter" || mode == "helm"
                        ? mode
                        : "summary";
                case "crafting":
                    return mode == "mine" || mode == "history" ||
                           mode == "skillups" || mode == "materials"
                        ? mode
                        : "summary";
                case "experience":
                    return mode == "history" || mode == "chains" ||
                           mode == "difficulty"
                        ? mode
                        : "mobs";
                default:
                    return "summary";
            }
        }

        private static string NormalizeGroupMode(
            string report,
            string displayMode,
            string requestedMode)
        {
            string mode = string.IsNullOrEmpty(requestedMode)
                ? "player"
                : requestedMode.Trim().ToLowerInvariant();
            if (mode != "action")
                return "player";

            if (report == "damageDealt")
                return IsDamageCategoryDisplay(displayMode) || displayMode == "dots"
                    ? "action"
                    : "player";
            if (report == "damageTaken")
                return IsIncomingDamageCategoryDisplay(displayMode) ? "action" : "player";
            if (report == "healing")
                return displayMode == "spells" || displayMode == "abilities" ||
                       displayMode == "cures" || displayMode == "status"
                    ? "action"
                    : "player";
            if (report == "buffs" && displayMode == "uptime")
                return "action";
            if (report == "buffs" && (displayMode == "performance" ||
                                      displayMode == "corsair"))
                return "player";
            if (report == "buffs" || report == "debuffs")
                return "action";

            return "player";
        }

        private static string NormalizeScope(string requestedScope)
        {
            string scope = string.IsNullOrEmpty(requestedScope)
                ? "all"
                : requestedScope.Trim().ToLowerInvariant();
            return scope == "current" || scope == "battle" || scope == "mob" ? scope : "all";
        }

        private static string FormatFilterDuration(double seconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0.0, Math.Floor(seconds)));
            return duration.TotalHours >= 1.0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:00}:{1:00}:{2:00}",
                    (int)duration.TotalHours,
                    duration.Minutes,
                    duration.Seconds)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:00}:{1:00}",
                    duration.Minutes,
                    duration.Seconds);
        }

        private static bool IsInCombatantScope(
            KPDatabaseDataSet.CombatantsRow combatant,
            EntityType entityType,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            string scope)
        {
            if (scope == "players")
                return entityType == EntityType.Player;

            if (scope != "party")
                return true;

            if (entityType != EntityType.Player &&
                entityType != EntityType.Pet &&
                entityType != EntityType.Fellow)
                return false;

            return events.Any(row =>
                row.IsActorIDNull() == false &&
                row.ActorID == combatant.CombatantID &&
                ((ActorPlayerType)row.ActorType == ActorPlayerType.Self ||
                 (ActorPlayerType)row.ActorType == ActorPlayerType.Party));
        }

        private static bool IsFriendlyCombatant(EntityType entityType)
        {
            return entityType == EntityType.Player ||
                   entityType == EntityType.Pet ||
                   entityType == EntityType.Fellow ||
                   entityType == EntityType.CharmedMob;
        }

        private static bool IsDamageActor(EntityType entityType)
        {
            return IsFriendlyCombatant(entityType) || entityType == EntityType.Skillchain;
        }

        private static bool IsEnemyEntity(EntityType entityType)
        {
            return entityType == EntityType.Mob || entityType == EntityType.CharmedPlayer;
        }

        private static long SumPrimaryDamage(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows,
            ActionType actionType)
        {
            return rows
                .Where(row => (ActionType)row.ActionType == actionType &&
                              IsCompletedDamage(row, actionType) &&
                              (DefenseType)row.DefenseType != DefenseType.Absorb)
                .Sum(row => (long)row.Amount);
        }

        private static long SumAdditionalEffectDamage(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows)
        {
            return rows
                .Where(row => ((ActionType)row.ActionType == ActionType.Melee ||
                               (ActionType)row.ActionType == ActionType.Ranged) &&
                              ((HarmType)row.SecondHarmType == HarmType.Damage ||
                               (HarmType)row.SecondHarmType == HarmType.Drain))
                .Sum(row => (long)row.SecondAmount);
        }

        private static long SumIncomingDamage(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows,
            ActionType actionType)
        {
            return rows
                .Where(row => (ActionType)row.ActionType == actionType)
                .Sum(item => GetIncomingDamage(item));
        }

        private static long GetIncomingDamage(KPDatabaseDataSet.InteractionsRow row)
        {
            long amount = 0;
            if (((HarmType)row.HarmType == HarmType.Damage ||
                 (HarmType)row.HarmType == HarmType.Drain) &&
                (DefenseType)row.DefenseType != DefenseType.Absorb &&
                IsCompletedDamage(row, (ActionType)row.ActionType))
            {
                amount += row.Amount;
            }

            if ((HarmType)row.SecondHarmType == HarmType.Damage ||
                (HarmType)row.SecondHarmType == HarmType.Drain)
            {
                amount += row.SecondAmount;
            }

            return amount;
        }

        private static bool IsCompletedDamage(
            KPDatabaseDataSet.InteractionsRow row,
            ActionType actionType)
        {
            if ((actionType == ActionType.Spell ||
                 actionType == ActionType.Ability ||
                 actionType == ActionType.Weaponskill) && row.Preparing)
            {
                return false;
            }

            if (actionType == ActionType.Counterattack ||
                actionType == ActionType.Retaliation ||
                actionType == ActionType.Spikes)
            {
                return row.Amount > 0;
            }

            HarmType harmType = (HarmType)row.HarmType;
            if (actionType == ActionType.Ability)
            {
                return harmType == HarmType.Damage ||
                       harmType == HarmType.Drain ||
                       (harmType == HarmType.Unknown && row.Amount > 0);
            }

            return harmType == HarmType.Damage || harmType == HarmType.Drain;
        }

        private static bool IsHpRecovery(KPDatabaseDataSet.InteractionsRow row)
        {
            return (AidType)row.AidType == AidType.Recovery &&
                   (RecoveryType)row.RecoveryType == RecoveryType.RecoverHP &&
                   row.Amount > 0;
        }

        private static bool IsStatusRecovery(KPDatabaseDataSet.InteractionsRow row)
        {
            return (AidType)row.AidType == AidType.RemoveStatus ||
                   (AidType)row.SecondAidType == AidType.RemoveStatus;
        }

        private static bool IsBuffApplication(KPDatabaseDataSet.InteractionsRow row)
        {
            return row.Preparing == false &&
                   ((AidType)row.AidType == AidType.Enhance ||
                    (AidType)row.AidType == AidType.RemoveEnmity);
        }

        private static bool IsDebuffAttempt(KPDatabaseDataSet.InteractionsRow row)
        {
            if (row.Preparing || row.IsActionIDNull())
                return false;

            HarmType primary = (HarmType)row.HarmType;
            HarmType secondary = (HarmType)row.SecondHarmType;
            return primary == HarmType.Enfeeble || primary == HarmType.Dispel ||
                   primary == HarmType.Unknown || secondary == HarmType.Enfeeble ||
                   secondary == HarmType.Dispel;
        }

        private static bool IsSuccessfulDebuff(KPDatabaseDataSet.InteractionsRow row)
        {
            HarmType primary = (HarmType)row.HarmType;
            HarmType secondary = (HarmType)row.SecondHarmType;
            return ((primary == HarmType.Dispel || primary == HarmType.Enfeeble ||
                     primary == HarmType.Unknown) &&
                    (DefenseType)row.DefenseType == DefenseType.None &&
                    (FailedActionType)row.FailedActionType == FailedActionType.None) ||
                   secondary == HarmType.Dispel || secondary == HarmType.Enfeeble;
        }

        private static bool IsPhysicalAttempt(KPDatabaseDataSet.InteractionsRow row)
        {
            ActionType action = (ActionType)row.ActionType;
            return action == ActionType.Melee || action == ActionType.Ranged;
        }

        private static bool IsEvaded(KPDatabaseDataSet.InteractionsRow row)
        {
            DefenseType defense = (DefenseType)row.DefenseType;
            return defense == DefenseType.Evasion || defense == DefenseType.Evade;
        }

        private static bool IsDamageCategoryDisplay(string displayMode)
        {
            return displayMode == "melee" || displayMode == "ranged" ||
                   displayMode == "weaponskills" || displayMode == "abilities" ||
                   displayMode == "magic" || displayMode == "skillchains" ||
                   displayMode == "additional" || displayMode == "reactive";
        }

        private static bool IsDamageCategoryEvent(
            KPDatabaseDataSet.InteractionsRow row,
            string displayMode)
        {
            ActionType actionType = (ActionType)row.ActionType;
            switch (displayMode)
            {
                case "melee":
                    return actionType == ActionType.Melee;
                case "ranged":
                    return actionType == ActionType.Ranged;
                case "weaponskills":
                    return actionType == ActionType.Weaponskill;
                case "abilities":
                    return actionType == ActionType.Ability;
                case "magic":
                    return actionType == ActionType.Spell;
                case "skillchains":
                    return actionType == ActionType.Skillchain;
                case "additional":
                    return (actionType == ActionType.Melee || actionType == ActionType.Ranged) &&
                           ((HarmType)row.SecondHarmType == HarmType.Damage ||
                            (HarmType)row.SecondHarmType == HarmType.Drain) &&
                           row.SecondAmount > 0;
                case "reactive":
                    return actionType == ActionType.Counterattack ||
                           actionType == ActionType.Retaliation ||
                           actionType == ActionType.Spikes;
                default:
                    return false;
            }
        }

        private static long GetDamageCategoryAmount(
            KPDatabaseDataSet.InteractionsRow row,
            string displayMode)
        {
            return displayMode == "additional" ? row.SecondAmount : GetPrimaryOutgoingDamage(row);
        }

        private static string GetDamageCategoryLabel(string displayMode)
        {
            switch (displayMode)
            {
                case "melee": return "Melee";
                case "ranged": return "Ranged";
                case "weaponskills": return "Weapon skill";
                case "abilities": return "Ability";
                case "magic": return "Magic";
                case "skillchains": return "Skillchain";
                case "additional": return "Additional effect";
                case "reactive": return "Reactive";
                default: return "Damage";
            }
        }

        private static string GetDamageCategoryAttemptLabel(string displayMode)
        {
            switch (displayMode)
            {
                case "magic": return "Casts";
                case "abilities": return "Uses";
                case "weaponskills": return "Uses";
                case "skillchains": return "Chains";
                case "additional": return "Procs";
                case "reactive": return "Triggers";
                default: return "Attempts";
            }
        }

        private static string GetTopDamageCategoryAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows,
            string displayMode)
        {
            var top = rows
                .GroupBy(GetDetailedActionName)
                .Select(group => new
                {
                    Action = group.Key,
                    Damage = group.Sum(row => GetDamageCategoryAmount(row, displayMode))
                })
                .OrderByDescending(item => item.Damage)
                .ThenBy(item => item.Action)
                .FirstOrDefault();
            return top == null
                ? "No action summary available"
                : "Top " + GetDamageCategoryLabel(displayMode).ToLowerInvariant() + ": " +
                  top.Action + " - " + top.Damage.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string GetDamageCategorySuccessRate(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows,
            string displayMode)
        {
            KPDatabaseDataSet.InteractionsRow[] attempts = rows.ToArray();
            int successful = attempts.Count(row => GetDamageCategoryAmount(row, displayMode) > 0);
            double rate = attempts.Length == 0 ? 0.0 : (double)successful * 100.0 / attempts.Length;
            return string.Format(CultureInfo.InvariantCulture, "Success rate: {0:0.0}%", rate);
        }

        private static string GetDamageCategoryCriticalRate(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows,
            string displayMode)
        {
            KPDatabaseDataSet.InteractionsRow[] successful = rows
                .Where(row => GetDamageCategoryAmount(row, displayMode) > 0)
                .ToArray();
            if (displayMode != "melee" && displayMode != "ranged" && displayMode != "weaponskills")
                return "Damaging actions: " + successful.Length.ToString("N0", CultureInfo.InvariantCulture);

            int criticals = successful.Count(row =>
                (DamageModifier)row.DamageModifier == DamageModifier.Critical);
            double rate = successful.Length == 0 ? 0.0 : (double)criticals * 100.0 / successful.Length;
            return string.Format(CultureInfo.InvariantCulture, "Critical rate: {0:0.0}%", rate);
        }

        private static bool IsIncomingDamageCategoryDisplay(string displayMode)
        {
            return displayMode == "melee" || displayMode == "ranged" ||
                   displayMode == "magic" || displayMode == "other";
        }

        private static bool IsIncomingDamageCategoryEvent(
            KPDatabaseDataSet.InteractionsRow row,
            string displayMode)
        {
            ActionType actionType = (ActionType)row.ActionType;
            if (displayMode == "melee") return actionType == ActionType.Melee;
            if (displayMode == "ranged") return actionType == ActionType.Ranged;
            if (displayMode == "magic") return actionType == ActionType.Spell;
            return actionType != ActionType.Melee &&
                   actionType != ActionType.Ranged &&
                   actionType != ActionType.Spell;
        }

        private static string GetIncomingDamageCategoryLabel(string displayMode)
        {
            if (displayMode == "melee") return "Melee";
            if (displayMode == "ranged") return "Ranged";
            if (displayMode == "magic") return "Magic";
            return "Other";
        }

        private static bool IsDebuffDisplayEvent(
            KPDatabaseDataSet.InteractionsRow row,
            string displayMode)
        {
            if (displayMode == "magic")
                return (ActionType)row.ActionType == ActionType.Spell;
            if (displayMode == "abilities")
                return (ActionType)row.ActionType == ActionType.Ability;
            return true;
        }

        private static bool IsDetailedOutgoingAction(KPDatabaseDataSet.InteractionsRow row)
        {
            ActionType actionType = (ActionType)row.ActionType;
            if (actionType == ActionType.Melee || actionType == ActionType.Ranged)
                return true;

            return actionType == ActionType.Weaponskill ||
                   actionType == ActionType.Spell ||
                   actionType == ActionType.Ability ||
                   actionType == ActionType.Skillchain ||
                   actionType == ActionType.Counterattack ||
                   actionType == ActionType.Retaliation ||
                   actionType == ActionType.Spikes;
        }

        private static long GetPrimaryOutgoingDamage(KPDatabaseDataSet.InteractionsRow row)
        {
            ActionType actionType = (ActionType)row.ActionType;
            return IsCompletedDamage(row, actionType) &&
                   (DefenseType)row.DefenseType != DefenseType.Absorb
                ? row.Amount
                : 0;
        }

        private static long GetOutgoingDamage(KPDatabaseDataSet.InteractionsRow row)
        {
            long damage = GetPrimaryOutgoingDamage(row);
            if ((HarmType)row.SecondHarmType == HarmType.Damage ||
                (HarmType)row.SecondHarmType == HarmType.Drain)
            {
                damage += row.SecondAmount;
            }
            return damage;
        }

        private static string GetActionCategory(ActionType actionType)
        {
            switch (actionType)
            {
                case ActionType.Melee:
                    return "Melee";
                case ActionType.Ranged:
                    return "Ranged";
                case ActionType.Weaponskill:
                    return "Weapon skill";
                case ActionType.Spell:
                    return "Spell";
                case ActionType.Ability:
                    return "Ability";
                case ActionType.Skillchain:
                    return "Skillchain";
                case ActionType.Counterattack:
                    return "Counter";
                case ActionType.Retaliation:
                    return "Retaliation";
                case ActionType.Spikes:
                    return "Spikes";
                default:
                    return actionType.ToString();
            }
        }

        private static string GetDetailedActionName(KPDatabaseDataSet.InteractionsRow row)
        {
            ActionType actionType = (ActionType)row.ActionType;
            if (row.IsActionIDNull() == false)
                return GetActionName(row);

            switch (actionType)
            {
                case ActionType.Melee:
                    return "Melee attacks";
                case ActionType.Ranged:
                    return "Ranged attacks";
                case ActionType.Counterattack:
                    return "Counterattacks";
                case ActionType.Retaliation:
                    return "Retaliations";
                case ActionType.Spikes:
                    return "Spikes damage";
                default:
                    return actionType.ToString();
            }
        }

        private static string GetTopAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows,
            EntityType entityType)
        {
            ActionSummary top = rows
                .Where(row => row.IsActionIDNull() == false &&
                              ((ActionType)row.ActionType == ActionType.Weaponskill ||
                               (ActionType)row.ActionType == ActionType.Spell ||
                               (ActionType)row.ActionType == ActionType.Ability) &&
                              IsCompletedDamage(row, (ActionType)row.ActionType) &&
                              (DefenseType)row.DefenseType != DefenseType.Absorb)
                .GroupBy(row => new
                {
                    row.ActionID,
                    ActionType = (ActionType)row.ActionType,
                    ActionName = GetActionName(row)
                })
                .Select(group => new ActionSummary
                {
                    ActionType = group.Key.ActionType,
                    ActionName = group.Key.ActionName,
                    Damage = group.Sum(row => (long)row.Amount)
                })
                .OrderByDescending(action => action.Damage)
                .FirstOrDefault();

            if (top == null || string.IsNullOrEmpty(top.ActionName))
            {
                return entityType == EntityType.Skillchain
                    ? "Skillchain damage"
                    : "No action summary available";
            }

            string label = top.ActionType == ActionType.Weaponskill
                ? "Top weapon skill"
                : top.ActionType == ActionType.Spell ? "Top spell" : "Top ability";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} - {2:N0}",
                label,
                top.ActionName,
                top.Damage);
        }

        private static string GetTopIncomingAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows)
        {
            var top = rows
                .Where(row => GetIncomingDamage(row) > 0)
                .GroupBy(row => new
                {
                    Source = row.IsActorIDNull()
                        ? "Unknown"
                        : row.CombatantsRowByActorCombatantRelation.CombatantName,
                    Action = GetActionName(row)
                })
                .Select(group => new
                {
                    group.Key.Source,
                    group.Key.Action,
                    Damage = group.Sum(item => GetIncomingDamage(item))
                })
                .OrderByDescending(entry => entry.Damage)
                .FirstOrDefault();

            return top == null
                ? "No incoming damage source available"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Top source: {0} / {1} - {2:N0}",
                    top.Source,
                    top.Action,
                    top.Damage);
        }

        private static string GetTopHealingAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> cures,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> statusCures)
        {
            var topHeal = cures
                .GroupBy(GetActionName)
                .Select(group => new { Name = group.Key, Amount = group.Sum(evt => (long)evt.Amount) })
                .OrderByDescending(entry => entry.Amount)
                .FirstOrDefault();
            if (topHeal != null)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Top healing action: {0} - {1:N0}",
                    topHeal.Name,
                    topHeal.Amount);
            }

            return GetTopCountedAction(statusCures, "Most-used status cure");
        }

        private static string GetTopCountedAction(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows,
            string label)
        {
            var top = rows
                .GroupBy(GetActionName)
                .Select(group => new { Name = group.Key, Count = group.Count() })
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Name)
                .FirstOrDefault();
            return top == null
                ? "No action summary available"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1} - {2:N0}",
                    label,
                    top.Name,
                    top.Count);
        }

        private static string GetActionName(KPDatabaseDataSet.InteractionsRow row)
        {
            if (row.IsActionIDNull() == false && row.ActionsRow != null &&
                string.IsNullOrEmpty(row.ActionsRow.ActionName) == false)
            {
                return row.ActionsRow.ActionName;
            }

            return ((ActionType)row.ActionType).ToString();
        }

        private static string GetAccuracy(IEnumerable<KPDatabaseDataSet.InteractionsRow> rows)
        {
            KPDatabaseDataSet.InteractionsRow[] attempts = rows.Where(IsPhysicalAttempt).ToArray();
            if (attempts.Length == 0)
                return "Accuracy: -";

            int hits = attempts.Count(row => IsEvaded(row) == false);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Accuracy: {0:0.0}%",
                (double)hits * 100.0 / attempts.Length);
        }

        private static string GetCriticalRate(IEnumerable<KPDatabaseDataSet.InteractionsRow> rows)
        {
            KPDatabaseDataSet.InteractionsRow[] hits = rows
                .Where(row => IsPhysicalAttempt(row) &&
                              ((HarmType)row.HarmType == HarmType.Damage ||
                               (HarmType)row.HarmType == HarmType.Drain))
                .ToArray();
            if (hits.Length == 0)
                return "Critical hit rate: -";

            int criticals = hits.Count(row =>
                (DamageModifier)row.DamageModifier == DamageModifier.Critical);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Critical hit rate: {0:0.0}%",
                (double)criticals * 100.0 / hits.Length);
        }

        private static string GetIncomingCriticalRate(
            IEnumerable<KPDatabaseDataSet.InteractionsRow> rows)
        {
            KPDatabaseDataSet.InteractionsRow[] hits = rows
                .Where(row => GetIncomingDamage(row) > 0 && IsPhysicalAttempt(row))
                .ToArray();
            if (hits.Length == 0)
                return "Incoming critical rate: -";

            int criticals = hits.Count(row =>
                (DamageModifier)row.DamageModifier == DamageModifier.Critical);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Incoming critical rate: {0:0.0}%",
                (double)criticals * 100.0 / hits.Length);
        }

        private static string GetJob(
            KPDatabaseDataSet.CombatantsRow actor,
            EntityType entityType)
        {
            if (entityType != EntityType.Player)
                return entityType == EntityType.Skillchain ? "Skillchain" : entityType.ToString();

            if (actor.IsPlayerInfoNull() == false && string.IsNullOrEmpty(actor.PlayerInfo) == false)
            {
                Match match = PlayerJob.Match(actor.PlayerInfo);
                if (match.Success)
                    return match.Groups["job"].Value.Trim().Replace("/", " / ");
            }

            SanctumPlayerStatProfile captured =
                SanctumDotProfileStore.GetForActor(actor.CombatantName);
            if (captured != null)
            {
                string mainJob = GetJobAbbreviation(captured.MainJob);
                string subJob = GetJobAbbreviation(captured.SubJob);
                return subJob == "-" ? mainJob : mainJob + " / " + subJob;
            }

            return InferJobFromActions(actor);
        }

        private static string GetJobAbbreviation(int jobId)
        {
            return jobId >= 0 && jobId < JobAbbreviations.Length
                ? JobAbbreviations[jobId]
                : "-";
        }

        private static string InferJobFromActions(KPDatabaseDataSet.CombatantsRow actor)
        {
            // Only unmistakable main-job actions are used.  Common spells and
            // subjob abilities are deliberately ignored rather than guessing.
            foreach (KPDatabaseDataSet.InteractionsRow interaction in
                actor.GetInteractionsRowsByActorCombatantRelation())
            {
                if (interaction.IsActionIDNull() || interaction.ActionsRow == null)
                    continue;

                string actionName = interaction.ActionsRow.ActionName;
                string job;
                if (MainJobActionSignatures.TryGetValue(actionName, out job))
                    return job;
            }

            return "-";
        }

        private static double GetDurationSeconds(
            KPDatabaseDataSet.BattlesRow battle,
            IEnumerable<KPDatabaseDataSet.InteractionsRow> events,
            bool parserRunning)
        {
            DateTime start = AsUtc(battle.StartTime);
            DateTime end;

            if (battle.IsOver && battle.IsEndTimeNull() == false)
            {
                end = AsUtc(battle.EndTime);
            }
            else if (parserRunning)
            {
                end = DateTime.UtcNow;
            }
            else
            {
                DateTime? lastEvent = events.Any()
                    ? (DateTime?)events.Max(row => AsUtc(row.Timestamp))
                    : null;
                end = lastEvent ?? start;
            }

            return Math.Max(0.0, (end - start).TotalSeconds);
        }

        private static string FormatReportDuration(double seconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
            if (duration.TotalHours >= 1)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1:00}:{2:00}",
                    (int)duration.TotalHours,
                    duration.Minutes,
                    duration.Seconds);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1:00}",
                (int)duration.TotalMinutes,
                duration.Seconds);
        }

        private static string GetEnemyName(KPDatabaseDataSet.BattlesRow battle)
        {
            string name = battle.CombatantsRowByEnemyCombatantRelation.CombatantName;
            return string.IsNullOrEmpty(name) ? "Unknown mob" : name;
        }

        private static DateTime AsUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static string GetEngineVersion()
        {
            Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            return assembly.GetName().Version.ToString();
        }

        private sealed class ActionSummary
        {
            public ActionType ActionType { get; set; }
            public string ActionName { get; set; }
            public long Damage { get; set; }
        }
    }
}
