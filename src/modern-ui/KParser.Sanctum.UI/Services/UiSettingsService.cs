using System.IO;
using System.Text.Json;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI.Services;

internal sealed class UiSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KParser Sanctum Modern",
        "Settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                var freshSettings = new AppSettings
                {
                    CompactMonitorHeightOptimized = true
                };
                return freshSettings;
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? new AppSettings();
            settings.MainWindow ??= new WindowPlacementSettings { Width = 1450, Height = 900 };
            settings.CurrentFightWindow ??= new WindowPlacementSettings { Width = 620, Height = 560 };
            settings.CompactCurrentFightWindow ??= new WindowPlacementSettings { Width = 430, Height = 285 };
            if (!settings.CompactMonitorHeightOptimized)
            {
                settings.CompactCurrentFightWindow.Height = Math.Min(
                    settings.CompactCurrentFightWindow.Height,
                    285);
                settings.CompactMonitorHeightOptimized = true;
            }
            settings.MainReport ??= "damageDealt";
            settings.MainEncounterKey ??= "all:0:";
            settings.MainCombatantScope ??= "all";
            settings.MainDisplayMode ??= "sources";
            settings.MainGroupMode ??= "player";
            settings.CurrentFightCombatantScope ??= "all";
            settings.CurrentFightView ??= "all";
            settings.CurrentFightBackgroundTransparencyPercent = Math.Clamp(
                settings.CurrentFightBackgroundTransparencyPercent,
                0,
                100);
            return settings;
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public bool TrySave(AppSettings settings, out string? error)
    {
        try
        {
            var directory = Path.GetDirectoryName(settingsPath)!;
            Directory.CreateDirectory(directory);

            var temporaryPath = settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, settingsPath, true);
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
