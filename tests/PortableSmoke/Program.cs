using System.Diagnostics;

if (args.Length != 2)
    throw new ArgumentException("Expected the portable assembly path and extraction root.");

var portablePath = Path.GetFullPath(args[0]);
var extractionRoot = Path.GetFullPath(args[1]);
if (!File.Exists(portablePath))
    throw new FileNotFoundException("The portable executable was not found.", portablePath);

Directory.CreateDirectory(extractionRoot);
var startInfo = new ProcessStartInfo
{
    FileName = portablePath,
    UseShellExecute = true,
    Verb = "runas",
    WindowStyle = ProcessWindowStyle.Hidden
};
startInfo.ArgumentList.Add("--verify-portable-payload");
startInfo.ArgumentList.Add(extractionRoot);

using var process = Process.Start(startInfo)
    ?? throw new InvalidOperationException("The portable verification process did not start.");
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
await process.WaitForExitAsync(timeout.Token);
if (process.ExitCode != 0)
    throw new InvalidOperationException(
        $"The portable verification process exited with code {process.ExitCode}.");

var enginePath = Directory
    .EnumerateFiles(extractionRoot, "KParser-Sanctum.exe", SearchOption.AllDirectories)
    .SingleOrDefault()
    ?? throw new InvalidOperationException("The portable engine was not extracted.");

var requiredFiles = new[]
{
    "KParser-Sanctum.exe",
    "WaywardGamers.KParser.ParserCore.dll",
    Path.Combine("x86", "sqlceme40.dll"),
    Path.Combine("x86", "Microsoft.VC90.CRT", "msvcr90.dll")
};

var engineDirectory = Path.GetDirectoryName(enginePath)!;
foreach (var relativePath in requiredFiles)
{
    if (!File.Exists(Path.Combine(engineDirectory, relativePath)))
        throw new FileNotFoundException("Missing extracted engine file.", relativePath);
}

Console.WriteLine("portable-extraction=verified");
Console.WriteLine("engine-directory=" + engineDirectory);
