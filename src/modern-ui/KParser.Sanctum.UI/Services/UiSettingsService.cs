using System.IO;
using System.Text.Json;
using System.Windows.Media;
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
                    CompactMonitorHeightOptimized = true,
                    ServerProfile = "other",
                    CurrentFightDisplayMode = "full"
                };
                return freshSettings;
            }

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? new AppSettings();
            settings.MainWindow ??= new WindowPlacementSettings { Width = 1450, Height = 900 };
            settings.CurrentFightWindow ??= new WindowPlacementSettings { Width = 620, Height = 560 };
            settings.CompactCurrentFightWindow ??= new WindowPlacementSettings { Width = 430, Height = 285 };
            settings.TrueOverlayCurrentFightWindow ??= new WindowPlacementSettings { Width = 500, Height = 240 };
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
            settings.CurrentFightDisplayMode = NormalizeMonitorDisplayMode(
                string.IsNullOrWhiteSpace(settings.CurrentFightDisplayMode)
                    ? settings.CurrentFightCompactMode ? "compact" : "full"
                    : settings.CurrentFightDisplayMode);
            settings.CurrentFightCompactMode = settings.CurrentFightDisplayMode == "compact";
            settings.ServerProfile = NormalizeServerProfile(settings.ServerProfile);
            settings.KParserBridgeAshitaRoot ??= string.Empty;
            settings.SkippedUpdateVersion ??= string.Empty;
            settings.CurrentFightBackgroundTransparencyPercent = Math.Clamp(
                settings.CurrentFightBackgroundTransparencyPercent,
                0,
                100);
            settings.TrueOverlayTextSize = double.IsFinite(settings.TrueOverlayTextSize)
                ? Math.Clamp(settings.TrueOverlayTextSize, 9, 24)
                : 12;
            settings.TrueOverlayNameColor = NormalizeColor(
                settings.TrueOverlayNameColor,
                "#F0D18A");
            settings.TrueOverlayStatisticColor = NormalizeColor(
                settings.TrueOverlayStatisticColor,
                "#EBEDF0");
            return settings;
        }
        catch (Exception)
        {
            return new AppSettings
            {
                ServerProfile = "other",
                CurrentFightDisplayMode = "full"
            };
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

    internal static string NormalizeServerProfile(string? profile)
    {
        string normalized = profile?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "sanctum" or "sanctum xi" or "sanctumxi" or "sanctum-xi" => "sanctum",
            "horizon" or "horizon xi" or "horizonxi" or "horizon-xi" => "horizon",
            _ => "other"
        };
    }

    internal static string NormalizeMonitorDisplayMode(string? displayMode) =>
        displayMode?.Trim().ToLowerInvariant() switch
        {
            "compact" => "compact",
            "overlay" => "overlay",
            _ => "full"
        };

    internal static string NormalizeColor(string? value, string fallback)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(value?.Trim() ?? string.Empty);
            return color.A == byte.MaxValue
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
