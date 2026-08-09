using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace KParser.Sanctum.UI.Services;

internal sealed class EngineProcessManager : IDisposable
{
    private Process? engineProcess;

    public bool OwnsEngineProcess { get; private set; }
    public string? EnginePath { get; private set; }
    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KParser Sanctum Modern",
        "Data");

    public async Task<bool> EnsureRunningAsync(
        ParserBridgeClient bridgeClient,
        CancellationToken cancellationToken)
    {
        if (await IsBridgeAvailableAsync(bridgeClient, cancellationToken))
            return true;

        EnginePath = FindEnginePath();
        if (EnginePath is null)
            return false;

        Directory.CreateDirectory(DataDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = EnginePath,
            Arguments = "--sanctum-engine --owner-pid " + Environment.ProcessId +
                        " --data-directory \"" + DataDirectory + "\"",
            WorkingDirectory = Path.GetDirectoryName(EnginePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        engineProcess = Process.Start(startInfo);
        if (engineProcess is null)
            return false;

        OwnsEngineProcess = true;

        for (var attempt = 0; attempt < 24; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (engineProcess.HasExited)
                return false;

            if (await IsBridgeAvailableAsync(bridgeClient, cancellationToken))
                return true;

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    public async Task ShutdownAsync(ParserBridgeClient bridgeClient)
    {
        if (!OwnsEngineProcess || engineProcess is null || engineProcess.HasExited)
            return;

        try
        {
            using var commandTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await bridgeClient.SendCommandAsync("shutdown", commandTimeout.Token);
        }
        catch (Exception)
        {
            // The engine also watches its owner PID, so it will close itself if
            // the modern application exits before a response is available.
        }

        try
        {
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await engineProcess.WaitForExitAsync(exitTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Owner-PID monitoring remains the final graceful shutdown path.
        }
    }

    public void Dispose()
    {
        engineProcess?.Dispose();
        engineProcess = null;
    }

    private static async Task<bool> IsBridgeAvailableAsync(
        ParserBridgeClient bridgeClient,
        CancellationToken cancellationToken)
    {
        try
        {
            await bridgeClient.GetSnapshotAsync(cancellationToken);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string? FindEnginePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Engine", "KParser-Sanctum.exe"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "Legacy KParser Engine",
                "KParser-Sanctum.exe")),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "legacy-engine-src",
                "FFXILogParser",
                "bin",
                "x86",
                "Release",
                "KParser-Sanctum.exe")),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "..",
                "kparser-sanctum-src",
                "FFXILogParser",
                "bin",
                "x86",
                "Release",
                "KParser-Sanctum.exe"))
        };

        return candidates.FirstOrDefault(File.Exists) ?? ExtractEmbeddedEngine();
    }

    private static string? ExtractEmbeddedEngine()
    {
        const string resourceName = "KParser.Sanctum.EnginePayload.zip";
        using var payloadStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName);
        if (payloadStream is null)
            return null;

        using var payloadBuffer = new MemoryStream();
        payloadStream.CopyTo(payloadBuffer);
        var payload = payloadBuffer.ToArray();
        var payloadId = Convert.ToHexString(SHA256.HashData(payload))[..16];
        var configuredPortableRoot = Environment.GetEnvironmentVariable(
            "KPARSER_SANCTUM_PORTABLE_ENGINE_DIR");
        var portableRoot = string.IsNullOrWhiteSpace(configuredPortableRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KParser Sanctum Modern",
                "PortableEngine")
            : Path.GetFullPath(configuredPortableRoot);
        var engineDirectory = Path.Combine(portableRoot, payloadId);
        var enginePath = Path.Combine(engineDirectory, "KParser-Sanctum.exe");
        if (File.Exists(enginePath))
            return enginePath;

        Directory.CreateDirectory(portableRoot);
        var temporaryDirectory = Path.Combine(
            portableRoot,
            payloadId + ".tmp-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            ExtractEngineArchive(payload, temporaryDirectory);
            if (!File.Exists(Path.Combine(temporaryDirectory, "KParser-Sanctum.exe")))
                throw new InvalidDataException("The bundled parser engine is incomplete.");

            try
            {
                Directory.Move(temporaryDirectory, engineDirectory);
            }
            catch (IOException) when (File.Exists(enginePath))
            {
                // A second instance completed the same extraction first.
            }

            return File.Exists(enginePath) ? enginePath : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                try
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
                catch
                {
                    // A later launch can safely clean up or ignore an abandoned temp folder.
                }
            }
        }
    }

    private static void ExtractEngineArchive(byte[] payload, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archiveStream = new MemoryStream(payload, writable: false);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The bundled parser engine contains an unsafe path.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var input = entry.Open();
            using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }
}
