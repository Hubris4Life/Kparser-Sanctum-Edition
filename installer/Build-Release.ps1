[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modernProject = Join-Path $repoRoot 'src\modern-ui\KParser.Sanctum.UI\KParser.Sanctum.UI.csproj'

[xml]$modernProjectXml = Get-Content -LiteralPath $modernProject -Raw
$applicationVersion = [string](@($modernProjectXml.Project.PropertyGroup.Version) |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    Select-Object -First 1)
$displayVersion = [string](@($modernProjectXml.Project.PropertyGroup.InformationalVersion) |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($applicationVersion) -or [string]::IsNullOrWhiteSpace($displayVersion))
{
    throw 'The modern application version metadata could not be read.'
}
$releaseTag = if ($displayVersion -match '(?i)preview')
{
    'v{0}-preview' -f $applicationVersion
}
else
{
    'v{0}' -f $applicationVersion
}
$assetVersionLabel = ($displayVersion -replace '[^A-Za-z0-9]+', '-').Trim('-')
$setupBaseName = 'KParser-Sanctum-Setup-{0}' -f $assetVersionLabel
$portableBaseName = 'KParser-Sanctum-Portable-{0}' -f $assetVersionLabel
$numericFileVersion = '{0}.0' -f $applicationVersion
$artifactRoot = Join-Path $repoRoot ('artifacts\{0}' -f $releaseTag)
$payloadRoot = Join-Path $PSScriptRoot 'payload\current'
$engineRoot = Join-Path $payloadRoot 'Engine'
$installerOutput = Join-Path $PSScriptRoot 'output'
$legacySolution = Join-Path $repoRoot 'src\legacy-engine\FFXILogParser.sln'
$legacyOutput = Join-Path $repoRoot 'src\legacy-engine\FFXILogParser\bin\x86\Release'
$engineArchive = Join-Path $repoRoot 'src\modern-ui\KParser.Sanctum.UI\Assets\EnginePayload.zip'
$kParserBridgeRoot = Join-Path $repoRoot 'addons\kparserbridge'
$kParserBridgeEntry = Join-Path $kParserBridgeRoot 'kparserbridge.lua'
$sqlCePrivateX86 = 'C:\Program Files\Microsoft SQL Server Compact Edition\v4.0\Private\x86'

function Assert-PathWithinRepo([string] $path)
{
    $fullPath = [System.IO.Path]::GetFullPath($path)
    $rootWithSeparator = $repoRoot.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to modify a release path outside the repository: $fullPath"
    }

    return $fullPath
}

function Reset-ReleaseDirectory([string] $path)
{
    $safePath = Assert-PathWithinRepo $path
    if (Test-Path -LiteralPath $safePath)
    {
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $safePath | Out-Null
}

function Copy-DirectoryContents([string] $source, [string] $destination)
{
    if (-not (Test-Path -LiteralPath $source))
    {
        throw "Required directory not found: $source"
    }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
}

function Assert-ReleaseDocumentVersion([string] $path, [string] $expectedText)
{
    $contents = Get-Content -LiteralPath $path -Raw
    if ($contents.IndexOf($expectedText, [System.StringComparison]::OrdinalIgnoreCase) -lt 0)
    {
        throw "Release document $path does not mention the expected version '$expectedText'."
    }
}

function Find-MSBuild
{
    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
    )

    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

function Find-InnoCompiler
{
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )

    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

function Find-SevenZip
{
    $candidates = @(
        'C:\Program Files\7-Zip\7z.exe',
        'C:\Program Files (x86)\7-Zip\7z.exe'
    )

    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

function Stage-ReleaseDocuments([string] $destination)
{
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README-FIRST.txt') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SOURCE-CODE.txt') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $destination 'LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'NOTICE.md') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'MODIFICATIONS.md') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\THIRD-PARTY-NOTICES.md') -Destination $destination -Force

    $licenseRoot = Join-Path $destination 'Licenses'
    Reset-ReleaseDirectory $licenseRoot
    Copy-DirectoryContents (Join-Path $repoRoot 'third_party\ZedGraph-5.1.5') (Join-Path $licenseRoot 'ZedGraph-5.1.5')
    Copy-DirectoryContents (Join-Path $repoRoot 'third_party\Microsoft-SQL-Server-Compact-4.0') (Join-Path $licenseRoot 'Microsoft-SQL-Server-Compact-4.0')
    Copy-DirectoryContents (Join-Path $repoRoot 'third_party\Microsoft-VC90-CRT') (Join-Path $licenseRoot 'Microsoft-VC90-CRT')
    Copy-DirectoryContents (Join-Path $repoRoot 'third_party\Microsoft-dotnet') (Join-Path $licenseRoot 'Microsoft-dotnet')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src\legacy-engine\Documentation\readme.txt') -Destination (Join-Path $licenseRoot 'KParser-original-readme.txt') -Force
}

function Stage-OptionalAddons([string] $destination)
{
    $addonDestination = Join-Path $destination 'Addons\kparserbridge'
    Copy-DirectoryContents $kParserBridgeRoot $addonDestination

    foreach ($requiredFile in @('kparserbridge.lua', 'README.md'))
    {
        if (-not (Test-Path -LiteralPath (Join-Path $addonDestination $requiredFile)))
        {
            throw "The staged KParserBridge addon is missing $requiredFile."
        }
    }
}

function Get-KParserBridgeVersion
{
    if (-not (Test-Path -LiteralPath $kParserBridgeEntry))
    {
        throw 'The KParserBridge addon entry file was not found.'
    }

    $source = Get-Content -LiteralPath $kParserBridgeEntry -Raw
    $match = [regex]::Match($source, 'addon\.version\s*=\s*[''\"](?<version>[^''\"]+)[''\"]')
    if (-not $match.Success)
    {
        throw 'The KParserBridge addon version could not be read.'
    }

    return $match.Groups['version'].Value
}

$msbuild = Find-MSBuild
if (-not $msbuild)
{
    throw 'MSBuild with the .NET Framework desktop targets was not found.'
}

$innoCompiler = Find-InnoCompiler
if (-not $innoCompiler)
{
    throw 'Inno Setup 6 or 7 was not found.'
}

$sevenZip = Find-SevenZip
if (-not $sevenZip)
{
    throw '7-Zip was not found. It is required for the optimized portable release archives.'
}

if (-not (Test-Path -LiteralPath $sqlCePrivateX86))
{
    throw 'The SQL Server Compact 4.0 SP1 x86 private runtime was not found.'
}

Assert-ReleaseDocumentVersion (Join-Path $PSScriptRoot 'RELEASE-NOTES.md') $displayVersion
Assert-ReleaseDocumentVersion (Join-Path $PSScriptRoot 'README-FIRST.txt') $displayVersion
Assert-ReleaseDocumentVersion (Join-Path $PSScriptRoot 'SOURCE-CODE.txt') $releaseTag

Write-Host 'Restoring modern Windows runtime dependencies...'
& dotnet restore $modernProject --runtime win-x64
if ($LASTEXITCODE -ne 0)
{
    throw "Modern runtime restore failed with exit code $LASTEXITCODE."
}

Write-Host 'Building x86 legacy engine...'
& $msbuild $legacySolution /t:Build /p:Configuration=Release /p:Platform=x86 /m /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0)
{
    throw "Legacy engine build failed with exit code $LASTEXITCODE."
}

Reset-ReleaseDirectory $artifactRoot
Reset-ReleaseDirectory $payloadRoot
Reset-ReleaseDirectory $installerOutput

Copy-DirectoryContents $legacyOutput $engineRoot
Copy-DirectoryContents $sqlCePrivateX86 (Join-Path $engineRoot 'x86')

$forbiddenDependencies = Get-ChildItem -LiteralPath $engineRoot -Recurse -File | Where-Object {
    $_.Name -in @('clrzmq.dll', 'clrzmq-ext.dll', 'libzmq.dll')
}
if ($forbiddenDependencies)
{
    throw 'The release engine unexpectedly contains a removed ZeroMQ dependency.'
}

$setupPublish = Join-Path $artifactRoot 'setup-publish'
Reset-ReleaseDirectory $setupPublish
Write-Host 'Publishing self-contained setup dashboard...'
& dotnet publish $modernProject --configuration Release --runtime win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:PublishReadyToRun=false -p:SatelliteResourceLanguages=en -p:DebugType=None -p:DebugSymbols=false --output $setupPublish
if ($LASTEXITCODE -ne 0)
{
    throw "Setup dashboard publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $setupPublish 'KParser-Sanctum-Modern.exe') -Destination $payloadRoot -Force
Stage-ReleaseDocuments $payloadRoot
Stage-OptionalAddons $payloadRoot

Write-Host 'Compiling setup executable...'
$innoArguments = @(
    ('/DMyAppVersion="{0}"' -f $displayVersion),
    ('/DMyAppNumericVersion="{0}"' -f $numericFileVersion),
    ('/DMyOutputBaseFilename="{0}"' -f $setupBaseName),
    (Join-Path $PSScriptRoot 'KParser-Sanctum.iss')
)
& $innoCompiler @innoArguments
if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$setupSource = Join-Path $installerOutput ($setupBaseName + '.exe')
$setupAsset = Join-Path $artifactRoot ($setupBaseName + '.exe')
Copy-Item -LiteralPath $setupSource -Destination $setupAsset -Force

$portablePublish = Join-Path $artifactRoot 'portable-publish'
$portablePackage = Join-Path $artifactRoot 'portable-package'
Reset-ReleaseDirectory $portablePublish
Reset-ReleaseDirectory $portablePackage

try
{
    if (Test-Path -LiteralPath $engineArchive)
    {
        Remove-Item -LiteralPath (Assert-PathWithinRepo $engineArchive) -Force
    }

    Compress-Archive -Path (Join-Path $engineRoot '*') -DestinationPath $engineArchive -CompressionLevel Optimal

    Write-Host 'Publishing portable single-file dashboard...'
    & dotnet publish $modernProject --configuration Release --runtime win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:SanctumPortable=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:PublishReadyToRun=false -p:SatelliteResourceLanguages=en -p:DebugType=None -p:DebugSymbols=false --output $portablePublish
    if ($LASTEXITCODE -ne 0)
    {
        throw "Portable dashboard publish failed with exit code $LASTEXITCODE."
    }
}
finally
{
    if (Test-Path -LiteralPath $engineArchive)
    {
        Remove-Item -LiteralPath (Assert-PathWithinRepo $engineArchive) -Force
    }
}

Copy-Item -LiteralPath (Join-Path $portablePublish 'KParser-Sanctum-Modern.exe') -Destination $portablePackage -Force
Stage-ReleaseDocuments $portablePackage
Stage-OptionalAddons $portablePackage

$portableAsset = Join-Path $artifactRoot ($portableBaseName + '.zip')
$portableCompactAsset = Join-Path $artifactRoot ($portableBaseName + '.7z')
Push-Location $portablePackage
try
{
    & $sevenZip a -tzip $portableAsset '.\*' -mx=9 -mm=Deflate -mfb=258 -mpass=15
    if ($LASTEXITCODE -ne 0)
    {
        throw "Portable ZIP creation failed with exit code $LASTEXITCODE."
    }

    & $sevenZip a -t7z $portableCompactAsset '.\*' -mx=9 -m0=lzma2 -ms=on
    if ($LASTEXITCODE -ne 0)
    {
        throw "Compact portable archive creation failed with exit code $LASTEXITCODE."
    }
}
finally
{
    Pop-Location
}

$kParserBridgeVersion = Get-KParserBridgeVersion
$kParserBridgeAsset = Join-Path $artifactRoot ("KParserBridge-Ashita4-v{0}.zip" -f $kParserBridgeVersion)
Push-Location (Join-Path $repoRoot 'addons')
try
{
    & $sevenZip a -tzip $kParserBridgeAsset '.\kparserbridge' -mx=9 -mm=Deflate
    if ($LASTEXITCODE -ne 0)
    {
        throw "KParserBridge ZIP creation failed with exit code $LASTEXITCODE."
    }
}
finally
{
    Pop-Location
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RELEASE-NOTES.md') -Destination $artifactRoot -Force

$checksumAsset = Join-Path $artifactRoot 'SHA256SUMS.txt'
$checksumEntries = @($setupAsset, $portableAsset, $portableCompactAsset, $kParserBridgeAsset) | ForEach-Object {
    $file = Get-Item -LiteralPath $_
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName
    [pscustomobject]@{
        Name = $file.Name
        Size = $file.Length
        Sha256 = $hash.Hash.ToLowerInvariant()
    }
}
$checksumLines = $checksumEntries | ForEach-Object { '{0}  {1}' -f $_.Sha256, $_.Name }
$checksumLines | Set-Content -LiteralPath $checksumAsset -Encoding ascii

$updateManifestAsset = Join-Path $artifactRoot 'update-manifest.json'
$updateManifest = [ordered]@{
    SchemaVersion = 1
    Version = $releaseTag
    DisplayVersion = $displayVersion
    ReleaseNotesAsset = 'RELEASE-NOTES.md'
    ChecksumAsset = 'SHA256SUMS.txt'
    Assets = @($checksumEntries | ForEach-Object {
        [ordered]@{
            Name = $_.Name
            Size = $_.Size
            Sha256 = $_.Sha256
        }
    })
}
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText(
    $updateManifestAsset,
    ($updateManifest | ConvertTo-Json -Depth 4),
    $utf8WithoutBom)

Write-Host "SETUP=$setupAsset"
Write-Host "PORTABLE=$portableAsset"
Write-Host "PORTABLE_COMPACT=$portableCompactAsset"
Write-Host "KPARSERBRIDGE=$kParserBridgeAsset"
Write-Host "CHECKSUMS=$checksumAsset"
Write-Host "UPDATE_MANIFEST=$updateManifestAsset"
