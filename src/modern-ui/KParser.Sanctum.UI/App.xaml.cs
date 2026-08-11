using System.IO;
using System.Windows;
using KParser.Sanctum.UI.Services;

namespace KParser.Sanctum.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ApplicationDiagnostics.Initialize(this);

        string? updateHelperToClean = null;
        if (e.Args.Length == 2 &&
            string.Equals(
                e.Args[0],
                "--cleanup-update-helper",
                StringComparison.OrdinalIgnoreCase))
        {
            updateHelperToClean = e.Args[1];
        }

        if (e.Args.Length > 0 &&
            string.Equals(
                e.Args[0],
                "--apply-portable-update",
                StringComparison.OrdinalIgnoreCase))
        {
            var exitCode = PortableUpdateApplier.Run(e.Args, out var error);
            if (exitCode != 0)
            {
                MessageBox.Show(
                    error ?? "The portable update could not be applied.",
                    "KParser update failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            Shutdown(exitCode);
            return;
        }

        if (e.Args.Length == 2 &&
            string.Equals(
                e.Args[0],
                "--verify-portable-payload",
                StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable(
                "KPARSER_SANCTUM_PORTABLE_ENGINE_DIR",
                Path.GetFullPath(e.Args[1]));
            var enginePath = EngineProcessManager.ExtractEmbeddedEngine();
            Shutdown(string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath) ? 1 : 0);
            return;
        }

        base.OnStartup(e);
        if (!string.IsNullOrWhiteSpace(updateHelperToClean))
            _ = PortableUpdateApplier.CleanupHelperDirectoryAsync(updateHelperToClean);
    }
}
