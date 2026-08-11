using System.IO;
using System.IO.Compression;
using System.Net.Http;
using KParser.Sanctum.UI.Models;
using KParser.Sanctum.UI.Services;

VerifyVersionOrdering();
VerifyReleaseParsingAndAssetSelection();
VerifyChecksumsAndReleaseNotes();
VerifyPortablePackageApplication();

Console.WriteLine("update-version-ordering=verified");
Console.WriteLine("update-release-selection=verified");
Console.WriteLine("update-checksum-parsing=verified");
Console.WriteLine("portable-update-rollback-boundary=verified");

static void VerifyVersionOrdering()
{
    var orderedTags = new[]
    {
        "v0.24.0-preview",
        "v0.24.0-preview.2",
        "v0.24.0",
        "v0.25.0-preview",
        "v1.0.0"
    };
    var versions = orderedTags.Select(tag =>
    {
        if (!ReleaseVersion.TryParse(tag, out var version))
            throw new InvalidOperationException("Could not parse " + tag);
        return version;
    }).ToArray();

    for (var index = 1; index < versions.Length; index++)
    {
        if (versions[index - 1].CompareTo(versions[index]) >= 0)
            throw new InvalidOperationException("Release version ordering is incorrect.");
    }

    if (ReleaseVersion.TryParse("preview-whatever", out _))
        throw new InvalidOperationException("An invalid release tag was accepted.");
}

static void VerifyReleaseParsingAndAssetSelection()
{
    const string json = """
    [
      {
        "tag_name": "v0.26.0-preview",
        "name": "Preview 26",
        "body": "# Preview 26\n\nLatest changes",
        "html_url": "https://example.invalid/26",
        "draft": false,
        "prerelease": true,
        "published_at": "2026-08-10T12:00:00Z",
        "assets": [
          { "name": "KParser-Sanctum-Setup-Preview-26.exe", "browser_download_url": "https://example.invalid/setup", "size": 1200, "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          { "name": "KParser-Sanctum-Portable-Preview-26.zip", "browser_download_url": "https://example.invalid/portable", "size": 900, "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
          { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.invalid/checksums", "size": 200, "digest": null }
        ]
      },
      {
        "tag_name": "v0.25.0-preview",
        "name": "Preview 25",
        "body": "Earlier changes",
        "html_url": "https://example.invalid/25",
        "draft": false,
        "prerelease": true,
        "published_at": "2026-08-09T12:00:00Z",
        "assets": []
      },
      {
        "tag_name": "v0.27.0",
        "name": "Stable 27",
        "body": "Stable changes",
        "html_url": "https://example.invalid/27",
        "draft": false,
        "prerelease": false,
        "published_at": "2026-08-11T12:00:00Z",
        "assets": []
      },
      {
        "tag_name": "v9.0.0",
        "name": "Unpublished draft",
        "body": "Must not be offered",
        "html_url": "https://example.invalid/draft",
        "draft": true,
        "prerelease": false,
        "published_at": null,
        "assets": []
      }
    ]
    """;

    var allChannels = ApplicationUpdateService.ParseAvailableReleases(
        json,
        "v0.24.0-preview",
        true);
    if (allChannels.Count != 3 ||
        allChannels[0].Tag != "v0.25.0-preview" ||
        allChannels[1].Tag != "v0.26.0-preview" ||
        allChannels[2].Tag != "v0.27.0")
    {
        throw new InvalidOperationException("Missed releases were not ordered correctly.");
    }

    var stableOnly = ApplicationUpdateService.ParseAvailableReleases(
        json,
        "v0.24.0-preview",
        false);
    if (stableOnly.Count != 1 || stableOnly[0].Tag != "v0.27.0")
        throw new InvalidOperationException("The stable update channel included a preview.");

    using var service = new ApplicationUpdateService(new HttpClient());
    var setupUpdate = new ApplicationUpdateCheckResult
    {
        CurrentVersion = "v0.24.0-preview",
        IsPortableInstallation = false,
        AvailableReleases = [allChannels[1]]
    };
    var portableUpdate = new ApplicationUpdateCheckResult
    {
        CurrentVersion = "v0.24.0-preview",
        IsPortableInstallation = true,
        AvailableReleases = [allChannels[1]]
    };
    if (service.SelectPackageAsset(setupUpdate)?.Name != "KParser-Sanctum-Setup-Preview-26.exe" ||
        service.SelectPackageAsset(portableUpdate)?.Name != "KParser-Sanctum-Portable-Preview-26.zip")
    {
        throw new InvalidOperationException("The updater chose the wrong release package.");
    }
}

static void VerifyChecksumsAndReleaseNotes()
{
    var expected = new string('a', 64);
    var checksumFile = $"{expected}  KParser-Sanctum-Setup.exe\n" +
                       $"{new string('b', 64)} *KParser-Sanctum-Portable.zip\n";
    if (ApplicationUpdateService.ParseChecksumFile(
            checksumFile,
            "KParser-Sanctum-Setup.exe") != expected)
    {
        throw new InvalidOperationException("The setup checksum was not parsed.");
    }

    var notes = ApplicationReleaseNotesFormatter.ToDisplayText(
        "# Changes\n\n- Added **updates**\n- See [release](https://example.invalid)");
    if (notes.Contains('#') || notes.Contains("**") ||
        !notes.Contains("Changes") || !notes.Contains("release (https://example.invalid)"))
    {
        throw new InvalidOperationException("Release notes were not formatted for display.");
    }
}

static void VerifyPortablePackageApplication()
{
    var root = Path.Combine(Path.GetTempPath(), "KParser-UpdateSmoke-" + Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var target = Path.Combine(root, "target");
    var package = Path.Combine(root, "update.zip");
    try
    {
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "KParser.exe"), "new executable");
        Directory.CreateDirectory(Path.Combine(source, "Addons"));
        File.WriteAllText(Path.Combine(source, "Addons", "new.txt"), "new addon");
        File.WriteAllText(Path.Combine(target, "KParser.exe"), "old executable");
        File.WriteAllText(Path.Combine(target, "personal-parse.txt"), "preserve me");
        ZipFile.CreateFromDirectory(source, package);

        // ApplyPackage normally restarts the new executable, so validate its
        // guarded extraction boundary separately with an intentionally missing
        // executable name. No target files may change when validation fails.
        try
        {
            PortableUpdateApplier.ApplyPackage(package, target, "Missing.exe");
            throw new InvalidOperationException("An invalid portable package was accepted.");
        }
        catch (InvalidDataException)
        {
        }

        if (File.ReadAllText(Path.Combine(target, "KParser.exe")) != "old executable" ||
            File.ReadAllText(Path.Combine(target, "personal-parse.txt")) != "preserve me")
        {
            throw new InvalidOperationException("Portable validation changed existing user files.");
        }

        PortableUpdateApplier.ApplyPackage(package, target, "KParser.exe", false);
        if (File.ReadAllText(Path.Combine(target, "KParser.exe")) != "new executable" ||
            File.ReadAllText(Path.Combine(target, "Addons", "new.txt")) != "new addon" ||
            File.ReadAllText(Path.Combine(target, "personal-parse.txt")) != "preserve me")
        {
            throw new InvalidOperationException(
                "The portable updater did not replace packaged files while preserving user files.");
        }
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
