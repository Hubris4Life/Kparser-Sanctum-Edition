[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $repoRoot 'artifacts\v0.23.0-preview'
$payloadRoot = Join-Path $PSScriptRoot 'payload\current'
$engineRoot = Join-Path $payloadRoot 'Engine'
$installerOutput = Join-Path $PSScriptRoot 'output'
$modernProject = Join-Path $repoRoot 'src\modern-ui\KParser.Sanctum.UI\KParser.Sanctum.UI.csproj'
$legacySolution = Join-Path $repoRoot 'src\legacy-engine\FFXILogParser.sln'
$legacyOutput = Join-Path $repoRoot 'src\legacy-engine\FFXILogParser\bin\x86\Release'
$engineArchive = Join-Path $repoRoot 'src\modern-ui\KParser.Sanctum.UI\Assets\EnginePayload.zip'
$sanctumChatRoot = Join-Path $repoRoot 'addons\sanctumchat'
$sanctumChatEntry = Join-Path $sanctumChatRoot 'sanctumchat.lua'
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
    $addonDestination = Join-Path $destination 'Addons\sanctumchat'
    Copy-DirectoryContents $sanctumChatRoot $addonDestination

    foreach ($requiredFile in @('sanctumchat.lua', 'README.md'))
    {
        if (-not (Test-Path -LiteralPath (Join-Path $addonDestination $requiredFile)))
        {
            throw "The staged SanctumChat addon is missing $requiredFile."
        }
    }
}

function Get-SanctumChatVersion
{
    if (-not (Test-Path -LiteralPath $sanctumChatEntry))
    {
        throw 'The SanctumChat addon entry file was not found.'
    }

    $source = Get-Content -LiteralPath $sanctumChatEntry -Raw
    $match = [regex]::Match($source, 'addon\.version\s*=\s*[''\"](?<version>[^''\"]+)[''\"]')
    if (-not $match.Success)
    {
        throw 'The SanctumChat addon version could not be read.'
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
& $innoCompiler (Join-Path $PSScriptRoot 'KParser-Sanctum.iss')
if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$setupSource = Join-Path $installerOutput 'KParser-Sanctum-Setup-Preview-23.exe'
$setupAsset = Join-Path $artifactRoot 'KParser-Sanctum-Setup-Preview-23.exe'
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

$portableAsset = Join-Path $artifactRoot 'KParser-Sanctum-Portable-Preview-23.zip'
$portableCompactAsset = Join-Path $artifactRoot 'KParser-Sanctum-Portable-Preview-23.7z'
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

$sanctumChatVersion = Get-SanctumChatVersion
$sanctumChatAsset = Join-Path $artifactRoot ("SanctumChat-Ashita4-v{0}.zip" -f $sanctumChatVersion)
Push-Location (Join-Path $repoRoot 'addons')
try
{
    & $sevenZip a -tzip $sanctumChatAsset '.\sanctumchat' -mx=9 -mm=Deflate
    if ($LASTEXITCODE -ne 0)
    {
        throw "SanctumChat ZIP creation failed with exit code $LASTEXITCODE."
    }
}
finally
{
    Pop-Location
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RELEASE-NOTES.md') -Destination $artifactRoot -Force

$checksumAsset = Join-Path $artifactRoot 'SHA256SUMS.txt'
$checksumLines = @($setupAsset, $portableAsset, $portableCompactAsset, $sanctumChatAsset) | ForEach-Object {
    $file = Get-Item -LiteralPath $_
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $file.Name
}
$checksumLines | Set-Content -LiteralPath $checksumAsset -Encoding ascii

Write-Host "SETUP=$setupAsset"
Write-Host "PORTABLE=$portableAsset"
Write-Host "PORTABLE_COMPACT=$portableCompactAsset"
Write-Host "SANCTUMCHAT=$sanctumChatAsset"
Write-Host "CHECKSUMS=$checksumAsset"
