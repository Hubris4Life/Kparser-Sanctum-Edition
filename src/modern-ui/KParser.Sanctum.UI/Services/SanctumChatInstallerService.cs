using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace KParser.Sanctum.UI.Services;

internal sealed record SanctumChatInstallLocation(
    string AshitaRoot,
    string AddonDirectory,
    bool IsInstalled,
    string InstalledVersion)
{
    public string DisplayName => IsInstalled
        ? $"{AshitaRoot}  (SanctumChat {InstalledVersion})"
        : $"{AshitaRoot}  (not installed)";
}

internal sealed record SanctumChatInstallResult(
    SanctumChatInstallLocation Location,
    string? BackupDirectory);

internal sealed class SanctumChatInstallerService
{
    private const string AddonFolderName = "sanctumchat";
    private static readonly Regex VersionExpression = new(
        "addon\\.version\\s*=\\s*['\"](?<version>[^'\"]+)['\"]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string bundledAddonDirectory;

    public SanctumChatInstallerService()
        : this(Path.Combine(AppContext.BaseDirectory, "Addons", AddonFolderName))
    {
    }

    internal SanctumChatInstallerService(string bundledAddonDirectory)
    {
        this.bundledAddonDirectory = Path.GetFullPath(bundledAddonDirectory);
    }

    public string BundledVersion => ReadAddonVersion(
        Path.Combine(bundledAddonDirectory, "sanctumchat.lua"));

    public bool IsBundledAddonAvailable =>
        File.Exists(Path.Combine(bundledAddonDirectory, "sanctumchat.lua")) &&
        File.Exists(Path.Combine(bundledAddonDirectory, "README.md"));

    public IReadOnlyList<SanctumChatInstallLocation> DetectInstallations(
        string? preferredPath = null)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddCandidate(candidates, preferredPath);

        AddCandidate(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ashita"));
        AddCandidate(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ashita"));
        AddCandidate(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Ashita"));
        AddCandidate(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Ashita"));
        AddCandidate(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Ashita"));
        AddCandidate(candidates, @"C:\Ashita");
        AddCandidate(candidates, @"C:\Ashita-v4");

        foreach (var processName in new[] { "Ashita", "Ashita-cli" })
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            AddCandidate(
                                candidates,
                                Path.GetDirectoryName(process.MainModule?.FileName));
                        }
                        catch (Exception)
                        {
                            // Some processes do not permit module-path inspection.
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Detection remains useful even if process enumeration is unavailable.
            }
        }

        return candidates
            .Where(LooksLikeAshitaRoot)
            .Select(InspectPath)
            .OrderByDescending(location => location.IsInstalled)
            .ThenBy(location => location.AshitaRoot, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public SanctumChatInstallLocation InspectPath(string selectedPath)
    {
        var ashitaRoot = NormalizeAshitaRoot(selectedPath);
        if (!LooksLikeAshitaRoot(ashitaRoot))
        {
            throw new InvalidOperationException(
                "That folder does not look like an Ashita v4 installation. " +
                "Select the folder containing Ashita.exe/Ashita-cli.exe or its addons folder.");
        }

        var addonsDirectory = Path.GetFullPath(Path.Combine(ashitaRoot, "addons"));
        var addonDirectory = Path.GetFullPath(Path.Combine(addonsDirectory, AddonFolderName));
        EnsureDirectChild(addonsDirectory, addonDirectory);
        var entryFile = Path.Combine(addonDirectory, "sanctumchat.lua");
        var installed = File.Exists(entryFile);
        return new SanctumChatInstallLocation(
            ashitaRoot,
            addonDirectory,
            installed,
            installed ? ReadAddonVersion(entryFile) : "not installed");
    }

    public SanctumChatInstallResult InstallOrUpdate(string selectedPath)
    {
        ValidateBundledAddon();
        var location = InspectPath(selectedPath);
        var addonsDirectory = Path.GetDirectoryName(location.AddonDirectory)
            ?? throw new InvalidOperationException("The Ashita addons directory could not be resolved.");
        Directory.CreateDirectory(addonsDirectory);

        var temporaryDirectory = Path.Combine(
            addonsDirectory,
            ".sanctumchat.install-" + Guid.NewGuid().ToString("N"));
        EnsureDirectChild(addonsDirectory, temporaryDirectory);
        string? backupDirectory = null;

        try
        {
            CopyDirectory(bundledAddonDirectory, temporaryDirectory);

            if (Directory.Exists(location.AddonDirectory))
            {
                backupDirectory = CreateAvailableRecoveryPath(
                    addonsDirectory,
                    "sanctumchat.backup");
                Directory.Move(location.AddonDirectory, backupDirectory);
            }

            try
            {
                Directory.Move(temporaryDirectory, location.AddonDirectory);
            }
            catch
            {
                if (backupDirectory is not null &&
                    Directory.Exists(backupDirectory) &&
                    !Directory.Exists(location.AddonDirectory))
                {
                    Directory.Move(backupDirectory, location.AddonDirectory);
                    backupDirectory = null;
                }

                throw;
            }
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }

        return new SanctumChatInstallResult(
            InspectPath(location.AshitaRoot),
            backupDirectory);
    }

    public string MoveInstalledAddonAside(string selectedPath)
    {
        var location = InspectPath(selectedPath);
        if (!Directory.Exists(location.AddonDirectory))
            throw new InvalidOperationException("SanctumChat is not installed in that Ashita location.");

        var addonsDirectory = Path.GetDirectoryName(location.AddonDirectory)
            ?? throw new InvalidOperationException("The Ashita addons directory could not be resolved.");
        var recoveryDirectory = CreateAvailableRecoveryPath(
            addonsDirectory,
            "sanctumchat.removed");
        Directory.Move(location.AddonDirectory, recoveryDirectory);
        return recoveryDirectory;
    }

    private void ValidateBundledAddon()
    {
        if (!IsBundledAddonAvailable)
        {
            throw new FileNotFoundException(
                "The bundled SanctumChat addon is incomplete. Reinstall KParser or use a complete portable package.",
                bundledAddonDirectory);
        }

        if (BundledVersion == "unknown")
            throw new InvalidDataException("The bundled SanctumChat version could not be read.");
    }

    private static string NormalizeAshitaRoot(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            throw new ArgumentException("Select an Ashita v4 installation folder.", nameof(selectedPath));

        var path = Path.GetFullPath(selectedPath.Trim());
        var directoryName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.Equals(directoryName, AddonFolderName, StringComparison.OrdinalIgnoreCase))
        {
            var addonsDirectory = Directory.GetParent(path)?.FullName;
            if (addonsDirectory is not null &&
                string.Equals(
                    Path.GetFileName(addonsDirectory.TrimEnd(Path.DirectorySeparatorChar)),
                    "addons",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Directory.GetParent(addonsDirectory)?.FullName ?? path;
            }
        }

        if (string.Equals(directoryName, "addons", StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(path)?.FullName ?? path;

        return path;
    }

    private static bool LooksLikeAshitaRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        return Directory.Exists(Path.Combine(path, "addons")) ||
               File.Exists(Path.Combine(path, "Ashita.exe")) ||
               File.Exists(Path.Combine(path, "Ashita-cli.exe"));
    }

    private static void AddCandidate(ISet<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            candidates.Add(NormalizeAshitaRoot(path));
        }
        catch (Exception)
        {
            // Ignore malformed or unavailable auto-detection candidates.
        }
    }

    private static void EnsureDirectChild(string parent, string child)
    {
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedChild = Path.GetFullPath(child);
        if (!normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
            normalizedChild.IndexOf(
                Path.DirectorySeparatorChar,
                normalizedParent.Length) >= 0)
        {
            throw new InvalidOperationException("Refusing to modify a folder outside the selected Ashita addons directory.");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The bundled addon contains an unsupported linked directory.");

            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static string CreateAvailableRecoveryPath(string parent, string prefix)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        for (var suffix = 0; suffix < 100; suffix++)
        {
            var name = suffix == 0
                ? $"{prefix}-{timestamp}"
                : $"{prefix}-{timestamp}-{suffix}";
            var candidate = Path.Combine(parent, name);
            EnsureDirectChild(parent, candidate);
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }

        throw new IOException("A recovery folder name could not be reserved.");
    }

    private static string ReadAddonVersion(string entryFile)
    {
        try
        {
            if (!File.Exists(entryFile))
                return "unknown";
            var match = VersionExpression.Match(File.ReadAllText(entryFile));
            return match.Success ? match.Groups["version"].Value.Trim() : "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
