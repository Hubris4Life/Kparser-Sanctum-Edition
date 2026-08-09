using System.IO;
using System.Text.Json;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI.Services;

internal sealed class PlayerParseService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string snapshotDirectory;

    public PlayerParseService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KParser Sanctum Modern",
            "Player Parses"))
    {
    }

    internal PlayerParseService(string snapshotDirectory)
    {
        this.snapshotDirectory = Path.GetFullPath(snapshotDirectory);
    }

    public IReadOnlyList<PlayerParseSnapshot> LoadAll()
    {
        if (!Directory.Exists(snapshotDirectory))
            return [];

        var snapshots = new List<PlayerParseSnapshot>();
        foreach (var path in Directory.EnumerateFiles(snapshotDirectory, "*.json"))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<PlayerParseSnapshot>(
                    File.ReadAllText(path),
                    JsonOptions);
                if (snapshot is not null && snapshot.DataVersion == 1 &&
                    !string.IsNullOrWhiteSpace(snapshot.Id) &&
                    !string.IsNullOrWhiteSpace(snapshot.PlayerName))
                {
                    snapshots.Add(snapshot);
                }
            }
            catch (Exception)
            {
                // One damaged snapshot should not prevent the remaining history from loading.
            }
        }

        return snapshots
            .OrderByDescending(snapshot => snapshot.SavedUtc)
            .ThenBy(snapshot => snapshot.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Save(PlayerParseSnapshot snapshot)
    {
        Directory.CreateDirectory(snapshotDirectory);
        var path = GetPath(snapshot.Id);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    public void Delete(PlayerParseSnapshot snapshot)
    {
        var path = GetPath(snapshot.Id);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string GetPath(string id)
    {
        var safeId = new string(id.Where(char.IsLetterOrDigit).ToArray());
        if (safeId.Length == 0)
            throw new InvalidDataException("The player parse has an invalid identifier.");
        return Path.Combine(snapshotDirectory, safeId + ".json");
    }
}
