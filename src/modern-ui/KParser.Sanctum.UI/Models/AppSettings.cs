namespace KParser.Sanctum.UI.Models;

internal sealed class AppSettings
{
    public WindowPlacementSettings MainWindow { get; set; } = new()
    {
        Width = 1450,
        Height = 900
    };

    public WindowPlacementSettings CurrentFightWindow { get; set; } = new()
    {
        Width = 620,
        Height = 560
    };

    public WindowPlacementSettings CompactCurrentFightWindow { get; set; } = new()
    {
        Width = 430,
        Height = 285
    };

    public string MainReport { get; set; } = "damageDealt";
    public string MainEncounterKey { get; set; } = "all:0:";
    public string MainCombatantScope { get; set; } = "all";
    public string MainDisplayMode { get; set; } = "sources";
    public string MainGroupMode { get; set; } = "player";
    public string CurrentFightCombatantScope { get; set; } = "all";
    public string CurrentFightView { get; set; } = "all";
    public double CurrentFightBackgroundTransparencyPercent { get; set; }
    public bool CurrentFightAlwaysOnTop { get; set; }
    public bool CurrentFightCompactMode { get; set; }
    public bool CompactMonitorHeightOptimized { get; set; }
    public bool CurrentFightOpen { get; set; }
    public bool AutoDetectMemoryOnStartup { get; set; }
    public bool IsLightMode { get; set; }
    public string SanctumChatAshitaRoot { get; set; } = string.Empty;
}

internal sealed class WindowPlacementSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}
