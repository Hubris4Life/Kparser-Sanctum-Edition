using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI.Services;

internal sealed class ApplicationUpdateService : IDisposable
{
    private const string ReleasesEndpoint =
        "https://api.github.com/repos/Hubris4Life/Kparser-Sanctum-Edition/releases?per_page=100";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public ApplicationUpdateService()
        : this(CreateHttpClient(), true)
    {
    }

    internal ApplicationUpdateService(HttpClient httpClient, bool ownsHttpClient = false)
    {
        this.httpClient = httpClient;
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(
        bool includePrereleases,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(ReleasesEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var currentVersion = GetCurrentVersionTag();
        var releases = ParseAvailableReleases(json, currentVersion, includePrereleases);
        return new ApplicationUpdateCheckResult
        {
            CurrentVersion = currentVersion,
            IsPortableInstallation = IsPortableInstallation(AppContext.BaseDirectory),
            AvailableReleases = releases
        };
    }

    public ApplicationUpdateAsset? SelectPackageAsset(
        ApplicationUpdateCheckResult update)
    {
        var assets = update.LatestRelease.Assets;
        return update.IsPortableInstallation
            ? assets.FirstOrDefault(asset =>
                asset.Name.Contains("Portable", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !asset.Name.Contains("KParserBridge", StringComparison.OrdinalIgnoreCase))
            : assets.FirstOrDefault(asset =>
                asset.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    public async Task PrepareAndLaunchAsync(
        ApplicationUpdateCheckResult update,
        IProgress<ApplicationUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var release = update.LatestRelease;
        var asset = SelectPackageAsset(update)
            ?? throw new InvalidOperationException(
                update.IsPortableInstallation
                    ? "This release does not contain a Portable ZIP update."
                    : "This release does not contain a Setup update.");
        if (!string.Equals(Path.GetFileName(asset.Name), asset.Name, StringComparison.Ordinal))
            throw new InvalidDataException("The release contains an unsafe update filename.");
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var assetUri) ||
            !string.Equals(assetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The release contains an unsafe update download URL.");
        }

        var updateRoot = Path.Combine(
            Path.GetTempPath(),
            "KParserSanctumUpdates",
            SanitizePathPart(release.Tag));
        Directory.CreateDirectory(updateRoot);
        var packagePath = Path.Combine(updateRoot, asset.Name);
        var partialPath = packagePath + ".download";

        try
        {
            progress?.Report(new ApplicationUpdateProgress
            {
                Status = $"Downloading {asset.Name}…",
                TotalBytes = asset.Size
            });
            await DownloadAssetAsync(asset, partialPath, progress, cancellationToken);
            File.Move(partialPath, packagePath, true);

            progress?.Report(new ApplicationUpdateProgress
            {
                Status = "Verifying SHA-256 checksum…",
                BytesReceived = asset.Size,
                TotalBytes = asset.Size
            });
            var expectedHash = await ResolveExpectedSha256Async(
                release,
                asset,
                cancellationToken);
            var actualHash = await ComputeSha256Async(packagePath, cancellationToken);
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The downloaded update did not match its published SHA-256 checksum. " +
                    "The update was not started.");
            }

            progress?.Report(new ApplicationUpdateProgress
            {
                Status = update.IsPortableInstallation
                    ? "Preparing the portable updater…"
                    : "Starting the verified installer…",
                BytesReceived = asset.Size,
                TotalBytes = asset.Size
            });

            if (update.IsPortableInstallation)
                LaunchPortableUpdater(packagePath);
            else
                LaunchInstaller(packagePath);
        }
        finally
        {
            if (File.Exists(partialPath))
                File.Delete(partialPath);
        }
    }

    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    internal static IReadOnlyList<ApplicationUpdateRelease> ParseAvailableReleases(
        string json,
        string currentVersion,
        bool includePrereleases)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var current))
            throw new InvalidDataException($"The current application version '{currentVersion}' is invalid.");

        var source = JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? [];
        return source
            .Where(release => !release.Draft && (includePrereleases || !release.Prerelease))
            .Select(ToApplicationRelease)
            .Select(release => new
            {
                Release = release,
                Valid = ReleaseVersion.TryParse(release.Tag, out var parsed),
                Version = parsed
            })
            .Where(item => item.Valid && item.Version.CompareTo(current) > 0)
            .OrderBy(item => item.Version)
            .Select(item => item.Release)
            .ToArray();
    }

    internal static string? ParseChecksumFile(string contents, string assetName)
    {
        using var reader = new StringReader(contents ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            var match = Regex.Match(
                line,
                "^\\s*(?<hash>[0-9a-fA-F]{64})\\s+[*]?(?<name>.+?)\\s*$",
                RegexOptions.CultureInvariant);
            if (match.Success && string.Equals(
                    match.Groups["name"].Value,
                    assetName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["hash"].Value.ToLowerInvariant();
            }
        }

        return null;
    }

    internal static bool IsPortableInstallation(string baseDirectory)
    {
        try
        {
            return !Directory.EnumerateFiles(
                Path.GetFullPath(baseDirectory),
                "unins*.exe",
                SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            // A directory that cannot expose an Inno Setup uninstaller must not
            // be treated as a managed installation.
            return true;
        }
    }

    internal static string GetCurrentVersionTag()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ApplicationUpdateService).Assembly;
        var version = assembly.GetName().Version ?? new Version(0, 0, 0);
        var core = string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}");
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return informational?.Contains("preview", StringComparison.OrdinalIgnoreCase) == true
            ? $"v{core}-preview"
            : $"v{core}";
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("KParser-Sanctum-Edition", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private async Task DownloadAssetAsync(
        ApplicationUpdateAsset asset,
        string destination,
        IProgress<ApplicationUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            asset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report(new ApplicationUpdateProgress
            {
                Status = $"Downloading {asset.Name}…",
                BytesReceived = received,
                TotalBytes = total
            });
        }

        if (asset.Size > 0 && received != asset.Size)
            throw new InvalidDataException("The downloaded update size did not match the published asset size.");
    }

    private async Task<string> ResolveExpectedSha256Async(
        ApplicationUpdateRelease release,
        ApplicationUpdateAsset asset,
        CancellationToken cancellationToken)
    {
        const string digestPrefix = "sha256:";
        if (asset.Digest?.StartsWith(digestPrefix, StringComparison.OrdinalIgnoreCase) == true)
        {
            var digest = asset.Digest[digestPrefix.Length..].Trim();
            if (Regex.IsMatch(digest, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
                return digest.ToLowerInvariant();
        }

        var checksumAsset = release.Assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (checksumAsset is null)
        {
            throw new InvalidDataException(
                "This release does not publish a verifiable SHA-256 checksum. " +
                "Use the GitHub Releases page to review it manually.");
        }

        if (!Uri.TryCreate(checksumAsset.DownloadUrl, UriKind.Absolute, out var checksumUri) ||
            !string.Equals(checksumUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The release contains an unsafe checksum download URL.");
        }

        var checksumText = await httpClient.GetStringAsync(
            checksumAsset.DownloadUrl,
            cancellationToken);
        return ParseChecksumFile(checksumText, asset.Name)
               ?? throw new InvalidDataException(
                   $"SHA256SUMS.txt does not contain a checksum for {asset.Name}.");
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void LaunchInstaller(string packagePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = packagePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(packagePath)!
        };
        startInfo.ArgumentList.Add("/SP-");
        startInfo.ArgumentList.Add("/SILENT");
        startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
        startInfo.ArgumentList.Add("/NORESTART");
        startInfo.ArgumentList.Add("/NORESTARTAPPLICATIONS");
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The verified update installer did not start.");
    }

    private static void LaunchPortableUpdater(string packagePath)
    {
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
            throw new InvalidOperationException("KParser could not locate its portable executable.");

        var helperDirectory = Path.Combine(
            Path.GetTempPath(),
            "KParserSanctumUpdater-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(helperDirectory);
        var helperPath = Path.Combine(helperDirectory, "KParser-Sanctum-Updater.exe");
        File.Copy(currentExecutable, helperPath, true);

        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = false,
            WorkingDirectory = helperDirectory
        };
        startInfo.ArgumentList.Add("--apply-portable-update");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);
        startInfo.ArgumentList.Add(Path.GetFileName(currentExecutable));
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The portable update helper did not start.");
    }

    private static ApplicationUpdateRelease ToApplicationRelease(GitHubRelease release) => new()
    {
        Tag = release.TagName?.Trim() ?? string.Empty,
        Name = string.IsNullOrWhiteSpace(release.Name)
            ? release.TagName?.Trim() ?? "KParser update"
            : release.Name.Trim(),
        Notes = release.Body?.Trim() ?? string.Empty,
        ReleaseUrl = release.HtmlUrl?.Trim() ?? string.Empty,
        PublishedAt = release.PublishedAt,
        IsPrerelease = release.Prerelease,
        Assets = release.Assets.Select(asset => new ApplicationUpdateAsset
        {
            Name = asset.Name?.Trim() ?? string.Empty,
            DownloadUrl = asset.BrowserDownloadUrl?.Trim() ?? string.Empty,
            Size = asset.Size,
            Digest = asset.Digest?.Trim()
        }).Where(asset => asset.Name.Length > 0 && asset.DownloadUrl.Length > 0).ToArray()
    };

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "update" : safe;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}

internal readonly struct ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly Version core;
    private readonly string[] prereleaseParts;

    private ReleaseVersion(Version core, string[] prereleaseParts)
    {
        this.core = core;
        this.prereleaseParts = prereleaseParts;
    }

    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        var metadataIndex = text.IndexOf('+');
        if (metadataIndex >= 0)
            text = text[..metadataIndex];
        var prereleaseIndex = text.IndexOf('-');
        var coreText = prereleaseIndex >= 0 ? text[..prereleaseIndex] : text;
        var prerelease = prereleaseIndex >= 0 ? text[(prereleaseIndex + 1)..] : string.Empty;
        if (!Version.TryParse(coreText, out var parsed) || parsed.Major < 0 || parsed.Minor < 0)
            return false;

        var normalized = new Version(
            parsed.Major,
            parsed.Minor,
            Math.Max(0, parsed.Build),
            Math.Max(0, parsed.Revision));
        var parts = prerelease.Length == 0
            ? []
            : prerelease.Split(['.', '-'], StringSplitOptions.RemoveEmptyEntries);
        version = new ReleaseVersion(normalized, parts);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var coreComparison = core.CompareTo(other.core);
        if (coreComparison != 0)
            return coreComparison;
        if (prereleaseParts.Length == 0)
            return other.prereleaseParts.Length == 0 ? 0 : 1;
        if (other.prereleaseParts.Length == 0)
            return -1;

        for (var index = 0; index < Math.Min(prereleaseParts.Length, other.prereleaseParts.Length); index++)
        {
            var left = prereleaseParts[index];
            var right = other.prereleaseParts[index];
            var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            int comparison;
            if (leftNumeric && rightNumeric)
                comparison = leftNumber.CompareTo(rightNumber);
            else if (leftNumeric != rightNumeric)
                comparison = leftNumeric ? -1 : 1;
            else
                comparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            if (comparison != 0)
                return comparison;
        }

        return prereleaseParts.Length.CompareTo(other.prereleaseParts.Length);
    }
}

internal static class ApplicationReleaseNotesFormatter
{
    private static readonly Regex LinkPattern = new(
        "\\[(?<text>[^]]+)\\]\\((?<url>[^)]+)\\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string ToDisplayText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "No release notes were provided for this update.";

        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal);
        text = LinkPattern.Replace(text, match =>
            $"{match.Groups["text"].Value} ({match.Groups["url"].Value})");
        var lines = text.Split('\n').Select(line =>
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
                return trimmed.TrimStart('#', ' ');
            return line;
        });
        return string.Join(Environment.NewLine, lines).Trim();
    }
}
