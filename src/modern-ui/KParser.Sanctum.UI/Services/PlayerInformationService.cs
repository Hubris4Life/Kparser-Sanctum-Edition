using System.IO;
using System.Text.Json;
using KParser.Sanctum.UI.Bridge;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI.Services;

internal sealed class PlayerInformationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
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

    public void ObserveAndApply(BridgeSnapshot snapshot)
    {
        var changed = false;
        lock (gate)
        {
            foreach (var combatant in snapshot.Combatants.Where(item =>
                         string.Equals(item.CombatantType, "Player", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(item.Name)))
            {
                var entry = entries.FirstOrDefault(item =>
                    string.Equals(item.Name, combatant.Name, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    entry = new PlayerInformationEntry
                    {
                        Name = combatant.Name,
                        DetectedJob = string.IsNullOrWhiteSpace(combatant.Job) ? "-" : combatant.Job
                    };
                    entries.Add(entry);
                    changed = true;
                }
                else if (!string.IsNullOrWhiteSpace(combatant.Job) &&
                         combatant.Job != "-" &&
                         !string.Equals(entry.DetectedJob, combatant.Job, StringComparison.OrdinalIgnoreCase))
                {
                    entry.DetectedJob = combatant.Job;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(entry.JobOverride))
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
                entries = updatedEntries
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                    .Select(item => new PlayerInformationEntry
                    {
                        Name = item.Name.Trim(),
                        DetectedJob = string.IsNullOrWhiteSpace(item.DetectedJob) ? "-" : item.DetectedJob.Trim(),
                        JobOverride = NormalizeJob(item.JobOverride),
                        Notes = (item.Notes ?? string.Empty).Trim()
                    })
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
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
            return JsonSerializer.Deserialize<List<PlayerInformationEntry>>(
                       File.ReadAllText(filePath), JsonOptions) ?? [];
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
}
