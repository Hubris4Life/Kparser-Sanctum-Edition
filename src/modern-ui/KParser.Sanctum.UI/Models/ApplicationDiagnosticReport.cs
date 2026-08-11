namespace KParser.Sanctum.UI.Models;

internal sealed class ApplicationDiagnosticReport
{
    public IReadOnlyList<ApplicationDiagnosticItem> Items { get; init; } = [];
    public string Text { get; init; } = string.Empty;
}

internal sealed class ApplicationDiagnosticItem
{
    public string Category { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

internal sealed class ApplicationDiagnosticContext
{
    public string ServerProfile { get; init; } = "other";
    public string BridgeStatus { get; init; } = "Not connected";
    public string EngineVersion { get; init; } = "Unknown";
    public string EnginePath { get; init; } = string.Empty;
    public bool OwnsEngineProcess { get; init; }
    public bool ParserRunning { get; init; }
    public bool DatabaseOpen { get; init; }
    public string ParseMode { get; init; } = string.Empty;
    public string MemoryStatus { get; init; } = "Not checked during this session";
    public string PetOwnershipMode { get; init; } = "Observed only";
    public string UnresolvedPetStatus { get; init; } = "No current report data";
    public bool DisplayPetDamageSeparately { get; init; }
    public string RegisteredPlayer { get; init; } = string.Empty;
    public bool AutomaticMemoryDetection { get; init; }
    public bool IncludePrereleaseUpdates { get; init; }
    public bool LightMode { get; init; }
    public DateTimeOffset? LastBridgeSuccessUtc { get; init; }
    public string LastBridgeError { get; init; } = string.Empty;
}
