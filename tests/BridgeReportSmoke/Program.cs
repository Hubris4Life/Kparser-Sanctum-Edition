using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using WaywardGamers.KParser;
using WaywardGamers.KParser.Bridge;

namespace BridgeReportSmoke
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--pet-protocol-only")
            {
                VerifySanctumPetProtocol();
                VerifyPetFirstEncounterGate();
                VerifyServerCompatibilityProfiles();
                VerifyCriticalRateDenominators();
                Console.WriteLine("sanctum-pet-protocol=verified");
                Console.WriteLine("pet-first-encounter=verified");
                Console.WriteLine("server-compatibility=verified");
                Console.WriteLine("critical-rate-denominators=verified");
                return 0;
            }

            if (args.Length == 1 && args[0] == "--dot-rules-only")
            {
                VerifyDotEstimatorRules();
                VerifyExtendedDotEstimatorRules();
                VerifyHorizonDotRules();
                VerifyPlayerStatLayout();
                Console.WriteLine("dot-estimator-rules=verified");
                Console.WriteLine("horizon-dot-rules=verified");
                Console.WriteLine("player-stat-layout=verified");
                return 0;
            }

            bool auditMode = args.Length == 2 && args[0] == "--audit";
            if ((!auditMode && args.Length != 1) || (auditMode && args.Length != 2))
                throw new ArgumentException("Expected a KParser database path.");

            string databasePath = auditMode ? args[1] : args[0];
            if (auditMode)
                return AuditDatabase(databasePath);

            VerifyRamReaderStopsBeforeReturning();
            VerifyDotEstimatorRules();
            VerifyExtendedDotEstimatorRules();
            VerifyHorizonDotRules();
            VerifyPlayerStatLayout();
            VerifyCriticalRateDenominators();
            ConfigureWritableDefaultDirectory(databasePath);
            DatabaseManager.Instance.OpenDatabase(databasePath);
            try
            {
                Type builder = typeof(DatabaseManager).Assembly.GetType(
                    "WaywardGamers.KParser.Bridge.SanctumDamageSnapshotBuilder",
                    true);
                MethodInfo build = builder.GetMethod(
                    "Build",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (build == null)
                    throw new MissingMethodException(builder.FullName, "Build");

                int verifiedDisplays = 0;
                verifiedDisplays += VerifyReport(
                    build,
                    "damageDealt",
                    new[] { "melee", "weaponskills", "magic", "dots", "abilities", "ranged", "skillchains", "additional", "reactive" });
                verifiedDisplays += VerifyReport(
                    build,
                    "damageTaken",
                    new[] { "melee", "ranged", "magic", "other" });
                verifiedDisplays += VerifyReport(
                    build,
                    "healing",
                    new[] { "spells", "abilities", "cures", "status" });
                verifiedDisplays += VerifyReport(
                    build,
                    "buffs",
                    new[] { "used", "received", "uptime" });
                verifiedDisplays += VerifyReport(
                    build,
                    "debuffs",
                    new[] { "summary", "magic", "abilities" });
                VerifyDotReport(build);
                VerifyFightReports(build);
                VerifyChatReport(build);
                VerifyLootReports(build);
                VerifyCraftingReport(build);
                VerifyBuffUptimeReport(build);
                VerifyAdvancedReports(build);
                VerifyLegacyParityReports(build);

                if (verifiedDisplays == 0)
                    throw new InvalidOperationException("The supplied parse contains no damage category to verify.");

                Console.WriteLine("action-report=verified");
                Console.WriteLine("verified-displays=" + verifiedDisplays);
                Console.WriteLine("dot-report=verified");
                Console.WriteLine("dot-estimator=verified");
                Console.WriteLine("horizon-dot-rules=verified");
                Console.WriteLine("ram-reader-lifecycle=verified");
                Console.WriteLine("fight-reports=verified");
                Console.WriteLine("chat-report=verified");
                Console.WriteLine("loot-reports=verified");
                Console.WriteLine("buff-uptime=verified");
                Console.WriteLine("current-preview-reports=verified");
                Console.WriteLine("legacy-parity-reports=verified");
                return 0;
            }
            finally
            {
                DatabaseManager.Instance.CloseDatabase();
            }
        }

        private static void VerifySanctumPetProtocol()
        {
            Assembly parserCore = typeof(DatabaseManager).Assembly;
            Type petNameType = parserCore.GetType(
                "WaywardGamers.KParser.Parsing.SanctumPetName",
                true);
            MethodInfo getToken = petNameType.GetMethod(
                "GetOwnerToken",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo tryParse = petNameType.GetMethod(
                "TryParse",
                BindingFlags.Static | BindingFlags.NonPublic);
            Type classifier = parserCore.GetType(
                "WaywardGamers.KParser.Parsing.ClassifyEntity",
                true);
            MethodInfo classify = classifier.GetMethod(
                "ClassifyByName",
                BindingFlags.Static | BindingFlags.NonPublic);
            Type builder = parserCore.GetType(
                "WaywardGamers.KParser.Bridge.SanctumDamageSnapshotBuilder",
                true);
            Type snapshotRowType = parserCore.GetType(
                "WaywardGamers.KParser.Bridge.SanctumCombatantSnapshot",
                true);
            MethodInfo applyOwnership = builder.GetMethod(
                "ApplySanctumPetOwnership",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (getToken == null || tryParse == null || classify == null ||
                applyOwnership == null || snapshotRowType == null)
            {
                throw new MissingMemberException(
                    "The Sanctum pet ownership protocol members could not be tested.");
            }

            string ownerName = "Nazgul";
            string token = (string)getToken.Invoke(null, new object[] { ownerName });
            if (token.Length != 5)
                throw new InvalidOperationException("The owner token is not five characters.");

            object[] parseArguments = { "Garuda@Nazgul", null, null };
            bool parsed = (bool)tryParse.Invoke(null, parseArguments);
            if (parsed == false || (string)parseArguments[1] != "Garuda" ||
                (string)parseArguments[2] != ownerName)
            {
                throw new InvalidOperationException("A full Sanctum pet owner name was not decoded.");
            }

            object[] expandedArguments = { "Nazgul's Garuda", null, null };
            if ((bool)tryParse.Invoke(null, expandedArguments) == false ||
                (string)expandedArguments[1] != "Garuda" ||
                (string)expandedArguments[2] != ownerName)
            {
                throw new InvalidOperationException(
                    "A SanctumChat-expanded pet name was not decoded.");
            }

            object[] expandedMultiwordArguments = { "Nazgul's Fire Spirit", null, null };
            if ((bool)tryParse.Invoke(null, expandedMultiwordArguments) == false ||
                (string)expandedMultiwordArguments[1] != "Fire Spirit" ||
                (string)expandedMultiwordArguments[2] != ownerName)
            {
                throw new InvalidOperationException(
                    "A SanctumChat-expanded multiword pet name was not decoded.");
            }

            object[] spacedPetArguments = { "Cait Sith@" + token, null, null };
            if ((bool)tryParse.Invoke(null, spacedPetArguments) == false ||
                (string)spacedPetArguments[1] != "Cait Sith")
            {
                throw new InvalidOperationException(
                    "A Sanctum pet name containing a space was not decoded.");
            }

            EntityType classification = (EntityType)classify.Invoke(
                null,
                new object[] { "Garuda@Nazgul" });
            if (classification != EntityType.Pet)
                throw new InvalidOperationException("A decorated Sanctum pet was not classified as a pet.");

            EntityType expandedClassification = (EntityType)classify.Invoke(
                null,
                new object[] { "Nazgul's Garuda" });
            if (expandedClassification != EntityType.Pet)
            {
                throw new InvalidOperationException(
                    "A SanctumChat-expanded pet was not classified as a pet.");
            }

            Type parseExpressions = parserCore.GetType(
                "WaywardGamers.KParser.ParseExpressions",
                true);
            FieldInfo meleeHitField = parseExpressions.GetField(
                "MeleeHit",
                BindingFlags.Static | BindingFlags.NonPublic);
            Regex meleeHit = meleeHitField == null
                ? null
                : meleeHitField.GetValue(null) as Regex;
            Match meleeMatch = meleeHit == null
                ? Match.Empty
                : meleeHit.Match("Garuda@Nazgul hits Shinryu for 300 points of damage.");
            if (meleeMatch.Success == false || meleeMatch.Groups["name"].Value != "Garuda@Nazgul")
            {
                throw new InvalidOperationException(
                    "The legacy combat expressions did not retain the decorated pet name.");
            }

            Match expandedMeleeMatch = meleeHit.Match(
                "Nazgul's Garuda hits Shinryu for 300 points of damage.");
            if (expandedMeleeMatch.Success == false ||
                expandedMeleeMatch.Groups["name"].Value != "Nazgul's Garuda")
            {
                throw new InvalidOperationException(
                    "The legacy combat expressions did not retain a SanctumChat-expanded pet name.");
            }

            Match expandedMultiwordMeleeMatch = meleeHit.Match(
                "Nazgul's Fire Spirit hits Shinryu for 300 points of damage.");
            if (expandedMultiwordMeleeMatch.Success == false ||
                expandedMultiwordMeleeMatch.Groups["name"].Value != "Nazgul's Fire Spirit")
            {
                throw new InvalidOperationException(
                    "The legacy combat expressions did not retain an expanded multiword pet name.");
            }

            Match spacedPetMeleeMatch = meleeHit.Match(
                "Cait Sith@" + token + " hits Shinryu for 300 points of damage.");
            if (spacedPetMeleeMatch.Success == false ||
                spacedPetMeleeMatch.Groups["name"].Value != "Cait Sith@" + token)
            {
                throw new InvalidOperationException(
                    "The legacy combat expressions did not retain a spaced pet name.");
            }

            KPDatabaseDataSet dataSet = new KPDatabaseDataSet();
            dataSet.Combatants.AddCombatantsRow(ownerName, (byte)EntityType.Player, string.Empty);

            IList combinedInput = CreatePetOwnershipRows(
                snapshotRowType,
                "CourierCa@" + token);
            IList combined = (IList)applyOwnership.Invoke(
                null,
                new object[] { combinedInput, dataSet, "all", "sources", "player", 10.0 });
            if (combined.Count != 1 ||
                (string)GetSnapshotValue(combined[0], "Name") != ownerName ||
                (long)GetSnapshotValue(combined[0], "Damage") != 1000L ||
                (string)GetSnapshotValue(combined[0], "Accuracy") != "Accuracy: 86.7%" ||
                (string)GetSnapshotValue(combined[0], "CriticalRate") != "Critical hit rate: 23.1%")
            {
                throw new InvalidOperationException(
                    "Pet damage or its combined physical rates were not attributed once to its owner.");
            }

            IList actionInput = CreatePetOwnershipRows(
                snapshotRowType,
                "Garuda@Nazgul");
            foreach (object actionRow in actionInput)
            {
                SetSnapshotValue(
                    actionRow,
                    "Key",
                    (string)GetSnapshotValue(actionRow, "Key") + "|Fire IV");
                SetSnapshotValue(actionRow, "Job", "Fire IV");
                SetSnapshotValue(actionRow, "Melee", 1L);
                SetSnapshotValue(actionRow, "WeaponSkills", 1L);
                SetSnapshotValue(actionRow, "Magic", (long)GetSnapshotValue(actionRow, "Damage"));
                SetSnapshotValue(actionRow, "Other", (long)GetSnapshotValue(actionRow, "Damage"));
            }
            IList actionCombined = (IList)applyOwnership.Invoke(
                null,
                new object[] { actionInput, dataSet, "all", "magic", "action", 10.0 });
            if (actionCombined.Count != 1 ||
                (long)GetSnapshotValue(actionCombined[0], "Damage") != 1000L ||
                (long)GetSnapshotValue(actionCombined[0], "Melee") != 2L ||
                (string)GetSnapshotValue(actionCombined[0], "Job") != "Fire IV")
            {
                throw new InvalidOperationException(
                    "The action-grouped pet contribution was not merged correctly.");
            }

            IList separateInput = CreatePetOwnershipRows(
                snapshotRowType,
                "Garuda@Nazgul");
            IList separate = (IList)applyOwnership.Invoke(
                null,
                new object[] { separateInput, dataSet, "all:petrows", "sources", "player", 10.0 });
            object separateOwner = separate.Cast<object>().Single(row =>
                (string)GetSnapshotValue(row, "CombatantType") == EntityType.Player.ToString());
            object separatePet = separate.Cast<object>().Single(row =>
                (string)GetSnapshotValue(row, "CombatantType") == EntityType.Pet.ToString());
            if (separate.Count != 2 ||
                (long)GetSnapshotValue(separateOwner, "Damage") != 700L ||
                (string)GetSnapshotValue(separatePet, "Name") != "Garuda (Nazgul)" ||
                (string)GetSnapshotValue(separatePet, "Job") != "Pet of Nazgul" ||
                (long)GetSnapshotValue(separatePet, "Damage") != 300L ||
                (string)GetSnapshotValue(separateOwner, "CriticalRate") != "Critical hit rate: 22.2%" ||
                (string)GetSnapshotValue(separatePet, "CriticalRate") != "Critical hit rate: 25.0%")
            {
                throw new InvalidOperationException(
                    "Separate pet display did not preserve both the master and pet rows.");
            }

            KPDatabaseDataSet noOwnerDataSet = new KPDatabaseDataSet();
            IList provisionalInput = CreatePetOwnershipRows(
                snapshotRowType,
                "Nazgul's Garuda");
            provisionalInput.RemoveAt(0);
            IList provisional = (IList)applyOwnership.Invoke(
                null,
                new object[] { provisionalInput, noOwnerDataSet, "all", "sources", "player", 10.0 });
            if (provisional.Count != 1 ||
                (string)GetSnapshotValue(provisional[0], "Name") != "Nazgul" ||
                (string)GetSnapshotValue(provisional[0], "CombatantType") != EntityType.Player.ToString() ||
                (long)GetSnapshotValue(provisional[0], "Damage") != 300L)
            {
                throw new InvalidOperationException(
                    "Pet-first damage was not provisionally attributed before the master acted.");
            }

            IList expandedPetInput = CreatePetOwnershipRows(
                snapshotRowType,
                "Nazgul's Garuda");
            IList expandedPetCombined = (IList)applyOwnership.Invoke(
                null,
                new object[] { expandedPetInput, dataSet, "all", "sources", "player", 10.0 });
            if (expandedPetCombined.Count != 1 ||
                (string)GetSnapshotValue(expandedPetCombined[0], "Name") != ownerName ||
                (long)GetSnapshotValue(expandedPetCombined[0], "Damage") != 1000L)
            {
                throw new InvalidOperationException(
                    "SanctumChat-expanded pet damage was not attributed to its owner.");
            }

            IList unresolvedInput = CreatePetOwnershipRows(
                snapshotRowType,
                "Garuda@00000");
            IList unresolved = (IList)applyOwnership.Invoke(
                null,
                new object[] { unresolvedInput, dataSet, "all", "sources", "player", 10.0 });
            long unresolvedTotal = 0;
            foreach (object unresolvedRow in unresolved)
                unresolvedTotal += (long)GetSnapshotValue(unresolvedRow, "Damage");
            if (unresolved.Count != 2 || unresolvedTotal != 1000L)
            {
                throw new InvalidOperationException(
                    "An unresolved owner tag changed or discarded pet damage.");
            }
        }

        private static void VerifyServerCompatibilityProfiles()
        {
            Assembly parserCore = typeof(DatabaseManager).Assembly;
            Type compatibility = parserCore.GetType(
                "WaywardGamers.KParser.Bridge.ServerCompatibility",
                true);
            MethodInfo configure = compatibility.GetMethod(
                "Configure",
                BindingFlags.Static | BindingFlags.NonPublic);
            PropertyInfo currentProfile = compatibility.GetProperty(
                "CurrentProfile",
                BindingFlags.Static | BindingFlags.NonPublic);
            PropertyInfo supportsCalculatedDots = compatibility.GetProperty(
                "SupportsCalculatedDots",
                BindingFlags.Static | BindingFlags.NonPublic);
            Type classifier = parserCore.GetType(
                "WaywardGamers.KParser.Parsing.ClassifyEntity",
                true);
            MethodInfo classify = classifier.GetMethod(
                "ClassifyByName",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (configure == null || currentProfile == null ||
                supportsCalculatedDots == null || classify == null)
                throw new MissingMemberException("Compatibility profile members were not found.");

            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "KParser-CompatibilitySmoke-" + Guid.NewGuid().ToString("N"));
            string mappingPath = Path.Combine(temporaryDirectory, "pet_mappings.tsv");
            Directory.CreateDirectory(temporaryDirectory);
            try
            {
                File.WriteAllText(
                    mappingPath,
                    "# kparserbridge-v1\n1\t123\tTestCompanion\t456\tTestowner\tpacket-entity\thigh\t2026-01-01T00:00:00Z\n");
                configure.Invoke(null, new object[] { "other", mappingPath });

                EntityType mapped = (EntityType)classify.Invoke(
                    null,
                    new object[] { "TestCompanion" });
                if (mapped != EntityType.Pet)
                    throw new InvalidOperationException("Other profile did not recognize a mapped pet.");

                EntityType sanctumAliasInOther = (EntityType)classify.Invoke(
                    null,
                    new object[] { "Garuda@Nazgul" });
                if (sanctumAliasInOther == EntityType.Pet)
                    throw new InvalidOperationException("Other profile applied Sanctum pet-name rules.");

                configure.Invoke(null, new object[] { "horizon-xi", mappingPath });
                if ((string)currentProfile.GetValue(null, null) != "horizon" ||
                    (bool)supportsCalculatedDots.GetValue(null, null) == false)
                {
                    throw new InvalidOperationException(
                        "Horizon profile did not normalize or enable standard calculated DoTs.");
                }
                EntityType horizonMapped = (EntityType)classify.Invoke(
                    null,
                    new object[] { "TestCompanion" });
                if (horizonMapped != EntityType.Pet)
                    throw new InvalidOperationException("Horizon profile did not recognize a mapped pet.");
                EntityType horizonAvatar = (EntityType)classify.Invoke(
                    null,
                    new object[] { "Garuda" });
                if (horizonAvatar != EntityType.Pet)
                    throw new InvalidOperationException("Horizon did not recognize a standard avatar pet name.");
                EntityType sanctumAliasInHorizon = (EntityType)classify.Invoke(
                    null,
                    new object[] { "Garuda@Nazgul" });
                if (sanctumAliasInHorizon == EntityType.Pet)
                    throw new InvalidOperationException("Horizon applied Sanctum pet-name rules.");

                configure.Invoke(null, new object[] { "sanctum", string.Empty });
                EntityType sanctumAlias = (EntityType)classify.Invoke(
                    null,
                    new object[] { "Garuda@Nazgul" });
                if (sanctumAlias != EntityType.Pet)
                    throw new InvalidOperationException("Sanctum profile did not restore Sanctum pet-name rules.");
            }
            finally
            {
                configure.Invoke(null, new object[] { "sanctum", string.Empty });
                Directory.Delete(temporaryDirectory, true);
            }
        }

        private static void VerifyPetFirstEncounterGate()
        {
            Assembly parserCore = typeof(DatabaseManager).Assembly;
            Type builder = parserCore.GetType(
                "WaywardGamers.KParser.Bridge.SanctumDamageSnapshotBuilder",
                true);
            MethodInfo encounterGate = builder.GetMethod(
                "HasAllianceOrOwnedPetDamage",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (encounterGate == null)
                throw new MissingMethodException(builder.FullName, "HasAllianceOrOwnedPetDamage");

            KPDatabaseDataSet data = new KPDatabaseDataSet();
            KPDatabaseDataSet.CombatantsRow pet = data.Combatants.AddCombatantsRow(
                "Nazgul's Garuda",
                (byte)EntityType.Pet,
                string.Empty);
            KPDatabaseDataSet.CombatantsRow enemy = data.Combatants.AddCombatantsRow(
                "Pet Test Target",
                (byte)EntityType.Mob,
                string.Empty);
            DateTime start = new DateTime(2026, 8, 10, 20, 0, 0, DateTimeKind.Utc);
            KPDatabaseDataSet.BattlesRow battle = data.Battles.AddBattlesRow(
                enemy,
                start,
                start.AddMinutes(1),
                false,
                pet,
                (byte)EntityType.Pet,
                0,
                0,
                (byte)MobDifficulty.EvenMatch,
                false);
            data.Interactions.AddInteractionsRow(
                start.AddSeconds(1), pet, enemy, battle,
                (byte)ActorPlayerType.Other, false, null, (byte)ActionType.Melee,
                (byte)FailedActionType.None, (byte)DefenseType.None, 0,
                (byte)AidType.None, (byte)RecoveryType.None, (byte)HarmType.Damage, 250,
                (byte)DamageModifier.None, (byte)AidType.None, (byte)RecoveryType.None,
                (byte)HarmType.None, 0, null, null);

            bool included = (bool)encounterGate.Invoke(null, new object[] { battle });
            if (!included)
                throw new InvalidOperationException("An owned pet's opening damage did not qualify the encounter.");

            pet.CombatantName = "UnmappedNearbyPet";
            bool unrelatedIncluded = (bool)encounterGate.Invoke(null, new object[] { battle });
            if (unrelatedIncluded)
                throw new InvalidOperationException("An unmapped outside pet incorrectly qualified the encounter.");
        }

        private static void VerifyCriticalRateDenominators()
        {
            Assembly parserCore = typeof(DatabaseManager).Assembly;
            Type builder = parserCore.GetType(
                "WaywardGamers.KParser.Bridge.SanctumDamageSnapshotBuilder",
                true);
            MethodInfo categoryRate = builder.GetMethod(
                "GetDamageCategoryCriticalRate",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo combinedRate = builder.GetMethod(
                "GetCriticalRate",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo criticalReport = builder.GetMethod(
                "BuildCriticalHits",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo mergePetDamage = builder.GetMethod(
                "MergeSanctumPetDamage",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (categoryRate == null || combinedRate == null ||
                criticalReport == null || mergePetDamage == null)
                throw new MissingMemberException("Critical-rate helpers could not be tested.");

            KPDatabaseDataSet data = new KPDatabaseDataSet();
            KPDatabaseDataSet.CombatantsRow player = data.Combatants.AddCombatantsRow(
                "Critical Tester",
                (byte)EntityType.Player,
                string.Empty);
            KPDatabaseDataSet.CombatantsRow enemy = data.Combatants.AddCombatantsRow(
                "Critical Target",
                (byte)EntityType.Mob,
                string.Empty);
            DateTime start = new DateTime(2026, 8, 10, 21, 0, 0, DateTimeKind.Utc);
            KPDatabaseDataSet.BattlesRow battle = data.Battles.AddBattlesRow(
                enemy,
                start,
                start.AddSeconds(30),
                true,
                player,
                (byte)EntityType.Player,
                0,
                0,
                (byte)MobDifficulty.EvenMatch,
                false);

            var allRows = new List<KPDatabaseDataSet.InteractionsRow>();
            VerifyCriticalCategory(
                data, battle, player, enemy, start, ActionType.Melee, "melee", categoryRate, allRows);
            VerifyCriticalCategory(
                data, battle, player, enemy, start, ActionType.Ranged, "ranged", categoryRate, allRows);
            VerifyCriticalCategory(
                data, battle, player, enemy, start, ActionType.Weaponskill, "weaponskills", categoryRate, allRows);

            string combined = (string)combinedRate.Invoke(null, new object[] { allRows });
            if (combined != "Critical hit rate: 33.3%")
            {
                throw new InvalidOperationException(
                    "Combined critical rate did not use successful melee/ranged hits: " + combined);
            }

            var enemyIds = new Dictionary<int, int>
            {
                { battle.BattleID, enemy.CombatantID }
            };
            IList criticalRows = (IList)criticalReport.Invoke(
                null,
                new object[] { allRows, enemyIds, "all" });
            if (criticalRows.Count != 1)
                throw new InvalidOperationException("Critical-hit report did not return its physical combatant.");
            object criticalRow = criticalRows[0];
            if ((long)criticalRow.GetType().GetProperty("Damage").GetValue(criticalRow, null) != 200L ||
                (long)criticalRow.GetType().GetProperty("Melee").GetValue(criticalRow, null) != 2L ||
                (long)criticalRow.GetType().GetProperty("WeaponSkills").GetValue(criticalRow, null) != 100L ||
                (long)criticalRow.GetType().GetProperty("Magic").GetValue(criticalRow, null) != 100L ||
                (long)criticalRow.GetType().GetProperty("Other").GetValue(criticalRow, null) != 100L ||
                Math.Abs((double)criticalRow.GetType().GetProperty("Dps").GetValue(criticalRow, null) - 33.3333) > 0.01)
            {
                throw new InvalidOperationException("Critical-hit high/low/average/rate aggregation is invalid.");
            }

            object petCriticalRow = Activator.CreateInstance(criticalRow.GetType(), true);
            SetSnapshotValue(petCriticalRow, "Damage", 300L);
            SetSnapshotValue(petCriticalRow, "Melee", 2L);
            SetSnapshotValue(petCriticalRow, "WeaponSkills", 250L);
            SetSnapshotValue(petCriticalRow, "Magic", 50L);
            SetSnapshotValue(petCriticalRow, "Other", 150L);
            SetSnapshotValue(petCriticalRow, "PhysicalAttempts", 5L);
            SetSnapshotValue(petCriticalRow, "PhysicalHits", 4L);
            SetSnapshotValue(petCriticalRow, "PhysicalMisses", 1L);
            SetSnapshotValue(petCriticalRow, "CriticalHits", 2L);
            SetSnapshotValue(petCriticalRow, "TopAction", "Pet critical test");
            mergePetDamage.Invoke(
                null,
                new object[] { criticalRow, petCriticalRow, "criticals", "player", 30.0 });
            if ((long)criticalRow.GetType().GetProperty("Damage").GetValue(criticalRow, null) != 500L ||
                (long)criticalRow.GetType().GetProperty("Melee").GetValue(criticalRow, null) != 4L ||
                (long)criticalRow.GetType().GetProperty("WeaponSkills").GetValue(criticalRow, null) != 250L ||
                (long)criticalRow.GetType().GetProperty("Magic").GetValue(criticalRow, null) != 50L ||
                (long)criticalRow.GetType().GetProperty("Other").GetValue(criticalRow, null) != 125L ||
                Math.Abs((double)criticalRow.GetType().GetProperty("Dps").GetValue(criticalRow, null) - 40.0) > 0.01)
            {
                throw new InvalidOperationException("Owned-pet critical-hit aggregation is invalid.");
            }
        }

        private static void VerifyCriticalCategory(
            KPDatabaseDataSet data,
            KPDatabaseDataSet.BattlesRow battle,
            KPDatabaseDataSet.CombatantsRow actor,
            KPDatabaseDataSet.CombatantsRow target,
            DateTime timestamp,
            ActionType actionType,
            string displayMode,
            MethodInfo categoryRate,
            ICollection<KPDatabaseDataSet.InteractionsRow> allRows)
        {
            var rows = new List<KPDatabaseDataSet.InteractionsRow>
            {
                AddPhysicalTestInteraction(data, battle, actor, target, timestamp, actionType, 100, DamageModifier.Critical),
                AddPhysicalTestInteraction(data, battle, actor, target, timestamp.AddSeconds(1), actionType, 90, DamageModifier.None),
                AddPhysicalTestInteraction(data, battle, actor, target, timestamp.AddSeconds(2), actionType, 80, DamageModifier.None),
                AddPhysicalTestInteraction(data, battle, actor, target, timestamp.AddSeconds(3), actionType, 0, DamageModifier.Critical)
            };
            foreach (var row in rows)
                allRows.Add(row);

            string rate = (string)categoryRate.Invoke(null, new object[] { rows, displayMode });
            if (rate != "Critical rate: 33.3%")
            {
                throw new InvalidOperationException(
                    displayMode + " critical rate counted misses or used the wrong denominator: " + rate);
            }
        }

        private static KPDatabaseDataSet.InteractionsRow AddPhysicalTestInteraction(
            KPDatabaseDataSet data,
            KPDatabaseDataSet.BattlesRow battle,
            KPDatabaseDataSet.CombatantsRow actor,
            KPDatabaseDataSet.CombatantsRow target,
            DateTime timestamp,
            ActionType actionType,
            int damage,
            DamageModifier modifier)
        {
            string actionName = actionType.ToString();
            KPDatabaseDataSet.ActionsRow action = data.Actions
                .FirstOrDefault(row => row.ActionName == actionName) ??
                data.Actions.AddActionsRow(actionName);
            return data.Interactions.AddInteractionsRow(
                timestamp, actor, target, battle, (byte)ActorPlayerType.Self, false, action,
                (byte)actionType, (byte)FailedActionType.None,
                (byte)(damage == 0 ? DefenseType.Evasion : DefenseType.None),
                0, (byte)AidType.None, (byte)RecoveryType.None,
                (byte)(damage == 0 ? HarmType.None : HarmType.Damage),
                damage, (byte)modifier, (byte)AidType.None, (byte)RecoveryType.None,
                (byte)HarmType.None, 0, null, null);
        }

        private static IList CreatePetOwnershipRows(Type snapshotRowType, string petName)
        {
            Type listType = typeof(List<>).MakeGenericType(snapshotRowType);
            IList rows = (IList)Activator.CreateInstance(listType);
            object player = Activator.CreateInstance(snapshotRowType, true);
            SetSnapshotValue(player, "Key", "1");
            SetSnapshotValue(player, "Name", "Nazgul");
            SetSnapshotValue(player, "Job", "SMN");
            SetSnapshotValue(player, "CombatantType", EntityType.Player.ToString());
            SetSnapshotValue(player, "Damage", 700L);
            SetSnapshotValue(player, "Melee", 500L);
            SetSnapshotValue(player, "WeaponSkills", 100L);
            SetSnapshotValue(player, "Magic", 100L);
            SetSnapshotValue(player, "PhysicalAttempts", 10L);
            SetSnapshotValue(player, "PhysicalHits", 9L);
            SetSnapshotValue(player, "PhysicalMisses", 1L);
            SetSnapshotValue(player, "CriticalHits", 2L);
            SetSnapshotValue(player, "Accuracy", "Accuracy: 90.0%");
            SetSnapshotValue(player, "CriticalRate", "Critical hit rate: 22.2%");
            SetSnapshotValue(player, "TopAction", "Top action: Predator Claws");

            object pet = Activator.CreateInstance(snapshotRowType, true);
            SetSnapshotValue(pet, "Key", "2");
            SetSnapshotValue(pet, "Name", petName);
            SetSnapshotValue(pet, "Job", "Pet");
            SetSnapshotValue(pet, "CombatantType", EntityType.Pet.ToString());
            SetSnapshotValue(pet, "Damage", 300L);
            SetSnapshotValue(pet, "Melee", 200L);
            SetSnapshotValue(pet, "WeaponSkills", 100L);
            SetSnapshotValue(pet, "PhysicalAttempts", 5L);
            SetSnapshotValue(pet, "PhysicalHits", 4L);
            SetSnapshotValue(pet, "PhysicalMisses", 1L);
            SetSnapshotValue(pet, "CriticalHits", 1L);
            SetSnapshotValue(pet, "Accuracy", "Accuracy: 80.0%");
            SetSnapshotValue(pet, "CriticalRate", "Critical hit rate: 25.0%");
            SetSnapshotValue(pet, "TopAction", "Top action: Burning Strike");

            rows.Add(player);
            rows.Add(pet);
            return rows;
        }

        private static object GetSnapshotValue(object row, string propertyName)
        {
            return row.GetType().GetProperty(propertyName).GetValue(row, null);
        }

        private static void SetSnapshotValue(object row, string propertyName, object value)
        {
            row.GetType().GetProperty(propertyName).SetValue(row, value, null);
        }

        private static void VerifyRamReaderStopsBeforeReturning()
        {
            Assembly parserCore = typeof(DatabaseManager).Assembly;
            Type readerType = parserCore.GetType(
                "WaywardGamers.KParser.Monitoring.RamReader",
                true);
            PropertyInfo instanceProperty = readerType.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo threadField = readerType.GetField(
                "readerThread",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo abortField = readerType.GetField(
                "abortMonitorThread",
                BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo runningProperty = readerType.BaseType.GetProperty(
                "IsRunning",
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo stopMethod = readerType.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo startMethod = readerType.GetMethod(
                "Start",
                BindingFlags.Instance | BindingFlags.Public);

            if (instanceProperty == null || threadField == null ||
                abortField == null || runningProperty == null ||
                stopMethod == null || startMethod == null)
            {
                throw new MissingMemberException(
                    "The RAM reader lifecycle members could not be tested.");
            }

            object reader = instanceProperty.GetValue(null, null);
            ManualResetEvent abort = abortField.GetValue(reader) as ManualResetEvent;
            if (reader == null || abort == null)
                throw new InvalidOperationException("The RAM reader test could not initialize.");

            ManualResetEvent workerReady = new ManualResetEvent(false);
            abort.Reset();
            Thread worker = new Thread(() =>
            {
                workerReady.Set();
                abort.WaitOne();
                Thread.Sleep(250);
            });
            worker.IsBackground = true;
            worker.Start();
            if (workerReady.WaitOne(1000) == false)
                throw new InvalidOperationException("The RAM reader test worker did not start.");

            threadField.SetValue(reader, worker);
            runningProperty.SetValue(reader, true, null);
            stopMethod.Invoke(reader, null);

            if (worker.IsAlive)
                throw new InvalidOperationException(
                    "The RAM reader returned from Stop while its worker was still alive.");
            if ((bool)runningProperty.GetValue(reader, null))
                throw new InvalidOperationException(
                    "The RAM reader remained marked as running after Stop.");
            if (threadField.GetValue(reader) != null)
                throw new InvalidOperationException(
                    "The RAM reader retained a stopped worker reference.");

            ManualResetEvent releaseBlockedWorker = new ManualResetEvent(false);
            Thread blockedWorker = new Thread(() => releaseBlockedWorker.WaitOne());
            blockedWorker.IsBackground = true;
            blockedWorker.Start();
            threadField.SetValue(reader, blockedWorker);
            runningProperty.SetValue(reader, false, null);
            try
            {
                startMethod.Invoke(reader, null);
                throw new InvalidOperationException(
                    "The RAM reader allowed a second worker to start.");
            }
            catch (TargetInvocationException ex)
            {
                if (!(ex.InnerException is InvalidOperationException))
                    throw;
            }
            finally
            {
                releaseBlockedWorker.Set();
                blockedWorker.Join(1000);
                threadField.SetValue(reader, null);
                runningProperty.SetValue(reader, false, null);
                abort.Set();
            }
        }

        private static int AuditDatabase(string databasePath)
        {
            ConfigureWritableDefaultDirectory(databasePath);
            DatabaseManager.Instance.OpenDatabase(databasePath);
            try
            {
                MethodInfo getDatabase = typeof(DatabaseManager).GetMethod(
                    "GetDatabaseForReading",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo doneReading = typeof(DatabaseManager).GetMethod(
                    "DoneReadingDatabase",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (getDatabase == null || doneReading == null)
                    throw new MissingMethodException("Database read-lock methods are unavailable.");

                KPDatabaseDataSet data = getDatabase.Invoke(
                    DatabaseManager.Instance,
                    null) as KPDatabaseDataSet;
                if (data == null)
                    throw new InvalidOperationException("The database could not be read for auditing.");

                try
                {
                    var groups = data.Interactions
                        .GroupBy(BuildExactInteractionKey)
                        .Where(group => group.Count() > 1)
                        .OrderByDescending(group => group.Count())
                        .ThenBy(group => group.First().Timestamp)
                        .ToArray();
                    int duplicateRows = groups.Sum(group => group.Count() - 1);
                    foreach (System.Data.DataTable table in data.Tables)
                    {
                        Console.WriteLine(string.Format(
                            "table={0} rows={1} columns={2}",
                            table.TableName,
                            table.Rows.Count,
                            string.Join(",", table.Columns.Cast<System.Data.DataColumn>()
                                .Select(column => column.ColumnName).ToArray())));
                    }
                    Console.WriteLine("interactions=" + data.Interactions.Count);
                    Console.WriteLine("exact-duplicate-groups=" + groups.Length);
                    Console.WriteLine("exact-extra-rows=" + duplicateRows);

                    System.Data.DataTable recordLog = data.Tables["RecordLog"];
                    var recordGroups = recordLog.Rows.Cast<System.Data.DataRow>()
                        .GroupBy(row => BuildExactRowKey(row, "RecordLogID"))
                        .Where(group => group.Count() > 1)
                        .OrderBy(group => group.First()["Timestamp"])
                        .ToArray();
                    Console.WriteLine("recordlog-duplicate-groups=" + recordGroups.Length);
                    Console.WriteLine("recordlog-extra-rows=" +
                        recordGroups.Sum(group => group.Count() - 1));
                    var repeatedEvents = recordLog.Rows.Cast<System.Data.DataRow>()
                        .GroupBy(row => row["MessageText"].ToString())
                        .Where(group => group.Count() > 1)
                        .OrderBy(group => group.First()["Timestamp"])
                        .ToArray();
                    Console.WriteLine("recordlog-repeated-event-groups=" + repeatedEvents.Length);
                    Console.WriteLine("recordlog-repeated-event-rows=" +
                        repeatedEvents.Sum(group => group.Count() - 1));
                    foreach (System.Data.DataRow row in recordLog.Rows)
                    {
                        Console.WriteLine(string.Format(
                            "raw id={0} time={1:o} parsed={2} text={3}",
                            row["RecordLogID"],
                            (DateTime)row["Timestamp"],
                            row["ParseSuccessful"],
                            row["MessageText"]));
                    }
                    foreach (var group in recordGroups.Take(12))
                    {
                        System.Data.DataRow row = group.First();
                        Console.WriteLine(string.Format(
                            "rawdup x{0} time={1:o} text={2}",
                            group.Count(),
                            (DateTime)row["Timestamp"],
                            row["MessageText"]));
                    }

                    foreach (var group in groups.Take(30))
                    {
                        KPDatabaseDataSet.InteractionsRow row = group.First();
                        string actor = row.IsActorIDNull() || row.CombatantsRowByActorCombatantRelation == null
                            ? "-"
                            : row.CombatantsRowByActorCombatantRelation.CombatantName;
                        string target = row.IsTargetIDNull() || row.CombatantsRowByTargetCombatantRelation == null
                            ? "-"
                            : row.CombatantsRowByTargetCombatantRelation.CombatantName;
                        string action = row.IsActionIDNull() || row.ActionsRow == null
                            ? ((ActionType)row.ActionType).ToString()
                            : row.ActionsRow.ActionName;
                        Console.WriteLine(string.Format(
                            "dup x{0} ids={1} time={2:o} actor={3} target={4} action={5} amount={6} harm={7} defense={8}",
                            group.Count(),
                            string.Join(",", group.Select(item => item.InteractionID.ToString()).ToArray()),
                            row.Timestamp,
                            actor,
                            target,
                            action,
                            row.Amount,
                            (HarmType)row.HarmType,
                            (DefenseType)row.DefenseType));
                    }

                    foreach (var actorGroup in data.Interactions
                        .Where(row => row.IsActorIDNull() == false &&
                                      (HarmType)row.HarmType == HarmType.Damage)
                        .GroupBy(row => row.CombatantsRowByActorCombatantRelation.CombatantName)
                        .OrderByDescending(group => group.Sum(row => (long)row.Amount)))
                    {
                        Console.WriteLine(string.Format(
                            "damage actor={0} rows={1} amount={2}",
                            actorGroup.Key,
                            actorGroup.Count(),
                            actorGroup.Sum(row => (long)row.Amount)));
                    }
                }
                finally
                {
                    doneReading.Invoke(DatabaseManager.Instance, null);
                }

                return 0;
            }
            finally
            {
                DatabaseManager.Instance.CloseDatabase();
            }
        }

        private static string BuildExactInteractionKey(KPDatabaseDataSet.InteractionsRow row)
        {
            return BuildExactRowKey(row, "InteractionID");
        }

        private static string BuildExactRowKey(
            System.Data.DataRow row,
            string identityColumn)
        {
            return string.Join("|", row.Table.Columns
                .Cast<System.Data.DataColumn>()
                .Where(column => column.ColumnName != identityColumn)
                .Select(column =>
                {
                    object value = row[column];
                    DateTime? date = value is DateTime ? (DateTime?)value : null;
                    return date.HasValue
                        ? date.Value.Ticks.ToString()
                        : value == null || value == DBNull.Value
                            ? "<null>"
                            : value.ToString();
                })
                .ToArray());
        }

        private static void ConfigureWritableDefaultDirectory(string databasePath)
        {
            Assembly parserCore = typeof(DatabaseManager).Assembly;
            Type settingsType = parserCore.GetType(
                "WaywardGamers.KParser.Properties.Settings",
                true);
            object settings = settingsType.GetProperty(
                "Default",
                BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
            settingsType.GetProperty("DefaultParseSaveDirectory").SetValue(
                settings,
                Path.GetDirectoryName(Path.GetFullPath(databasePath)),
                null);
        }

        private static int VerifyReport(MethodInfo build, string report, string[] displays)
        {
            int verified = 0;
            foreach (string display in displays)
            {
                object player = Build(build, report, display, "player");
                object action = Build(build, report, display, "action");
                long playerTotal = GetEncounterTotal(player);
                long actionTotal = GetEncounterTotal(action);
                if (playerTotal == 0 && actionTotal == 0)
                    continue;
                if (playerTotal != actionTotal)
                {
                    throw new InvalidOperationException(string.Format(
                        "{0}/{1} totals differ: player={2}, action={3}",
                        report,
                        display,
                        playerTotal,
                        actionTotal));
                }

                ValidateActionRows(action);
                Console.WriteLine("display=" + report + "/" + display);
                verified++;
            }
            return verified;
        }

        private static object Build(
            MethodInfo build,
            string report,
            string display,
            string group)
        {
            return build.Invoke(null, new object[]
            {
                "all", 0, null, report, "all", display, group, string.Empty, false
            });
        }

        private static void VerifyChatReport(MethodInfo build)
        {
            object snapshot = Build(build, "chat", "all", "player");
            string report = (string)snapshot.GetType().GetProperty("Report").GetValue(snapshot, null);
            string display = (string)snapshot.GetType().GetProperty("DisplayMode").GetValue(snapshot, null);
            object columns = snapshot.GetType().GetProperty("Columns").GetValue(snapshot, null);
            string messageColumn = (string)columns.GetType().GetProperty("Detail1").GetValue(columns, null);
            if (report != "chat" || display != "all" || messageColumn != "Message")
                throw new InvalidOperationException("The Chat report metadata is invalid.");

            foreach (object row in (IEnumerable)snapshot.GetType().GetProperty("Combatants").GetValue(snapshot, null))
            {
                string key = (string)row.GetType().GetProperty("Key").GetValue(row, null);
                string message = (string)row.GetType().GetProperty("Detail1Text").GetValue(row, null);
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(message))
                    throw new InvalidOperationException("A Chat row is missing its key or message.");
            }
        }

        private static void VerifyLootReports(MethodInfo build)
        {
            foreach (string display in new[] { "summary", "distribution", "rates", "treasurehunter", "itemsused", "helm" })
            {
                object snapshot = Build(build, "loot", display, "player");
                string report = (string)snapshot.GetType().GetProperty("Report").GetValue(snapshot, null);
                string actualDisplay = (string)snapshot.GetType().GetProperty("DisplayMode").GetValue(snapshot, null);
                object columns = snapshot.GetType().GetProperty("Columns").GetValue(snapshot, null);
                string primary = (string)columns.GetType().GetProperty("Primary").GetValue(columns, null);
                if (report != "loot" || actualDisplay != display || string.IsNullOrEmpty(primary))
                    throw new InvalidOperationException("The Item Drops report metadata is invalid for " + display + ".");
            }

            object allLoot = Build(build, "loot", "distribution", "player");
            IEnumerable filters = (IEnumerable)allLoot.GetType()
                .GetProperty("CombatantFilters").GetValue(allLoot, null);
            string recipientKey = null;
            foreach (object filter in filters)
            {
                string key = (string)filter.GetType().GetProperty("Key").GetValue(filter, null);
                if (key.StartsWith("recipient:", StringComparison.Ordinal))
                {
                    recipientKey = key;
                    break;
                }
            }

            if (recipientKey != null)
            {
                object filtered = build.Invoke(null, new object[]
                {
                    "all", 0, null, "loot", recipientKey, "distribution", "player", string.Empty, false
                });
                string expectedRecipient = recipientKey.Substring("recipient:".Length);
                foreach (object row in (IEnumerable)filtered.GetType()
                    .GetProperty("Combatants").GetValue(filtered, null))
                {
                    string actualRecipient = (string)row.GetType().GetProperty("Name").GetValue(row, null);
                    if (!string.Equals(expectedRecipient, actualRecipient, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The Item Drops recipient filter leaked another recipient.");
                }
            }
        }

        private static void VerifyAdvancedReports(MethodInfo build)
        {
            string[][] reports =
            {
                new[] { "damageDealt", "criticals" },
                new[] { "damageDealt", "multiattacks" },
                new[] { "damageDealt", "timeline" },
                new[] { "damageDealt", "wsrates" },
                new[] { "damageTaken", "buffperformance" },
                new[] { "healing", "recipients" },
                new[] { "healing", "recovery" },
                new[] { "healing", "efficiency" },
                new[] { "buffs", "performance" },
                new[] { "buffs", "corsair" },
                new[] { "experience", "mobs" },
                new[] { "experience", "history" },
                new[] { "experience", "chains" },
                new[] { "experience", "difficulty" }
            };
            foreach (string[] item in reports)
            {
                object snapshot = Build(build, item[0], item[1], "player");
                string report = (string)snapshot.GetType().GetProperty("Report").GetValue(snapshot, null);
                string display = (string)snapshot.GetType().GetProperty("DisplayMode").GetValue(snapshot, null);
                object columns = snapshot.GetType().GetProperty("Columns").GetValue(snapshot, null);
                string primary = (string)columns.GetType().GetProperty("Primary").GetValue(columns, null);
                string error = (string)snapshot.GetType().GetProperty("Error").GetValue(snapshot, null);
                if (report != item[0] || display != item[1] || string.IsNullOrEmpty(primary) ||
                    string.IsNullOrEmpty(error) == false)
                {
                    throw new InvalidOperationException(
                        "Current preview report failed metadata/runtime verification: " + item[0] + "/" + item[1] +
                        (string.IsNullOrEmpty(error) ? string.Empty : " - " + error));
                }
            }
        }

        private static void VerifyLegacyParityReports(MethodInfo build)
        {
            object timeline = Build(build, "damageDealt", "timeline", "player");
            object timelineColumns = timeline.GetType().GetProperty("Columns").GetValue(timeline, null);
            if ((string)timelineColumns.GetType().GetProperty("Primary").GetValue(timelineColumns, null) != "Interval damage")
                throw new InvalidOperationException("Damage timeline columns were not applied.");

            int previousRank = 0;
            long intervalDamage = 0;
            int intervalCount = 0;
            foreach (object row in (IEnumerable)timeline.GetType().GetProperty("Combatants").GetValue(timeline, null))
            {
                int rank = (int)row.GetType().GetProperty("Rank").GetValue(row, null);
                if (rank <= previousRank)
                    throw new InvalidOperationException("Damage timeline intervals are not chronological.");
                previousRank = rank;
                intervalDamage += (long)row.GetType().GetProperty("Damage").GetValue(row, null);
                intervalCount++;
            }
            if (intervalCount > 72)
                throw new InvalidOperationException("Damage timeline exceeded its bounded interval count.");
            if (intervalDamage != GetEncounterTotal(timeline))
                throw new InvalidOperationException("Damage timeline interval totals do not match the encounter total.");

            object wsRates = Build(build, "damageDealt", "wsrates", "player");
            object wsColumns = wsRates.GetType().GetProperty("Columns").GetValue(wsRates, null);
            if ((string)wsColumns.GetType().GetProperty("Rate").GetValue(wsColumns, null) != "Avg interval" ||
                (string)wsColumns.GetType().GetProperty("Detail3").GetValue(wsColumns, null) != "Median attacks")
            {
                throw new InvalidOperationException("WS / TP cycle metadata is invalid.");
            }
            int wsRatePlayers = 0;
            foreach (object row in (IEnumerable)wsRates.GetType().GetProperty("Combatants").GetValue(wsRates, null))
            {
                if ((long)row.GetType().GetProperty("Damage").GetValue(row, null) <= 0 ||
                    string.IsNullOrEmpty((string)row.GetType().GetProperty("Detail1Text").GetValue(row, null)))
                {
                    throw new InvalidOperationException("A WS / TP cycle row has invalid values.");
                }
                wsRatePlayers++;
            }

            object multiAttacks = Build(build, "damageDealt", "multiattacks", "player");
            object multiColumns = multiAttacks.GetType().GetProperty("Columns").GetValue(multiAttacks, null);
            if ((string)multiColumns.GetType().GetProperty("Detail2").GetValue(multiColumns, null) != "Extra attacks" ||
                (string)multiColumns.GetType().GetProperty("Detail3").GetValue(multiColumns, null) != "Zanshin candidates")
            {
                throw new InvalidOperationException("Expanded multi-attack metadata is invalid.");
            }
            int multiAttackPlayers = ((IEnumerable)multiAttacks.GetType()
                .GetProperty("Combatants").GetValue(multiAttacks, null)).Cast<object>().Count();

            object itemUsage = Build(build, "loot", "itemsused", "player");
            object itemColumns = itemUsage.GetType().GetProperty("Columns").GetValue(itemUsage, null);
            if ((string)itemColumns.GetType().GetProperty("Secondary").GetValue(itemColumns, null) != "Item" ||
                (string)itemColumns.GetType().GetProperty("Primary").GetValue(itemColumns, null) != "Uses")
            {
                throw new InvalidOperationException("Consumable item-usage metadata is invalid.");
            }
            int itemUsageRows = ((IEnumerable)itemUsage.GetType()
                .GetProperty("Combatants").GetValue(itemUsage, null)).Cast<object>().Count();
            VerifySyntheticItemUsage(build.DeclaringType);
            Console.WriteLine("timeline-intervals=" + intervalCount);
            Console.WriteLine("ws-rate-players=" + wsRatePlayers);
            Console.WriteLine("multi-attack-players=" + multiAttackPlayers);
            Console.WriteLine("item-usage-rows=" + itemUsageRows);
        }

        private static void VerifySyntheticItemUsage(Type builderType)
        {
            MethodInfo itemUsageBuilder = builderType.GetMethod(
                "BuildItemUsage",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (itemUsageBuilder == null)
                throw new MissingMethodException(builderType.FullName, "BuildItemUsage");

            KPDatabaseDataSet data = new KPDatabaseDataSet();
            KPDatabaseDataSet.CombatantsRow player = data.Combatants.AddCombatantsRow(
                "ItemTester",
                (byte)EntityType.Player,
                "[WAR99]");
            KPDatabaseDataSet.CombatantsRow enemy = data.Combatants.AddCombatantsRow(
                "Training Dummy",
                (byte)EntityType.Mob,
                null);
            DateTime start = new DateTime(2026, 8, 10, 20, 0, 0, DateTimeKind.Utc);
            KPDatabaseDataSet.BattlesRow battle = data.Battles.AddBattlesRow(
                enemy,
                start,
                start.AddMinutes(1),
                true,
                player,
                (byte)EntityType.Player,
                0,
                0,
                (byte)MobDifficulty.EvenMatch,
                false);
            KPDatabaseDataSet.ItemsRow item = data.Items.AddItemsRow("Sole Sushi");
            for (int index = 0; index < 2; index++)
            {
                data.Interactions.AddInteractionsRow(
                    start.AddSeconds(index * 20), player, player, battle,
                    (byte)ActorType.Self, false, null, (byte)ActionType.Unknown,
                    (byte)FailedActionType.None, (byte)DefenseType.None, 0,
                    (byte)AidType.Item, (byte)RecoveryType.None, (byte)HarmType.None, 0,
                    (byte)DamageModifier.None, (byte)AidType.None, (byte)RecoveryType.None,
                    (byte)HarmType.None, 0, null, item);
            }

            object result = itemUsageBuilder.Invoke(null, new object[]
            {
                data.Interactions.Cast<KPDatabaseDataSet.InteractionsRow>(),
                "all",
                string.Empty,
                1
            });
            object row = ((IEnumerable)result).Cast<object>().Single();
            if ((string)row.GetType().GetProperty("Name").GetValue(row, null) != "ItemTester" ||
                (string)row.GetType().GetProperty("Job").GetValue(row, null) != "Sole Sushi" ||
                (long)row.GetType().GetProperty("Damage").GetValue(row, null) != 2L)
            {
                throw new InvalidOperationException("Synthetic consumable item-use aggregation failed.");
            }
        }

        private static void VerifyBuffUptimeReport(MethodInfo build)
        {
            object snapshot = Build(build, "buffs", "uptime", "action");
            string display = (string)snapshot.GetType().GetProperty("DisplayMode").GetValue(snapshot, null);
            string group = (string)snapshot.GetType().GetProperty("GroupMode").GetValue(snapshot, null);
            object columns = snapshot.GetType().GetProperty("Columns").GetValue(snapshot, null);
            string primary = (string)columns.GetType().GetProperty("Primary").GetValue(columns, null);
            string rate = (string)columns.GetType().GetProperty("Rate").GetValue(columns, null);
            if (display != "uptime" || group != "action" ||
                primary != "Active time" || rate != "Uptime")
            {
                throw new InvalidOperationException("Buff duration/uptime report metadata is invalid.");
            }
        }

        private static void VerifyCraftingReport(MethodInfo build)
        {
            foreach (string expectedDisplay in new[] { "mine", "summary", "history", "skillups", "materials" })
            {
                object snapshot = Build(build, "crafting", expectedDisplay, "player");
                string report = (string)snapshot.GetType().GetProperty("Report").GetValue(snapshot, null);
                string display = (string)snapshot.GetType().GetProperty("DisplayMode").GetValue(snapshot, null);
                object columns = snapshot.GetType().GetProperty("Columns").GetValue(snapshot, null);
                string primary = (string)columns.GetType().GetProperty("Primary").GetValue(columns, null);
                if (report != "crafting" || display != expectedDisplay || string.IsNullOrEmpty(primary))
                    throw new InvalidOperationException("The Crafting report metadata is invalid for " + expectedDisplay + ".");
            }

            Type tracker = typeof(DatabaseManager).Assembly.GetType(
                "WaywardGamers.KParser.Bridge.SanctumCraftingTracker",
                true);
            MethodInfo parse = tracker.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo createSessions = tracker.GetMethod("CreateSessions", BindingFlags.Static | BindingFlags.NonPublic);
            if (parse == null || createSessions == null)
                throw new MissingMethodException("Crafting tracker parser methods are unavailable.");

            KPDatabaseDataSet sample = new KPDatabaseDataSet();
            DateTime start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
            sample.RecordLog.AddRecordLogRow(start,
                "79,00,00\x1e[12:00:00]\x1e You synthesize 12 bronze ingots.\u007f1", false);
            sample.RecordLog.AddRecordLogRow(start.AddSeconds(1),
                "79,00,00\x1e[12:00:01]\x1e Your smithing skill rises 0.1 points.\u007f1", false);
            sample.RecordLog.AddRecordLogRow(start.AddMinutes(1),
                "79,00,00\x1e[12:01:00]\x1e The synthesis fails. You lose an iron ore.\u007f1", false);
            sample.RecordLog.AddRecordLogRow(start.AddHours(1),
                "79,00,00\x1e[13:00:00]\x1e You synthesize a cursed hauberk +1.\u007f1", false);

            IList attempts = (IList)parse.Invoke(null, new object[] { sample.RecordLog });
            if (attempts.Count != 3)
                throw new InvalidOperationException("The Crafting parser did not identify all sample attempts.");

            object first = attempts[0];
            object second = attempts[1];
            object third = attempts[2];
            if (!(bool)GetInternalProperty(first, "Success") ||
                (int)GetInternalProperty(first, "Yield") != 12 ||
                (string)GetInternalProperty(first, "SkillName") != "Smithing" ||
                (double)GetInternalProperty(first, "SkillGain") != 0.1)
            {
                throw new InvalidOperationException("The Crafting success/yield/skill-up parser is invalid.");
            }
            if ((bool)GetInternalProperty(second, "Success") ||
                ((IList)GetInternalProperty(second, "LostMaterials")).Count != 1)
            {
                throw new InvalidOperationException("The Crafting break/material-loss parser is invalid.");
            }
            if (!(bool)GetInternalProperty(third, "HighQuality"))
                throw new InvalidOperationException("The Crafting HQ parser is invalid.");

            IList sessions = (IList)createSessions.Invoke(null, new object[] { attempts });
            if (sessions.Count != 2)
                throw new InvalidOperationException("The Crafting session splitter is invalid.");

            Console.WriteLine("crafting-reports=verified");
        }

        private static object GetInternalProperty(object instance, string propertyName)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(instance.GetType().FullName, propertyName);
            return property.GetValue(instance, null);
        }

        private static long GetEncounterTotal(object snapshot)
        {
            object encounter = snapshot.GetType().GetProperty("Encounter").GetValue(snapshot, null);
            return encounter == null
                ? 0
                : (long)encounter.GetType().GetProperty("TotalDamage").GetValue(encounter, null);
        }

        private static void ValidateActionRows(object snapshot)
        {
            string groupMode = (string)snapshot.GetType().GetProperty("GroupMode").GetValue(snapshot, null);
            if (groupMode != "action")
                throw new InvalidOperationException("The bridge did not retain action grouping.");

            object columns = snapshot.GetType().GetProperty("Columns").GetValue(snapshot, null);
            string secondary = (string)columns.GetType().GetProperty("Secondary").GetValue(columns, null);
            if (string.IsNullOrEmpty(secondary) || secondary == "Job")
                throw new InvalidOperationException("The action column metadata is missing.");

            int rowCount = 0;
            foreach (object row in (IEnumerable)snapshot.GetType().GetProperty("Combatants").GetValue(snapshot, null))
            {
                rowCount++;
                string key = (string)row.GetType().GetProperty("Key").GetValue(row, null);
                string action = (string)row.GetType().GetProperty("Job").GetValue(row, null);
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(action))
                    throw new InvalidOperationException("An action row is missing its stable key or action name.");
            }

            if (rowCount == 0)
                throw new InvalidOperationException("The action report returned no rows.");
        }

        private static void VerifyDotReport(MethodInfo build)
        {
            object player = Build(build, "damageDealt", "dots", "player");
            object action = Build(build, "damageDealt", "dots", "action");

            string display = (string)player.GetType().GetProperty("DisplayMode").GetValue(player, null);
            if (display != "dots")
                throw new InvalidOperationException("The bridge did not retain the calculated DoT view.");

            object columns = player.GetType().GetProperty("Columns").GetValue(player, null);
            string primary = (string)columns.GetType().GetProperty("Primary").GetValue(columns, null);
            if (primary != "Calculated DoT")
                throw new InvalidOperationException("The calculated DoT column metadata is missing.");

            long playerTotal = GetEncounterTotal(player);
            long actionTotal = GetEncounterTotal(action);
            if (playerTotal != actionTotal)
                throw new InvalidOperationException("Calculated DoT player and action totals differ.");

            if (actionTotal > 0)
            {
                ValidateActionRows(action);
                object magic = Build(build, "damageDealt", "magic", "action");
                if (GetEncounterTotal(magic) < actionTotal)
                    throw new InvalidOperationException("Calculated spell DoT was not included in Magic totals.");

                bool foundIntegratedDot = false;
                foreach (object row in (IEnumerable)magic.GetType()
                    .GetProperty("Combatants").GetValue(magic, null))
                {
                    string actionName = (string)row.GetType().GetProperty("Job").GetValue(row, null);
                    if (actionName.EndsWith("(DoT)", StringComparison.Ordinal))
                        foundIntegratedDot = true;
                }
                if (!foundIntegratedDot)
                    throw new InvalidOperationException("Magic action grouping omitted calculated DoT rows.");
            }
        }

        private static void VerifyDotEstimatorRules()
        {
            SanctumDotProfileStore.Clear();
            KPDatabaseDataSet data = new KPDatabaseDataSet();
            KPDatabaseDataSet.CombatantsRow player = data.Combatants.AddCombatantsRow(
                "DoT Tester",
                (byte)EntityType.Player,
                null);
            KPDatabaseDataSet.CombatantsRow enemy = data.Combatants.AddCombatantsRow(
                "Training Target",
                (byte)EntityType.Mob,
                null);
            DateTime start = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);
            KPDatabaseDataSet.BattlesRow battle = data.Battles.AddBattlesRow(
                enemy,
                start,
                start.AddSeconds(30),
                true,
                player,
                (byte)EntityType.Player,
                0,
                0,
                (byte)MobDifficulty.EvenMatch,
                false);

            AddEnfeeble(data, battle, player, enemy, "Poison", start);
            AddDamage(data, battle, player, enemy, "Poison Nails", start.AddSeconds(10), 100);
            AddEnfeeble(data, battle, player, enemy, "Poison II", start.AddSeconds(15));
            AddDamage(data, battle, player, enemy, "Geohelix", start, 100);
            AddEnfeeble(data, battle, player, enemy, "Foe Requiem V", start);
            AddEnfeeble(data, battle, player, enemy, "Frost", start);
            AddEnfeeble(data, battle, player, enemy, "Burn", start.AddSeconds(12));

            Type estimator = typeof(DatabaseManager).Assembly.GetType(
                "WaywardGamers.KParser.Bridge.SanctumDotEstimator",
                true);
            MethodInfo estimate = estimator.GetMethod(
                "Estimate",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (estimate == null)
                throw new MissingMethodException(estimator.FullName, "Estimate");

            IList<KPDatabaseDataSet.BattlesRow> battles =
                new List<KPDatabaseDataSet.BattlesRow> { battle };
            IDictionary<int, int> enemyIds = new Dictionary<int, int>();
            enemyIds[battle.BattleID] = enemy.CombatantID;
            IEnumerable results = (IEnumerable)estimate.Invoke(null, new object[]
            {
                battles,
                data.Interactions,
                enemyIds,
                false
            });

            long total = 0;
            int rows = 0;
            foreach (object result in results)
            {
                rows++;
                total += (long)result.GetType().GetProperty("Damage").GetValue(result, null);
            }

            // Poison Nails is ignored while Poison is active. Burn also removes
            // Frost at 12 seconds, matching the elemental-debuff effect scripts.
            // The fallback estimate totals 470 + Frost 4*4 + Burn 6*4 = 510.
            if (rows != 6 || total != 510)
            {
                throw new InvalidOperationException(string.Format(
                    "Calculated DoT rule test failed: rows={0}, total={1}.",
                    rows,
                    total));
            }

            SanctumDotProfileStore.Set(new SanctumPlayerStatProfile
            {
                PlayerName = "DoT Tester",
                Intelligence = 151,
                EnfeeblingSkill = 500,
                ElementalSkill = 500,
                DarkSkill = 500,
                NinjutsuSkill = 500,
                SingingSkill = 500,
                CapturedUtc = DateTime.UtcNow
            });

            results = (IEnumerable)estimate.Invoke(null, new object[]
            {
                battles,
                data.Interactions,
                enemyIds,
                false
            });
            total = 0;
            rows = 0;
            int capturedRows = 0;
            foreach (object result in results)
            {
                rows++;
                total += (long)result.GetType().GetProperty("Damage").GetValue(result, null);
                if ((bool)result.GetType().GetProperty("UsedCapturedStats").GetValue(result, null))
                    capturedRows++;
            }

            // Enfeebling 500 produces Poison 55 and Poison II 78. INT 151
            // produces 5/tick elemental debuffs. Direct Helix and base Requiem
            // remain log/base-driven, for a total of 1,065.
            if (rows != 6 || capturedRows != 4 || total != 1065)
            {
                throw new InvalidOperationException(string.Format(
                    "Captured DoT rule test failed: rows={0}, captured={1}, total={2}.",
                    rows,
                    capturedRows,
                    total));
            }

            SanctumDotProfileStore.Clear();
        }

        private static void VerifyExtendedDotEstimatorRules()
        {
            SanctumDotProfileStore.Clear();
            KPDatabaseDataSet data = new KPDatabaseDataSet();
            KPDatabaseDataSet.CombatantsRow player = data.Combatants.AddCombatantsRow(
                "DotTester",
                (byte)EntityType.Player,
                null);
            KPDatabaseDataSet.CombatantsRow pet = data.Combatants.AddCombatantsRow(
                "DotTester's Funguar",
                (byte)EntityType.Pet,
                null);
            DateTime start = new DateTime(2026, 8, 8, 1, 0, 0, DateTimeKind.Utc);
            List<KPDatabaseDataSet.BattlesRow> battles =
                new List<KPDatabaseDataSet.BattlesRow>();
            Dictionary<int, int> enemyIds = new Dictionary<int, int>();

            SanctumDotProfileStore.Set(new SanctumPlayerStatProfile
            {
                PlayerName = "DotTester",
                MainJobLevel = 75,
                DaggerSkill = 180,
                SwordSkill = 220,
                ClubSkill = 225,
                ArcherySkill = 180,
                MarksmanshipSkill = 200,
                DarkSkill = 220,
                CapturedUtc = DateTime.UtcNow
            });

            KPDatabaseDataSet.BattlesRow hotShot = AddDotTestBattle(
                data, player, "Hot Shot Target", start, 30, battles, enemyIds);
            AddDamage(data, hotShot, player, hotShot.CombatantsRowByEnemyCombatantRelation,
                "Hot Shot", start, 100, ActionType.Weaponskill);

            KPDatabaseDataSet.BattlesRow flamingArrow = AddDotTestBattle(
                data, player, "Flaming Arrow Target", start, 30, battles, enemyIds);
            AddDamage(data, flamingArrow, player, flamingArrow.CombatantsRowByEnemyCombatantRelation,
                "Flaming Arrow", start, 100, ActionType.Weaponskill);

            KPDatabaseDataSet.BattlesRow burningBlade = AddDotTestBattle(
                data, player, "Burning Blade Target", start, 45, battles, enemyIds);
            AddDamage(data, burningBlade, player, burningBlade.CombatantsRowByEnemyCombatantRelation,
                "Burning Blade", start, 100, ActionType.Weaponskill);

            KPDatabaseDataSet.BattlesRow brainshaker = AddDotTestBattle(
                data, player, "Brainshaker Target", start, 60, battles, enemyIds);
            AddDamage(data, brainshaker, player, brainshaker.CombatantsRowByEnemyCombatantRelation,
                "Brainshaker", start, 100, ActionType.Weaponskill);

            KPDatabaseDataSet.BattlesRow viperBite = AddDotTestBattle(
                data, player, "Viper Bite Target", start, 90, battles, enemyIds);
            AddDamage(data, viperBite, player, viperBite.CombatantsRowByEnemyCombatantRelation,
                "Viper Bite", start, 100, ActionType.Weaponskill);

            KPDatabaseDataSet.BattlesRow waspSting = AddDotTestBattle(
                data, player, "Wasp Sting Target", start, 90, battles, enemyIds);
            AddDamage(data, waspSting, player, waspSting.CombatantsRowByEnemyCombatantRelation,
                "Wasp Sting", start, 100, ActionType.Weaponskill);

            KPDatabaseDataSet.BattlesRow bladeYu = AddDotTestBattle(
                data, player, "Blade Yu Target", start, 90, battles, enemyIds);
            AddDamage(data, bladeYu, player, bladeYu.CombatantsRowByEnemyCombatantRelation,
                "Blade: Yu", start, 100, ActionType.Weaponskill);

            KPDatabaseDataSet.BattlesRow kaustra = AddDotTestBattle(
                data, player, "Kaustra Target", start, 120, battles, enemyIds);
            AddDamage(data, kaustra, player, kaustra.CombatantsRowByEnemyCombatantRelation,
                "Kaustra", start, 400, ActionType.Spell);

            KPDatabaseDataSet.BattlesRow modus = AddDotTestBattle(
                data, player, "Modus Target", start, 60, battles, enemyIds);
            AddDamage(data, modus, player, modus.CombatantsRowByEnemyCombatantRelation,
                "Geohelix", start, 100, ActionType.Spell);
            AddDamage(data, modus, player, modus.CombatantsRowByEnemyCombatantRelation,
                "Modus Veritas", start.AddSeconds(25), 300, ActionType.Ability);

            KPDatabaseDataSet.BattlesRow queasyshroom = AddDotTestBattle(
                data, player, "Queasyshroom Target", start, 70, battles, enemyIds);
            AddDamage(data, queasyshroom, pet, queasyshroom.CombatantsRowByEnemyCombatantRelation,
                "Queasyshroom", start, 100, ActionType.Ability);

            KPDatabaseDataSet.BattlesRow leafDagger = AddDotTestBattle(
                data, player, "Leaf Dagger Target", start, 90, battles, enemyIds);
            AddDamage(data, leafDagger, pet, leafDagger.CombatantsRowByEnemyCombatantRelation,
                "Leaf Dagger", start, 100, ActionType.Ability);

            KPDatabaseDataSet.BattlesRow toxicSpit = AddDotTestBattle(
                data, player, "Toxic Spit Target", start, 180, battles, enemyIds);
            AddEnfeeble(data, toxicSpit, pet, toxicSpit.CombatantsRowByEnemyCombatantRelation,
                "Toxic Spit", start, ActionType.Ability);

            KPDatabaseDataSet.BattlesRow purulentOoze = AddDotTestBattle(
                data, player, "Purulent Ooze Target", start, 120, battles, enemyIds);
            AddDamage(data, purulentOoze, pet, purulentOoze.CombatantsRowByEnemyCombatantRelation,
                "Purulent Ooze", start, 100, ActionType.Ability);

            KPDatabaseDataSet.BattlesRow venom = AddDotTestBattle(
                data, player, "Venom Target", start, 60, battles, enemyIds);
            AddDamage(data, venom, pet, venom.CombatantsRowByEnemyCombatantRelation,
                "Venom", start, 100, ActionType.Ability);

            KPDatabaseDataSet.BattlesRow venomSpray = AddDotTestBattle(
                data, player, "Venom Spray Target", start, 120, battles, enemyIds);
            AddEnfeeble(data, venomSpray, pet, venomSpray.CombatantsRowByEnemyCombatantRelation,
                "Venom Spray", start, ActionType.Ability);

            KPDatabaseDataSet.BattlesRow zeroDamageBio = AddDotTestBattle(
                data, player, "Zero Damage Bio Target", start, 30, battles, enemyIds);
            AddDamage(data, zeroDamageBio, player, zeroDamageBio.CombatantsRowByEnemyCombatantRelation,
                "Bio", start, 0, ActionType.Spell);

            KPDatabaseDataSet.BattlesRow zeroDamageDia = AddDotTestBattle(
                data, player, "Zero Damage Dia Target", start, 30, battles, enemyIds);
            AddDamage(data, zeroDamageDia, player, zeroDamageDia.CombatantsRowByEnemyCombatantRelation,
                "Dia", start, 0, ActionType.Spell);

            KPDatabaseDataSet.BattlesRow resistedBio = AddDotTestBattle(
                data, player, "Resisted Bio Target", start, 30, battles, enemyIds);
            AddDamageOutcome(data, resistedBio, player, resistedBio.CombatantsRowByEnemyCombatantRelation,
                "Bio II", start, FailedActionType.None, DefenseType.Resist);

            KPDatabaseDataSet.BattlesRow absorbedDia = AddDotTestBattle(
                data, player, "Absorbed Dia Target", start, 30, battles, enemyIds);
            AddDamageOutcome(data, absorbedDia, player, absorbedDia.CombatantsRowByEnemyCombatantRelation,
                "Dia II", start, FailedActionType.None, DefenseType.Absorb);

            KPDatabaseDataSet.BattlesRow interruptedBio = AddDotTestBattle(
                data, player, "Interrupted Bio Target", start, 30, battles, enemyIds);
            AddDamageOutcome(data, interruptedBio, player, interruptedBio.CombatantsRowByEnemyCombatantRelation,
                "Bio III", start, FailedActionType.Interrupted, DefenseType.None);

            KPDatabaseDataSet.BattlesRow noEffectDia = AddDotTestBattle(
                data, player, "No Effect Dia Target", start, 30, battles, enemyIds);
            AddDamageOutcome(data, noEffectDia, player, noEffectDia.CombatantsRowByEnemyCombatantRelation,
                "Dia III", start, FailedActionType.NoEffect, DefenseType.None);

            Type estimator = typeof(DatabaseManager).Assembly.GetType(
                "WaywardGamers.KParser.Bridge.SanctumDotEstimator",
                true);
            MethodInfo estimate = estimator.GetMethod(
                "Estimate",
                BindingFlags.Static | BindingFlags.NonPublic);
            IEnumerable results = (IEnumerable)estimate.Invoke(null, new object[]
            {
                battles,
                data.Interactions,
                enemyIds,
                false
            });

            Dictionary<string, long> actual = new Dictionary<string, long>(
                StringComparer.Ordinal);
            foreach (object result in results)
            {
                string action = (string)result.GetType()
                    .GetProperty("ActionName").GetValue(result, null);
                actual[action] = (long)result.GetType()
                    .GetProperty("Damage").GetValue(result, null);
            }

            Dictionary<string, long> expected = new Dictionary<string, long>(
                StringComparer.Ordinal)
            {
                { "Hot Shot", 130 },
                { "Flaming Arrow", 120 },
                { "Burning Blade", 210 },
                { "Brainshaker", 300 },
                { "Viper Bite", 360 },
                { "Wasp Sting", 30 },
                { "Blade: Yu", 300 },
                { "Kaustra", 2100 },
                { "Geohelix", 200 },
                { "Queasyshroom", 160 },
                { "Leaf Dagger", 210 },
                { "Toxic Spit", 1080 },
                { "Purulent Ooze", 480 },
                { "Venom", 40 },
                { "Venom Spray", 600 },
                { "Bio", 30 },
                { "Dia", 10 }
            };

            if (actual.Count != expected.Count ||
                expected.Any(item =>
                    actual.ContainsKey(item.Key) == false ||
                    actual[item.Key] != item.Value))
            {
                throw new InvalidOperationException(
                    "Extended Sanctum DoT rules did not produce the expected totals.");
            }

            SanctumDotProfileStore.Clear();
        }

        private static void VerifyHorizonDotRules()
        {
            SanctumDotProfileStore.Clear();
            Assembly parserCore = typeof(DatabaseManager).Assembly;
            Type compatibility = parserCore.GetType(
                "WaywardGamers.KParser.Bridge.ServerCompatibility",
                true);
            MethodInfo configure = compatibility.GetMethod(
                "Configure",
                BindingFlags.Static | BindingFlags.NonPublic);
            Type estimator = parserCore.GetType(
                "WaywardGamers.KParser.Bridge.SanctumDotEstimator",
                true);
            MethodInfo estimate = estimator.GetMethod(
                "Estimate",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (configure == null || estimate == null)
                throw new MissingMemberException("Horizon DoT compatibility members were not found.");

            KPDatabaseDataSet data = new KPDatabaseDataSet();
            KPDatabaseDataSet.CombatantsRow player = data.Combatants.AddCombatantsRow(
                "HorizonDotTester",
                (byte)EntityType.Player,
                null);
            DateTime start = new DateTime(2026, 8, 12, 1, 0, 0, DateTimeKind.Utc);
            List<KPDatabaseDataSet.BattlesRow> battles =
                new List<KPDatabaseDataSet.BattlesRow>();
            Dictionary<int, int> enemyIds = new Dictionary<int, int>();

            KPDatabaseDataSet.BattlesRow poison = AddDotTestBattle(
                data, player, "Horizon Poison Target", start, 30, battles, enemyIds);
            AddEnfeeble(
                data,
                poison,
                player,
                poison.CombatantsRowByEnemyCombatantRelation,
                "Poison",
                start);
            KPDatabaseDataSet.BattlesRow hotShot = AddDotTestBattle(
                data, player, "Horizon Hot Shot Target", start, 30, battles, enemyIds);
            AddDamage(
                data,
                hotShot,
                player,
                hotShot.CombatantsRowByEnemyCombatantRelation,
                "Hot Shot",
                start,
                100,
                ActionType.Weaponskill);

            try
            {
                configure.Invoke(null, new object[] { "horizon", string.Empty });
                IEnumerable results = (IEnumerable)estimate.Invoke(null, new object[]
                {
                    battles,
                    data.Interactions,
                    enemyIds,
                    false
                });
                List<string> actions = new List<string>();
                foreach (object result in results)
                {
                    actions.Add((string)result.GetType()
                        .GetProperty("ActionName").GetValue(result, null));
                }

                if (actions.Contains("Poison") == false || actions.Contains("Hot Shot"))
                {
                    throw new InvalidOperationException(
                        "Horizon did not retain standard DoTs while excluding Sanctum-only weapon effects.");
                }
            }
            finally
            {
                configure.Invoke(null, new object[] { "sanctum", string.Empty });
                SanctumDotProfileStore.Clear();
            }
        }

        private static KPDatabaseDataSet.BattlesRow AddDotTestBattle(
            KPDatabaseDataSet data,
            KPDatabaseDataSet.CombatantsRow player,
            string enemyName,
            DateTime start,
            int durationSeconds,
            IList<KPDatabaseDataSet.BattlesRow> battles,
            IDictionary<int, int> enemyIds)
        {
            KPDatabaseDataSet.CombatantsRow enemy = data.Combatants.AddCombatantsRow(
                enemyName,
                (byte)EntityType.Mob,
                null);
            KPDatabaseDataSet.BattlesRow battle = data.Battles.AddBattlesRow(
                enemy,
                start,
                start.AddSeconds(durationSeconds),
                true,
                player,
                (byte)EntityType.Player,
                0,
                0,
                (byte)MobDifficulty.EvenMatch,
                false);
            battles.Add(battle);
            enemyIds[battle.BattleID] = enemy.CombatantID;
            return battle;
        }

        private static void VerifyPlayerStatLayout()
        {
            byte[] data = new byte[204];
            data[8] = 4;
            data[9] = 99;
            data[10] = 5;
            data[11] = 49;
            for (int offset = 16; offset <= 28; offset += 2)
                WriteInt16(data, offset, 70);
            WriteInt16(data, 38, 81);
            WriteUInt16(data, 108 + 2 * 2, 202);
            WriteUInt16(data, 108 + 3 * 2, 203);
            WriteUInt16(data, 108 + 9 * 2, 209);
            WriteUInt16(data, 108 + 11 * 2, 211);
            WriteUInt16(data, 108 + 25 * 2, 225);
            WriteUInt16(data, 108 + 26 * 2, 226);
            WriteUInt16(data, 108 + 36 * 2, 410);
            WriteUInt16(data, 108 + 37 * 2, 420);
            WriteUInt16(data, 108 + 38 * 2, 430);
            WriteUInt16(data, 108 + 40 * 2, 440);
            WriteUInt16(data, 108 + 41 * 2, 450);

            MethodInfo parse = typeof(SanctumPlayerStatsReader).GetMethod(
                "TryParsePlayerData",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (parse == null)
                throw new MissingMethodException(typeof(SanctumPlayerStatsReader).FullName, "TryParsePlayerData");

            object[] arguments = { data, "DoT Tester", 1234, null };
            bool success = (bool)parse.Invoke(null, arguments);
            SanctumPlayerStatProfile profile = arguments[3] as SanctumPlayerStatProfile;
            if (!success || profile == null || profile.MainJob != 4 ||
                profile.MainJobLevel != 99 || profile.SubJob != 5 ||
                profile.SubJobLevel != 49 || profile.Intelligence != 151 ||
                profile.DaggerSkill != 202 || profile.SwordSkill != 203 ||
                profile.KatanaSkill != 209 || profile.ClubSkill != 211 ||
                profile.ArcherySkill != 225 || profile.MarksmanshipSkill != 226 ||
                profile.EnfeeblingSkill != 410 || profile.ElementalSkill != 420 ||
                profile.DarkSkill != 430 || profile.NinjutsuSkill != 440 ||
                profile.SingingSkill != 450)
            {
                throw new InvalidOperationException("The validated FFXI player stat layout did not parse correctly.");
            }
        }

        private static void WriteInt16(byte[] data, int offset, short value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Copy(bytes, 0, data, offset, bytes.Length);
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Copy(bytes, 0, data, offset, bytes.Length);
        }

        private static void AddEnfeeble(
            KPDatabaseDataSet data,
            KPDatabaseDataSet.BattlesRow battle,
            KPDatabaseDataSet.CombatantsRow actor,
            KPDatabaseDataSet.CombatantsRow target,
            string actionName,
            DateTime timestamp)
        {
            AddEnfeeble(
                data,
                battle,
                actor,
                target,
                actionName,
                timestamp,
                ActionType.Spell);
        }

        private static void AddEnfeeble(
            KPDatabaseDataSet data,
            KPDatabaseDataSet.BattlesRow battle,
            KPDatabaseDataSet.CombatantsRow actor,
            KPDatabaseDataSet.CombatantsRow target,
            string actionName,
            DateTime timestamp,
            ActionType actionType)
        {
            KPDatabaseDataSet.ActionsRow action = data.Actions.AddActionsRow(actionName);
            data.Interactions.AddInteractionsRow(
                timestamp, actor, target, battle, (byte)ActorType.Self, false, action,
                (byte)actionType, (byte)FailedActionType.None, (byte)DefenseType.None,
                0, (byte)AidType.None, (byte)RecoveryType.None, (byte)HarmType.Enfeeble,
                0, (byte)DamageModifier.None, (byte)AidType.None, (byte)RecoveryType.None,
                (byte)HarmType.None, 0, null, null);
        }

        private static void AddDamage(
            KPDatabaseDataSet data,
            KPDatabaseDataSet.BattlesRow battle,
            KPDatabaseDataSet.CombatantsRow actor,
            KPDatabaseDataSet.CombatantsRow target,
            string actionName,
            DateTime timestamp,
            int damage)
        {
            AddDamage(
                data,
                battle,
                actor,
                target,
                actionName,
                timestamp,
                damage,
                ActionType.Spell);
        }

        private static void AddDamage(
            KPDatabaseDataSet data,
            KPDatabaseDataSet.BattlesRow battle,
            KPDatabaseDataSet.CombatantsRow actor,
            KPDatabaseDataSet.CombatantsRow target,
            string actionName,
            DateTime timestamp,
            int damage,
            ActionType actionType)
        {
            KPDatabaseDataSet.ActionsRow action = data.Actions.AddActionsRow(actionName);
            data.Interactions.AddInteractionsRow(
                timestamp, actor, target, battle, (byte)ActorType.Self, false, action,
                (byte)actionType, (byte)FailedActionType.None, (byte)DefenseType.None,
                0, (byte)AidType.None, (byte)RecoveryType.None, (byte)HarmType.Damage,
                damage, (byte)DamageModifier.None, (byte)AidType.None, (byte)RecoveryType.None,
                (byte)HarmType.None, 0, null, null);
        }

        private static void AddDamageOutcome(
            KPDatabaseDataSet data,
            KPDatabaseDataSet.BattlesRow battle,
            KPDatabaseDataSet.CombatantsRow actor,
            KPDatabaseDataSet.CombatantsRow target,
            string actionName,
            DateTime timestamp,
            FailedActionType failedAction,
            DefenseType defense)
        {
            KPDatabaseDataSet.ActionsRow action = data.Actions.AddActionsRow(actionName);
            data.Interactions.AddInteractionsRow(
                timestamp, actor, target, battle, (byte)ActorType.Self, false, action,
                (byte)ActionType.Spell, (byte)failedAction, (byte)defense,
                0, (byte)AidType.None, (byte)RecoveryType.None, (byte)HarmType.Damage,
                0, (byte)DamageModifier.None, (byte)AidType.None, (byte)RecoveryType.None,
                (byte)HarmType.None, 0, null, null);
        }

        private static void VerifyFightReports(MethodInfo build)
        {
            object history = Build(build, "fights", "history", "player");
            object performance = Build(build, "fights", "performance", "player");
            ValidateFightRows(history);
            ValidatePerformanceRows(performance);
        }

        private static void ValidateFightRows(object snapshot)
        {
            string report = (string)snapshot.GetType().GetProperty("Report").GetValue(snapshot, null);
            string display = (string)snapshot.GetType().GetProperty("DisplayMode").GetValue(snapshot, null);
            if (report != "fights" || display != "history")
                throw new InvalidOperationException("The bridge did not retain the fight history report.");

            object columns = snapshot.GetType().GetProperty("Columns").GetValue(snapshot, null);
            if ((string)columns.GetType().GetProperty("Name").GetValue(columns, null) != "Enemy")
                throw new InvalidOperationException("Fight history column metadata is missing.");

            long rowTotal = 0;
            int rowCount = 0;
            foreach (object row in (IEnumerable)snapshot.GetType().GetProperty("Combatants").GetValue(snapshot, null))
            {
                rowCount++;
                string key = (string)row.GetType().GetProperty("Key").GetValue(row, null);
                string duration = (string)row.GetType().GetProperty("Detail1Text").GetValue(row, null);
                if (string.IsNullOrEmpty(key) || !key.StartsWith("fight:") || string.IsNullOrEmpty(duration))
                    throw new InvalidOperationException("A fight-history row is missing its identity or duration.");
                rowTotal += (long)row.GetType().GetProperty("Damage").GetValue(row, null);
            }

            if (rowCount == 0 || rowTotal != GetEncounterTotal(snapshot))
                throw new InvalidOperationException("Fight-history totals did not reconcile.");
        }

        private static void ValidatePerformanceRows(object snapshot)
        {
            string display = (string)snapshot.GetType().GetProperty("DisplayMode").GetValue(snapshot, null);
            if (display != "performance")
                throw new InvalidOperationException("The bridge did not retain the player performance view.");

            long rowTotal = 0;
            int rowCount = 0;
            foreach (object row in (IEnumerable)snapshot.GetType().GetProperty("Combatants").GetValue(snapshot, null))
            {
                rowCount++;
                string fights = (string)row.GetType().GetProperty("Detail1Text").GetValue(row, null);
                string participation = (string)row.GetType().GetProperty("Detail2Text").GetValue(row, null);
                string activeTime = (string)row.GetType().GetProperty("Detail3Text").GetValue(row, null);
                if (string.IsNullOrEmpty(fights) || string.IsNullOrEmpty(participation) ||
                    string.IsNullOrEmpty(activeTime))
                {
                    throw new InvalidOperationException("A player-performance row is missing participation data.");
                }
                rowTotal += (long)row.GetType().GetProperty("Damage").GetValue(row, null);
            }

            if (rowCount == 0 || rowTotal != GetEncounterTotal(snapshot))
                throw new InvalidOperationException("Player-performance totals did not reconcile.");
        }
    }
}
