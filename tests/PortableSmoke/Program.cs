using System.Reflection;

if (args.Length != 2)
    throw new ArgumentException("Expected the portable assembly path and extraction root.");

var assemblyPath = Path.GetFullPath(args[0]);
var extractionRoot = Path.GetFullPath(args[1]);
Environment.SetEnvironmentVariable("KPARSER_SANCTUM_PORTABLE_ENGINE_DIR", extractionRoot);

var assembly = Assembly.LoadFrom(assemblyPath);
var managerType = assembly.GetType(
    "KParser.Sanctum.UI.Services.EngineProcessManager",
    throwOnError: true)!;
var extractionMethod = managerType.GetMethod(
    "ExtractEmbeddedEngine",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new MissingMethodException(managerType.FullName, "ExtractEmbeddedEngine");

var enginePath = extractionMethod.Invoke(null, null) as string;
if (string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath))
    throw new InvalidOperationException("The portable engine was not extracted.");

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
