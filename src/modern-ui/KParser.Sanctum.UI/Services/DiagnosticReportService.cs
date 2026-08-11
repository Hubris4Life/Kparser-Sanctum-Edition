using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using KParser.Sanctum.UI.Models;

namespace KParser.Sanctum.UI.Services;

internal static class DiagnosticReportService
{
    public static ApplicationDiagnosticReport Create(ApplicationDiagnosticContext context)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var fileVersion = assembly.GetName().Version?.ToString() ?? "unknown";
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? fileVersion
            : $"{informationalVersion} ({fileVersion})";
        var installationMode = ApplicationUpdateService.IsPortableInstallation(AppContext.BaseDirectory)
            ? "Portable"
            : "Installed / development";

        var items = new List<ApplicationDiagnosticItem>();
        Add(items, "Application", "Version", version);
        Add(items, "Application", "Installation", installationMode);
        Add(items, "Application", "Runtime", RuntimeInformation.FrameworkDescription);
        Add(items, "Application", "Architecture", RuntimeInformation.ProcessArchitecture.ToString());
        Add(items, "Application", "Windows", RuntimeInformation.OSDescription);
        Add(items, "Application", "Application folder", RedactUserPath(AppContext.BaseDirectory));

        Add(items, "Parser", "Bridge", context.BridgeStatus);
        Add(items, "Parser", "Engine version", TextOrUnknown(context.EngineVersion));
        Add(items, "Parser", "Engine ownership", context.OwnsEngineProcess
            ? "Started and managed by this dashboard"
            : "Existing or unavailable engine");
        Add(items, "Parser", "Engine path", string.IsNullOrWhiteSpace(context.EnginePath)
            ? "Not located"
            : RedactUserPath(context.EnginePath));
        Add(items, "Parser", "Parser state", context.ParserRunning ? "Running" : "Stopped");
        Add(items, "Parser", "Combat database", context.DatabaseOpen ? "Open" : "Closed");
        Add(items, "Parser", "Parse mode", TextOrUnknown(context.ParseMode));

        Add(items, "FFXI", "Detected clients", GetDetectedGameClients());
        Add(items, "FFXI", "Memory detection", context.MemoryStatus);
        Add(items, "FFXI", "Auto-detect at startup", context.AutomaticMemoryDetection ? "Enabled" : "Disabled");
        Add(items, "FFXI", "Registered DoT player", string.IsNullOrWhiteSpace(context.RegisteredPlayer)
            ? "Not registered"
            : context.RegisteredPlayer);

        Add(items, "Compatibility", "Server profile",
            string.Equals(context.ServerProfile, "sanctum", StringComparison.OrdinalIgnoreCase)
                ? "Sanctum XI"
                : "Other");
        Add(items, "Compatibility", "Pet ownership", TextOrUnknown(context.PetOwnershipMode));
        Add(items, "Compatibility", "Unresolved pets", context.UnresolvedPetStatus);
        Add(items, "Compatibility", "Pet display", context.DisplayPetDamageSeparately
            ? "Separate pet rows"
            : "Included in owner totals");

        Add(items, "Preferences", "Theme", context.LightMode ? "Light" : "Dark");
        Add(items, "Preferences", "Update channel", context.IncludePrereleaseUpdates
            ? "Stable and preview releases"
            : "Stable releases only");

        Add(items, "Diagnostics", "Last bridge response",
            context.LastBridgeSuccessUtc?.ToLocalTime().ToString("g") ?? "No response this session");
        Add(items, "Diagnostics", "Last bridge error", string.IsNullOrWhiteSpace(context.LastBridgeError)
            ? "None recorded"
            : context.LastBridgeError);
        Add(items, "Diagnostics", "Application error log",
            RedactUserPath(ApplicationDiagnostics.ApplicationErrorLogPath));
        Add(items, "Diagnostics", "Generated", DateTimeOffset.Now.ToString("O"));

        return new ApplicationDiagnosticReport
        {
            Items = items,
            Text = BuildText(items)
        };
    }

    public static bool TryOpenLogDirectory(out string? error)
    {
        try
        {
            Directory.CreateDirectory(ApplicationDiagnostics.LogDirectoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = ApplicationDiagnostics.LogDirectoryPath,
                UseShellExecute = true
            });
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            ApplicationDiagnostics.LogHandledException("Open diagnostics folder", ex);
            error = ex.Message;
            return false;
        }
    }

    private static void Add(
        ICollection<ApplicationDiagnosticItem> items,
        string category,
        string name,
        string value) => items.Add(new ApplicationDiagnosticItem
    {
        Category = category,
        Name = name,
        Value = value
    });

    private static string BuildText(IEnumerable<ApplicationDiagnosticItem> items)
    {
        var builder = new StringBuilder("KParser - Sanctum Edition diagnostic report");
        builder.AppendLine();
        string? currentCategory = null;
        foreach (var item in items)
        {
            if (!string.Equals(currentCategory, item.Category, StringComparison.Ordinal))
            {
                if (currentCategory is not null)
                    builder.AppendLine();
                currentCategory = item.Category;
                builder.Append('[').Append(currentCategory).AppendLine("]");
            }

            builder.Append(item.Name).Append(": ").AppendLine(item.Value);
        }

        return builder.ToString().TrimEnd();
    }

    private static string GetDetectedGameClients()
    {
        var candidates = new[] { "xiloader", "horizon-loader", "pol", "ffximain" };
        var clients = new List<string>();
        foreach (var processName in candidates)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                        clients.Add($"{process.ProcessName}.exe (PID {process.Id})");
                }
            }
            catch
            {
                // A protected or exiting process should not block diagnostics.
            }
        }

        return clients.Count == 0
            ? "No supported client process detected"
            : string.Join(", ", clients.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string RedactUserPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var fullPath = Path.GetFullPath(path);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return userProfile.Length > 0 && fullPath.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase)
            ? "%USERPROFILE%" + fullPath[userProfile.Length..]
            : fullPath;
    }

    private static string TextOrUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
}
