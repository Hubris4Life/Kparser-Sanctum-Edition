namespace KParser.Sanctum.UI.Models;

internal sealed class ApplicationUpdateAsset
{
    public string Name { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public long Size { get; init; }
    public string? Digest { get; init; }
}

internal sealed class ApplicationUpdateRelease
{
    public string Tag { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string ReleaseUrl { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
    public bool IsPrerelease { get; init; }
    public IReadOnlyList<ApplicationUpdateAsset> Assets { get; init; } = [];
}

internal sealed class ApplicationUpdateCheckResult
{
    public string CurrentVersion { get; init; } = string.Empty;
    public bool IsPortableInstallation { get; init; }
    public IReadOnlyList<ApplicationUpdateRelease> AvailableReleases { get; init; } = [];

    public ApplicationUpdateRelease LatestRelease => AvailableReleases[^1];
}

internal sealed class ApplicationUpdateProgress
{
    public string Status { get; init; } = string.Empty;
    public long BytesReceived { get; init; }
    public long TotalBytes { get; init; }

    public double? Percent => TotalBytes > 0
        ? Math.Clamp(BytesReceived * 100.0 / TotalBytes, 0, 100)
        : null;
}

internal enum ApplicationUpdateWindowOutcome
{
    RemindLater,
    SkipVersion,
    UpdateLaunched
}
