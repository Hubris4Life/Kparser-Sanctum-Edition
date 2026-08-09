namespace KParser.Sanctum.UI.Models;

internal sealed class PlayerInformationEntry
{
    public string Name { get; set; } = string.Empty;
    public string DetectedJob { get; set; } = "-";
    public string JobOverride { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public PlayerInformationEntry Clone() => new()
    {
        Name = Name,
        DetectedJob = DetectedJob,
        JobOverride = JobOverride,
        Notes = Notes
    };
}
