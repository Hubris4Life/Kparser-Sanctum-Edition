using System.IO;
using System.Text.Json;
using KParser.Sanctum.UI.Bridge;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI.Services;

internal sealed class PlayerInformationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> KnownJobCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAR", "MNK", "WHM", "BLM", "RDM", "THF", "PLD", "DRK",
        "BST", "BRD", "RNG", "SAM", "NIN", "DRG", "SMN", "BLU",
        "COR", "PUP", "DNC", "SCH", "GEO", "RUN", "MON"
    };
    private readonly object gate = new();
    private readonly string filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KParser Sanctum Modern",
        "PlayerInformation.json");
    private List<PlayerInformationEntry> entries;

    public PlayerInformationService()
    {
        entries = Load();
    }

    public IReadOnlyList<PlayerInformationEntry> GetEntries()
    {
        lock (gate)
            return entries.Select(item => item.Clone()).OrderBy(item => item.Name).ToArray();
    }

    public IReadOnlyList<PlayerInformationEntry> GetEntriesForSnapshot(BridgeSnapshot? snapshot)
    {
        if (snapshot is null || !CanObserveDetectedPlayers(snapshot))
            return [];

        var visiblePlayers = snapshot.Combatants
            .Where(item =>
                string.Equals(item.CombatantType, "Player", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (gate)
        {
            return entries
                .Where(item => visiblePlayers.Contains(item.Name))
                .Select(item => item.Clone())
                .OrderBy(item => item.Name)
                .ToArray();
        }
    }

    public void ObserveAndApply(BridgeSnapshot snapshot)
    {
        if (!IsPlayerJobSnapshot(snapshot))
            return;

        var mayObserve = CanObserveDetectedPlayers(snapshot);
        var changed = false;
        lock (gate)
        {
            foreach (var combatant in snapshot.Combatants.Where(item =>
                         string.Equals(item.CombatantType, "Player", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(item.Name)))
            {
                var detectedJob = NormalizeDetectedJob(combatant.Job);
                var entry = entries.FirstOrDefault(item =>
                    string.Equals(item.Name, combatant.Name, StringComparison.OrdinalIgnoreCase));
                if (mayObserve && entry is null)
                {
                    entry = new PlayerInformationEntry
                    {
                        Name = combatant.Name,
                        DetectedJob = detectedJob
                    };
                    entries.Add(entry);
                    changed = true;
                }
                else if (mayObserve && entry is not null &&
                         detectedJob != "-" &&
                         !string.Equals(entry.DetectedJob, detectedJob, StringComparison.OrdinalIgnoreCase))
                {
                    entry.DetectedJob = detectedJob;
                    changed = true;
                }

                if (entry is not null && !string.IsNullOrWhiteSpace(entry.JobOverride))
                    combatant.Job = entry.JobOverride.Trim().ToUpperInvariant();
            }

            if (changed)
            {
                try { TryWrite(entries); } catch { }
            }
        }
    }

    public bool TrySave(IEnumerable<PlayerInformationEntry> updatedEntries, out string? error)
    {
        lock (gate)
        {
            try
            {
                var updates = updatedEntries
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                    .Select(item => new PlayerInformationEntry
                    {
                        Name = item.Name.Trim(),
                        DetectedJob = NormalizeDetectedJob(item.DetectedJob),
                        JobOverride = NormalizeJob(item.JobOverride),
                        Notes = (item.Notes ?? string.Empty).Trim()
                    })
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(item => item.Name)
                    .ToList();
                var updatedNames = updates
                    .Select(item => item.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                entries = entries
                    .Where(item => !updatedNames.Contains(item.Name))
                    .Concat(updates)
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .OrderBy(item => item.Name)
                    .ToList();
                TryWrite(entries);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    private List<PlayerInformationEntry> Load()
    {
        try
        {
            if (!File.Exists(filePath))
                return [];
            return (JsonSerializer.Deserialize<List<PlayerInformationEntry>>(
                        File.ReadAllText(filePath), JsonOptions) ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => new PlayerInformationEntry
                {
                    Name = item.Name.Trim(),
                    DetectedJob = NormalizeDetectedJob(item.DetectedJob),
                    JobOverride = NormalizeJob(item.JobOverride),
                    Notes = (item.Notes ?? string.Empty).Trim()
                })
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Name)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void TryWrite(IEnumerable<PlayerInformationEntry> value)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var temporary = filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, filePath, true);
    }

    private static string NormalizeJob(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length > 24 ? normalized[..24] : normalized;
    }

    internal static bool CanObserveDetectedPlayers(BridgeSnapshot snapshot) =>
        snapshot.ParserRunning && snapshot.ClientLoggedIn && IsPlayerJobSnapshot(snapshot);

    internal static bool IsDetectedJobLabel(string? value) =>
        NormalizeDetectedJob(value) != "-";

    private static bool IsPlayerJobSnapshot(BridgeSnapshot snapshot) =>
        string.Equals(snapshot.GroupMode, "player", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(snapshot.Columns.Secondary, "Job", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDetectedJob(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (text.Length == 0 || text == "-")
            return "-";

        var parts = text.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2)
            return "-";

        var normalizedParts = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var token = new string(part.Where(character => !char.IsWhiteSpace(character)).ToArray());
            if (token.Length < 3 || token.Length > 6)
                return "-";

            var jobCode = token[..3];
            var level = token[3..];
            if (!KnownJobCodes.Contains(jobCode) ||
                (level.Length > 0 && !level.All(char.IsDigit)))
            {
                return "-";
            }

            normalizedParts.Add(jobCode + level);
        }

        return string.Join(" / ", normalizedParts);
    }
}
