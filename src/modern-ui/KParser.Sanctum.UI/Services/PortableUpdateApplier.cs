using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace KParser.Sanctum.UI.Services;

internal static class PortableUpdateApplier
{
    public static int Run(string[] args, out string? error)
    {
        error = null;
        if (args.Length != 5 ||
            !string.Equals(args[0], "--apply-portable-update", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out var ownerProcessId))
        {
            error = "The portable updater received invalid arguments.";
            return 2;
        }

        try
        {
            WaitForOwnerToExit(ownerProcessId);
            ApplyPackage(args[1], args[2], args[3]);
            return 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            WriteFailureLog(ex);
            return 1;
        }
    }

    internal static void ApplyPackage(
        string packagePath,
        string targetDirectory,
        string executableName,
        bool restartApplication = true)
    {
        packagePath = Path.GetFullPath(packagePath);
        targetDirectory = Path.GetFullPath(targetDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!File.Exists(packagePath) ||
            !packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("The verified Portable ZIP update was not found.", packagePath);
        }

        if (string.IsNullOrWhiteSpace(executableName) ||
            !string.Equals(Path.GetFileName(executableName), executableName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The portable application filename is invalid.");
        }

        var workRoot = Path.Combine(
            Path.GetTempPath(),
            "KParserSanctumApply-" + Guid.NewGuid().ToString("N"));
        var stagingRoot = Path.Combine(workRoot, "staging");
        var backupRoot = Path.Combine(workRoot, "backup");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(backupRoot);
        var replacedFiles = new List<string>();
        var createdFiles = new List<string>();

        try
        {
            ExtractSafely(packagePath, stagingRoot);
            var stagedExecutable = Path.Combine(stagingRoot, executableName);
            if (!File.Exists(stagedExecutable))
            {
                throw new InvalidDataException(
                    $"The Portable ZIP does not contain {executableName} at its root.");
            }

            foreach (var sourcePath in Directory.EnumerateFiles(
                         stagingRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(stagingRoot, sourcePath);
                var destinationPath = GetSafeDestination(targetDirectory, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
                Directory.CreateDirectory(destinationDirectory);

                if (File.Exists(destinationPath))
                {
                    var backupPath = GetSafeDestination(backupRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(destinationPath, backupPath, true);
                    replacedFiles.Add(relativePath);
                }
                else
                {
                    createdFiles.Add(relativePath);
                }

                var temporaryDestination = destinationPath + ".kparser-update";
                File.Copy(sourcePath, temporaryDestination, true);
                File.Move(temporaryDestination, destinationPath, true);
            }
        }
        catch
        {
            RestoreFiles(targetDirectory, backupRoot, replacedFiles, createdFiles);
            throw;
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }

        if (!restartApplication)
            return;

        var updatedExecutable = Path.Combine(targetDirectory, executableName);
        var startInfo = new ProcessStartInfo
        {
            FileName = updatedExecutable,
            WorkingDirectory = targetDirectory,
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("--cleanup-update-helper");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The updated portable KParser did not restart.");
    }

    internal static async Task CleanupHelperDirectoryAsync(string path)
    {
        string helperDirectory;
        try
        {
            helperDirectory = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var tempRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!helperDirectory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(helperDirectory).StartsWith(
                    "KParserSanctumUpdater-",
                    StringComparison.Ordinal))
            {
                return;
            }
        }
        catch
        {
            return;
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                if (Directory.Exists(helperDirectory))
                    Directory.Delete(helperDirectory, true);
                return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(250);
        }
    }

    private static void WaitForOwnerToExit(int ownerProcessId)
    {
        try
        {
            using var owner = Process.GetProcessById(ownerProcessId);
            if (!owner.WaitForExit(60000))
                throw new TimeoutException("KParser did not close in time for the portable update.");
        }
        catch (ArgumentException)
        {
            // The process was already gone by the time the helper started.
        }
    }

    private static void ExtractSafely(string packagePath, string stagingRoot)
    {
        var safeRoot = stagingRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destination = Path.GetFullPath(Path.Combine(stagingRoot, entry.FullName));
            if (!destination.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The Portable ZIP contains an unsafe file path.");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
        }
    }

    private static string GetSafeDestination(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!destination.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update attempted to write outside its target folder.");
        return destination;
    }

    private static void RestoreFiles(
        string targetDirectory,
        string backupRoot,
        IEnumerable<string> replacedFiles,
        IEnumerable<string> createdFiles)
    {
        foreach (var relativePath in replacedFiles.Reverse())
        {
            try
            {
                File.Copy(
                    GetSafeDestination(backupRoot, relativePath),
                    GetSafeDestination(targetDirectory, relativePath),
                    true);
            }
            catch
            {
            }
        }

        foreach (var relativePath in createdFiles.Reverse())
        {
            try
            {
                File.Delete(GetSafeDestination(targetDirectory, relativePath));
            }
            catch
            {
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static void WriteFailureLog(Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KParser Sanctum Modern");
            Directory.CreateDirectory(logDirectory);
            File.WriteAllText(
                Path.Combine(logDirectory, "PortableUpdateError.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch
        {
        }
    }
}
