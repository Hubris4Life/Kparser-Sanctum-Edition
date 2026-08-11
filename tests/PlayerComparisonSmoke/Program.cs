using System.IO;
using System.Text;
using KParser.Sanctum.UI.Bridge;
using KParser.Sanctum.UI.Models;
using KParser.Sanctum.UI.Services;
using KParser.Sanctum.UI.ViewModels;

var root = Path.Combine(Path.GetTempPath(), "KParser-PlayerComparisonSmoke-" + Guid.NewGuid().ToString("N"));
try
{
    var startup = new MainWindowViewModel();
    if (startup.Combatants.Count != 0)
        throw new InvalidOperationException("The main window still contains demonstration parse rows.");
    startup.ConfigureServerProfile("sanctum");
    startup.IsDamageDealtSelected = true;
    var combatantScopes = startup.CombatantScopes.Select(scope => scope.Label).ToArray();
    if (!combatantScopes.SequenceEqual(new[] { "Alliance", "Party", "Self" }))
        throw new InvalidOperationException("The combatant list is not Alliance / Party / Self.");
    var combatantScopeKeys = startup.CombatantScopes.Select(scope => scope.Key).ToArray();
    if (!combatantScopeKeys.SequenceEqual(new[] { "all", "party", "self" }))
        throw new InvalidOperationException("The combatant scope keys are not all / party / self.");
    foreach (var scope in combatantScopeKeys)
    {
        if (ParserBridgeClient.GetEffectiveCombatantScope(scope, "damageDealt", true) != scope + ":petrows" ||
            ParserBridgeClient.GetEffectiveCombatantScope(scope + ":petrows", "damageDealt", true) != scope + ":petrows" ||
            ParserBridgeClient.GetEffectiveCombatantScope(scope, "damageDealt", false) != scope ||
            ParserBridgeClient.GetEffectiveCombatantScope(scope, "healing", true) != scope)
        {
            throw new InvalidOperationException(
                "Separate pet display did not preserve the " + scope + " combatant scope boundary.");
        }
    }
    startup.SetEngineReady(startedBundledEngine: true);
    if (!startup.CanCaptureDotStats || startup.SelectedCombatant is not null || startup.ParserRunning)
        throw new InvalidOperationException("Stopped-session DoT stat capture is not available from the clean main page.");
    startup.ConfigureServerProfile("other");
    if (startup.CanCaptureDotStats)
        throw new InvalidOperationException("Other-server mode unexpectedly enabled Sanctum DoT stat capture.");
    startup.ConfigureServerProfile("sanctum");
    if (!startup.DisplayModes.Any(mode => mode.Key == "timeline") ||
        !startup.DisplayModes.Any(mode => mode.Key == "wsrates"))
    {
        throw new InvalidOperationException("The legacy-parity damage displays are missing.");
    }
    startup.SelectedDisplayMode = startup.DisplayModes.Single(mode => mode.Key == "timeline");
    if (!startup.IsTimelineSelected || startup.ShowReportTable || startup.ShowTotalRow || startup.ShowSelectedFooter)
        throw new InvalidOperationException("Damage timeline layout switching is invalid.");
    startup.IsLootSelected = true;
    if (!startup.DisplayModes.Any(mode => mode.Key == "itemsused"))
        throw new InvalidOperationException("The consumable item-use display is missing.");

    var partyRow = new CombatantRow
    {
        Rank = 1,
        Name = "Packa",
        Job = "PLD / WAR",
        CombatantType = "Player",
        Damage = 123456,
        Share = 42.3,
        Dps = 617.3,
        Accuracy = "Accuracy: 95.5%"
    };
    var partyCommand = GameChatService.BuildPartyCommand(partyRow);
    if (partyCommand != "/p Packa: 123,456 dmg | 42.3% share | 95.5% acc")
        throw new InvalidOperationException("The party-chat summary format is incorrect: " + partyCommand);

    Directory.CreateDirectory(root);
    var exportViewModel = new MainWindowViewModel();
    var exportSnapshot = new BridgeSnapshot
    {
        GeneratedUtc = DateTime.UtcNow.ToString("o"),
        EngineVersion = "smoke",
        DatabaseOpen = true,
        Report = "damageDealt",
        DisplayMode = "sources",
        Encounter = new BridgeEncounter
        {
            Name = "Export test",
            Scope = "all",
            StartUtc = DateTime.UtcNow.AddMinutes(-1).ToString("o"),
            DurationSeconds = 60,
            FightCount = 1,
            EventCount = 1,
            TotalDamage = 123456,
            AllianceDps = 2057.6
        },
        Combatants =
        [
            new BridgeCombatant
            {
                Key = "Packa",
                Rank = 1,
                Name = "Packa",
                Job = "PLD / WAR",
                CombatantType = "Player",
                Damage = 123456,
                SharePercent = 100,
                PhysicalAttempts = 100,
                PhysicalHits = 95,
                PhysicalMisses = 5,
                CriticalHits = 19,
                Accuracy = "Accuracy: 95.0%",
                CriticalRate = "Critical hit rate: 20.0%"
            }
        ]
    };
    exportViewModel.ApplySnapshot(exportSnapshot);
    if (exportViewModel.Combatants.Single().CriticalRateDisplay != "20.0%")
        throw new InvalidOperationException("The main physical report did not retain critical-hit rate.");
    var exportPath = Path.Combine(root, ReportExportService.CreateDefaultFileName(exportViewModel));
    File.WriteAllText(exportPath, ReportExportService.BuildCsv(exportViewModel), new UTF8Encoding(true));
    var exportedText = File.ReadAllText(exportPath);
    if (!exportedText.Contains("Packa") || !exportedText.Contains("123,456") ||
        !exportedText.Contains("Critical hit rate: 20.0%"))
        throw new InvalidOperationException("The main report CSV export omitted visible report data.");

    var overlay = new CurrentFightViewModel("all", "all", false, "overlay", 0);
    overlay.ApplySnapshot(exportSnapshot);
    if (!overlay.IsTrueOverlayMode || overlay.Combatants.Single().CriticalRateDisplay != "20.0%")
        throw new InvalidOperationException("True Overlay did not retain the physical critical-hit rate.");
    var overlayCsv = ReportExportService.BuildCurrentFightCsv(overlay);
    if (!overlayCsv.Contains("Critical rate") || !overlayCsv.Contains("20.0%"))
        throw new InvalidOperationException("The live-monitor export omitted critical-hit rate.");

    var petSnapshot = new BridgeSnapshot
    {
        GeneratedUtc = DateTime.UtcNow.ToString("o"),
        EngineVersion = "smoke",
        DatabaseOpen = true,
        Report = "damageDealt",
        DisplayMode = "sources",
        CombatantScope = "all:petrows",
        Encounter = new BridgeEncounter
        {
            Name = "Pet display test",
            Scope = "all",
            StartUtc = DateTime.UtcNow.AddSeconds(-10).ToString("o"),
            DurationSeconds = 10,
            FightCount = 1,
            EventCount = 15,
            TotalDamage = 1000,
            AllianceDps = 100
        },
        Combatants =
        [
            new BridgeCombatant
            {
                Key = "Nazgul",
                Rank = 1,
                Name = "Nazgul",
                Job = "SMN",
                CombatantType = "Player",
                Damage = 700,
                SharePercent = 70,
                Accuracy = "Accuracy: 90.0%",
                CriticalRate = "Critical hit rate: 22.2%"
            },
            new BridgeCombatant
            {
                Key = "Garuda@Nazgul",
                Rank = 2,
                Name = "Garuda (Nazgul)",
                Job = "Pet of Nazgul",
                CombatantType = "Pet",
                Damage = 300,
                SharePercent = 30,
                Accuracy = "Accuracy: 80.0%",
                CriticalRate = "Critical hit rate: 25.0%"
            }
        ]
    };
    var petReport = new MainWindowViewModel();
    petReport.ApplySnapshot(petSnapshot);
    if (petReport.Combatants.Sum(row => row.Damage) != 1000 ||
        Math.Abs(petReport.Combatants.Sum(row => row.Share) - 100) > 0.001 ||
        !ReportExportService.BuildCsv(petReport).Contains("Garuda (Nazgul)"))
    {
        throw new InvalidOperationException(
            "Separate pet rows did not preserve totals, shares, owner labels, or export data.");
    }
    var petMonitor = new CurrentFightViewModel("all", "all", false, "compact", 0);
    petMonitor.ApplySnapshot(petSnapshot);
    if (petMonitor.Combatants.Count != 2 ||
        petMonitor.Combatants.Single(row => row.CombatantType == "Pet").Job != "Pet of Nazgul")
    {
        throw new InvalidOperationException("The live monitor did not preserve the separate pet owner label.");
    }

    var diagnosticReport = DiagnosticReportService.Create(new ApplicationDiagnosticContext
    {
        ServerProfile = "sanctum",
        BridgeStatus = "Connected",
        EngineVersion = "smoke",
        ParserRunning = true,
        DatabaseOpen = true,
        ParseMode = "Ram",
        MemoryStatus = "Validated at 0x1234",
        PetOwnershipMode = "SanctumChat",
        RegisteredPlayer = "Packa",
        DisplayPetDamageSeparately = true,
        LastBridgeSuccessUtc = DateTimeOffset.UtcNow
    });
    if (!diagnosticReport.Text.Contains("Server profile: Sanctum XI") ||
        !diagnosticReport.Text.Contains("Memory detection: Validated at 0x1234") ||
        !diagnosticReport.Text.Contains("Pet display: Separate pet rows") ||
        diagnosticReport.Text.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("The copied diagnostic report is incomplete or exposes a full user path.");
    }

    var service = new PlayerParseService(root);
    service.Save(CreateSnapshot("Baseline", 100_000, 500.0, 90, 100));
    service.Save(CreateSnapshot("Build B", 120_000, 600.0, 95, 100));

    var loaded = service.LoadAll();
    if (loaded.Count != 2)
        throw new InvalidOperationException("Player snapshot history did not round-trip.");

    var comparison = new PlayerComparisonViewModel(service);
    if (!comparison.HasComparison || comparison.Metrics.Count < 10)
        throw new InvalidOperationException("The side-by-side comparison did not populate.");
    if (!comparison.Metrics.Any(metric => metric.Metric == "DPS" &&
                                          metric.Change.Contains("+100.0") &&
                                          metric.Change.Contains("+20.0%")))
        throw new InvalidOperationException("The DPS comparison delta is incorrect.");
    var selectedFirstId = comparison.SelectedFirst?.Id;
    var selectedSecondId = comparison.SelectedSecond?.Id;
    comparison.Refresh();
    if (comparison.SelectedFirst?.Id != selectedFirstId ||
        comparison.SelectedSecond?.Id != selectedSecondId)
    {
        throw new InvalidOperationException("Refreshing saved builds did not retain the visible selections.");
    }

    Console.WriteLine("player-snapshots=verified");
    Console.WriteLine("comparison-metrics=" + comparison.Metrics.Count);
    Console.WriteLine("comparison-selection=verified");
    Console.WriteLine("clean-startup=verified");
    Console.WriteLine("stopped-dot-capture=verified");
    Console.WriteLine("combatant-scopes=verified");
    Console.WriteLine("pet-display-scopes=verified");
    Console.WriteLine("critical-rate-surfaces=verified");
    Console.WriteLine("pet-display-totals-and-exports=verified");
    Console.WriteLine("diagnostic-report=verified");
    Console.WriteLine("party-summary=verified");
    Console.WriteLine("main-export=verified");
}
finally
{
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}

static PlayerParseSnapshot CreateSnapshot(
    string label,
    long damage,
    double dps,
    long hits,
    long attempts)
{
    return new PlayerParseSnapshot
    {
        Label = label,
        PlayerName = "Packa",
        Job = "PLD/WAR",
        EncounterName = "Shinryu",
        DurationSeconds = 200,
        FightCount = 1,
        TotalDamage = damage,
        Dps = dps,
        MeleeDamage = damage / 3,
        WeaponSkillDamage = damage / 2,
        MagicDamage = damage / 6,
        PhysicalHits = hits,
        PhysicalAttempts = attempts,
        PhysicalMisses = attempts - hits,
        CriticalHits = hits / 5,
        TopAction = "Savage Blade"
    };
}
