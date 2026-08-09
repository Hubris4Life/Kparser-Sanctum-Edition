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
    exportViewModel.ApplySnapshot(new BridgeSnapshot
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
                PhysicalHits = 95
            }
        ]
    });
    var exportPath = Path.Combine(root, ReportExportService.CreateDefaultFileName(exportViewModel));
    File.WriteAllText(exportPath, ReportExportService.BuildCsv(exportViewModel), new UTF8Encoding(true));
    var exportedText = File.ReadAllText(exportPath);
    if (!exportedText.Contains("Packa") || !exportedText.Contains("123,456"))
        throw new InvalidOperationException("The main report CSV export omitted visible report data.");

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
