using System.IO;
using System.Windows;
using KParser.Sanctum.UI.Services;

namespace KParser.Sanctum.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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
    }
}
