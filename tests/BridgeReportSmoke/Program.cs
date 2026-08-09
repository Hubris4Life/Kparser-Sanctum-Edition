using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using WaywardGamers.KParser;
using WaywardGamers.KParser.Bridge;

namespace BridgeReportSmoke
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool auditMode = args.Length == 2 && args[0] == "--audit";
            if ((!auditMode && args.Length != 1) || (auditMode && args.Length != 2))
                throw new ArgumentException("Expected a KParser database path.");

            string databasePath = auditMode ? args[1] : args[0];
            if (auditMode)
                return AuditDatabase(databasePath);

            VerifyRamReaderStopsBeforeReturning();
            VerifyDotEstimatorRules();
            VerifyPlayerStatLayout();
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

                if (verifiedDisplays == 0)
                    throw new InvalidOperationException("The supplied parse contains no damage category to verify.");

                Console.WriteLine("action-report=verified");
                Console.WriteLine("verified-displays=" + verifiedDisplays);
                Console.WriteLine("dot-report=verified");
                Console.WriteLine("dot-estimator=verified");
                Console.WriteLine("ram-reader-lifecycle=verified");
                Console.WriteLine("fight-reports=verified");
                Console.WriteLine("chat-report=verified");
                Console.WriteLine("loot-reports=verified");
                Console.WriteLine("buff-uptime=verified");
                Console.WriteLine("preview22-reports=verified");
                return 0;
            }
            finally
            {
                DatabaseManager.Instance.CloseDatabase();
            }
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
            foreach (string display in new[] { "summary", "distribution", "rates", "treasurehunter", "helm" })
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
                new[] { "damageDealt", "multiattacks" },
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
                        "Preview 22 report failed metadata/runtime verification: " + item[0] + "/" + item[1] +
                        (string.IsNullOrEmpty(error) ? string.Empty : " - " + error));
                }
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
            KPDatabaseDataSet.ActionsRow action = data.Actions.AddActionsRow(actionName);
            data.Interactions.AddInteractionsRow(
                timestamp, actor, target, battle, (byte)ActorType.Self, false, action,
                (byte)ActionType.Spell, (byte)FailedActionType.None, (byte)DefenseType.None,
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
            KPDatabaseDataSet.ActionsRow action = data.Actions.AddActionsRow(actionName);
            data.Interactions.AddInteractionsRow(
                timestamp, actor, target, battle, (byte)ActorType.Self, false, action,
                (byte)ActionType.Spell, (byte)FailedActionType.None, (byte)DefenseType.None,
                0, (byte)AidType.None, (byte)RecoveryType.None, (byte)HarmType.Damage,
                damage, (byte)DamageModifier.None, (byte)AidType.None, (byte)RecoveryType.None,
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
