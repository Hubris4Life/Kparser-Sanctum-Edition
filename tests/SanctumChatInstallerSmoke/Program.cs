using KParser.Sanctum.UI.Services;

var testRoot = Path.Combine(
    Path.GetTempPath(),
    "KParser-SanctumChatInstallerSmoke-" + Guid.NewGuid().ToString("N"));
var sourceDirectory = Path.Combine(testRoot, "bundled", "sanctumchat");
var ashitaRoot = Path.Combine(testRoot, "Ashita v4");
var addonsDirectory = Path.Combine(ashitaRoot, "addons");

try
{
    Directory.CreateDirectory(sourceDirectory);
    Directory.CreateDirectory(addonsDirectory);
    File.WriteAllText(Path.Combine(ashitaRoot, "Ashita-cli.exe"), string.Empty);
    File.WriteAllText(Path.Combine(sourceDirectory, "README.md"), "SanctumChat smoke payload");
    WriteAddonVersion(sourceDirectory, "0.2.4");

    var service = new SanctumChatInstallerService(sourceDirectory);
    Require(service.IsBundledAddonAvailable, "The bundled addon was not recognized.");
    Require(service.BundledVersion == "0.2.4", "The bundled addon version was not read.");

    var initial = service.InspectPath(ashitaRoot);
    Require(!initial.IsInstalled, "A fresh Ashita test folder reported an installed addon.");
    Require(
        service.DetectInstallations(ashitaRoot).Any(location =>
            string.Equals(location.AshitaRoot, ashitaRoot, StringComparison.OrdinalIgnoreCase)),
        "The preferred Ashita installation was not detected.");

    var firstInstall = service.InstallOrUpdate(ashitaRoot);
    Require(firstInstall.Location.IsInstalled, "The addon was not installed.");
    Require(firstInstall.Location.InstalledVersion == "0.2.4", "The installed version was incorrect.");
    Require(firstInstall.BackupDirectory is null, "A first install unexpectedly created a backup.");

    WriteAddonVersion(sourceDirectory, "0.2.5");
    var update = service.InstallOrUpdate(Path.Combine(ashitaRoot, "addons"));
    Require(update.Location.InstalledVersion == "0.2.5", "The addon was not updated.");
    Require(
        update.BackupDirectory is not null && Directory.Exists(update.BackupDirectory),
        "The previous addon was not preserved during update.");

    var removedDirectory = service.MoveInstalledAddonAside(update.Location.AddonDirectory);
    Require(Directory.Exists(removedDirectory), "The recoverable removal folder was not created.");
    Require(!Directory.Exists(update.Location.AddonDirectory), "The active addon folder still exists after removal.");

    Console.WriteLine("sanctumchat-installer=verified");
}
finally
{
    if (Directory.Exists(testRoot))
        Directory.Delete(testRoot, recursive: true);
}

static void WriteAddonVersion(string directory, string version)
{
    File.WriteAllText(
        Path.Combine(directory, "sanctumchat.lua"),
        "addon = addon or {}\naddon.version = '" + version + "';\n");
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
