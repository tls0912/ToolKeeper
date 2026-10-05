#Requires -Version 5.1
<#
.SYNOPSIS
Builds a self-contained Hanqing (汗青) MSIX for Microsoft Store submission.
.DESCRIPTION
Requires real Partner Center identity values in a JSON file. Restores the pinned
Microsoft SDK tools into .packages if necessary. Produces an unsigned MSIX;
Microsoft Store signs it after certification. Does not install or register it.
.EXAMPLE
.\scripts\Publish-MarkPadMsix.ps1 -IdentityFile .\packaging\MarkPad\StoreIdentity.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$IdentityFile,
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$PackageVersion = '1.0.3.0',
    [string]$OutputDirectory,
    [string]$SdkBinDirectory,
    [string]$MaxVersionTested = '10.0.26100.0',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Build Hanqing MSIX on Windows with the .NET 10 SDK.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packagingRoot = Join-Path $repositoryRoot 'packaging\MarkPad'
$packageCache = Join-Path $repositoryRoot '.packages'
$projectPath = Join-Path $repositoryRoot 'src\MarkPad\MarkPad.csproj'
$configPath = Join-Path $packagingRoot 'NuGet.Config'

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE." }
}

function Read-QuadVersion([string]$Value, [string]$Label) {
    if ($Value -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "$Label must contain four integers, for example 1.0.0.0." }
    $parts = @($Value.Split('.') | ForEach-Object { [uint64]$_ })
    if (@($parts | Where-Object { $_ -gt 65535 }).Count -gt 0) { throw "$Label components must be between 0 and 65535." }
    return [version]$Value
}

# Fail before building or creating output if Partner Center data is incomplete.
$identityPath = [IO.Path]::GetFullPath($IdentityFile)
$identity = Get-Content -LiteralPath $identityPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($field in @('Name', 'Publisher', 'PublisherDisplayName', 'PackageFamilyName', 'StoreId')) {
    $property = $identity.PSObject.Properties[$field]
    if ($null -eq $property -or $property.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($property.Value)) {
        throw "Identity file must contain $field copied exactly from Partner Center. See packaging/MarkPad/StoreIdentity.example.json."
    }
    if ($property.Value -ne $property.Value.Trim()) {
        throw "Identity field $field has leading/trailing whitespace, which is not valid in this manifest. Verify the value in Partner Center before correcting it."
    }
}
if ($identity.Name -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'Package identity Name must be 3-50 letters, digits, periods or hyphens.' }
if ($identity.Publisher -notmatch '^CN=') { throw 'Publisher must be the full Partner Center distinguished name, beginning with CN=.' }
try { $null = New-Object Security.Cryptography.X509Certificates.X500DistinguishedName($identity.Publisher) }
catch { throw 'Publisher is not a valid X.500 distinguished name. Copy Package/Identity/Publisher from Partner Center.' }
if ($identity.PublisherDisplayName.Length -gt 256) { throw 'PublisherDisplayName must not exceed 256 characters.' }
if ($identity.StoreId -notmatch '^[A-Za-z0-9]{12}$') { throw 'StoreId must be the 12-character product ID from Partner Center.' }
if ($identity.PackageFamilyName -cnotmatch ('^' + [regex]::Escape($identity.Name) + '_[a-z0-9]{13}$')) {
    throw 'PackageFamilyName must be the matching package family name copied from Partner Center.'
}
$version = Read-QuadVersion $PackageVersion 'PackageVersion'
if ($version.Major -eq 0 -or $version.Revision -ne 0) { throw 'Store versions must start above zero and end in .0, for example 1.0.0.0.' }
$testedVersion = Read-QuadVersion $MaxVersionTested 'MaxVersionTested'
if ($testedVersion -lt [version]'10.0.17763.0') { throw 'MaxVersionTested cannot be lower than the minimum Windows version.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET 10 SDK is required.' }

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\msix\Hanqing-$Runtime-$stamp"
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    if (-not (Test-Path -LiteralPath $outputPath -PathType Container) -or @(Get-ChildItem -LiteralPath $outputPath -Force).Count -gt 0) {
        throw "Choose an empty OutputDirectory to avoid including stale files: $outputPath"
    }
}

if ([string]::IsNullOrWhiteSpace($SdkBinDirectory)) {
    # Use a pinned, app-local SDK rather than changing a machine-wide installation.
    [xml]$toolsProject = Get-Content -LiteralPath (Join-Path $packagingRoot 'BuildTools.csproj') -Raw
    $toolsVersion = $toolsProject.Project.ItemGroup.PackageReference.Version
    $toolsRoot = Join-Path $packageCache "microsoft.windows.sdk.buildtools\$toolsVersion"
    $makeAppx = @(Get-ChildItem -LiteralPath $toolsRoot -Filter makeappx.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' }) | Select-Object -First 1
    if ($null -eq $makeAppx) {
        if ($NoRestore) { throw 'MSIX SDK tools are not cached. Omit -NoRestore or supply -SdkBinDirectory.' }
        Invoke-Checked 'dotnet' @('restore', (Join-Path $packagingRoot 'BuildTools.csproj'), '--packages', $packageCache, '--configfile', $configPath, '--nologo')
        $makeAppx = @(Get-ChildItem -LiteralPath $toolsRoot -Filter makeappx.exe -File -Recurse |
            Where-Object { $_.Directory.Name -eq 'x64' }) | Select-Object -First 1
    }
    if ($null -eq $makeAppx) { throw 'The restored SDK does not contain x64 MakeAppx.exe.' }
    $SdkBinDirectory = $makeAppx.Directory.FullName
}
$makeAppxPath = Join-Path $SdkBinDirectory 'makeappx.exe'
$makePriPath = Join-Path $SdkBinDirectory 'makepri.exe'
foreach ($tool in @($makeAppxPath, $makePriPath)) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Missing SDK tool: $tool" }
}

$layout = Join-Path $outputPath 'Layout'
[IO.Directory]::CreateDirectory($layout) | Out-Null
if (-not $NoRestore) {
    Invoke-Checked 'dotnet' @('restore', $projectPath, '--runtime', $Runtime, '--packages', $packageCache, '--configfile', $configPath, '--nologo')
}
# Version overrides keep the installed app's About version consistent with MSIX.
# The portable project's development version remains untouched.
Invoke-Checked 'dotnet' @('publish', $projectPath, '--configuration', 'Release', '--runtime', $Runtime,
    '--self-contained', 'true', '--no-restore', '--output', $layout, '--nologo',
    '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false',
    "-p:Version=$version", "-p:InformationalVersion=$($version.ToString(3))", '-p:IncludeSourceRevisionInInformationalVersion=false',
    '-p:StoreLicenseRequired=true', "-p:StoreProductId=$($identity.StoreId)", "-p:StorePackageFamilyName=$($identity.PackageFamilyName)")
if (-not (Test-Path -LiteralPath (Join-Path $layout 'Hanqing.exe') -PathType Leaf)) { throw 'Publish did not produce Hanqing.exe.' }
& (Join-Path $PSScriptRoot 'Add-MarkPadNotices.ps1') -OutputDirectory $layout -Runtime $Runtime
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\samples\Welcome.md') -Destination $layout
& (Join-Path $PSScriptRoot 'New-MarkPadPackageAssets.ps1') -OutputDirectory (Join-Path $layout 'Assets')

[xml]$manifest = Get-Content -LiteralPath (Join-Path $packagingRoot 'AppxManifest.xml') -Raw -Encoding UTF8
$manifest.Package.Identity.SetAttribute('Name', $identity.Name)
$manifest.Package.Identity.SetAttribute('Publisher', $identity.Publisher)
$manifest.Package.Identity.SetAttribute('Version', $version.ToString())
$manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $Runtime.Substring(4))
$manifest.Package.Properties.PublisherDisplayName = $identity.PublisherDisplayName
$manifest.Package.Dependencies.TargetDeviceFamily.SetAttribute('MaxVersionTested', $testedVersion.ToString())
$manifestPath = Join-Path $layout 'AppxManifest.xml'
$manifest.Save($manifestPath)

# Index scale/targetsize variants, keeping the PRI configuration outside the payload.
$priConfig = Join-Path $outputPath 'priconfig.xml'
Invoke-Checked $makePriPath @('createconfig', '/cf', $priConfig, '/dq', 'en-US', '/pv', '10.0', '/o')
[xml]$priSettings = Get-Content -LiteralPath $priConfig -Raw -Encoding UTF8
# This is one complete MSIX, not a bundle of separate language/scale packages.
$packagingNode = $priSettings.SelectSingleNode('/resources/packaging')
if ($null -ne $packagingNode) { $null = $packagingNode.ParentNode.RemoveChild($packagingNode) }
$priSettings.Save($priConfig)
# A separate indexing root excludes .NET satellite DLLs while preserving the
# Files/Assets/... resource names referenced by the package manifest.
$resourceInput = Join-Path $outputPath 'ResourceInput'
[IO.Directory]::CreateDirectory($resourceInput) | Out-Null
Copy-Item -LiteralPath (Join-Path $layout 'Assets') -Destination $resourceInput -Recurse
Invoke-Checked $makePriPath @('new', '/pr', $resourceInput, '/cf', $priConfig, '/mn', $manifestPath, '/of', (Join-Path $layout 'resources.pri'), '/o')
$priDump = Join-Path $outputPath 'resources.dump.xml'
Invoke-Checked $makePriPath @('dump', '/if', (Join-Path $layout 'resources.pri'), '/of', $priDump, '/dt', 'detailed', '/o')
[xml]$resourceMap = Get-Content -LiteralPath $priDump -Raw -Encoding UTF8
foreach ($logo in @('StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png')) {
    $mappedLogo = @($resourceMap.SelectNodes('//NamedResource') | Where-Object { $_.GetAttribute('uri').EndsWith("/Files/Assets/$logo") })
    if ($mappedLogo.Count -ne 1) { throw "The PRI does not index Assets/$logo under the manifest's resource path." }
}
$packageName = 'Hanqing_{0}_{1}.msix' -f $version, $Runtime.Substring(4)
$packagePath = Join-Path $outputPath $packageName
# Do not use /nv: MakeAppx must validate the manifest and package semantics.
Invoke-Checked $makeAppxPath @('pack', '/d', $layout, '/p', $packagePath, '/h', 'SHA256', '/no')
& (Join-Path $PSScriptRoot 'Test-MarkPadMsix.ps1') -PackagePath $packagePath -IdentityFile $identityPath
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
"$hash  $packageName" | Set-Content -LiteralPath (Join-Path $outputPath "$packageName.sha256") -Encoding ASCII
@"
汗青 - Markdown Writer Microsoft Store package
Package: $packageName
Name: $($identity.Name)
Publisher: $($identity.Publisher)
Publisher display name: $($identity.PublisherDisplayName)
Version: $version
Architecture: $($Runtime.Substring(4))
Store product: $($identity.StoreId)
Package family: $($identity.PackageFamilyName)
Startup licensing: trial check; confirmed full purchases remembered locally
Store package identity enforcement: required (compiled into Hanqing.dll)

This MSIX is intentionally unsigned. Upload it to the matching Partner Center
MSIX product; Microsoft Store signs it after certification. It is not a signed
sideload installer. The build does not install an app or trust a certificate.

.NET runtime and third-party license notices are included. Microsoft Edge
WebView2 Evergreen Runtime is required for Preview and PDF export and is NOT
bundled. Verify it on clean supported Windows machines before submission.

MakeAppx validation and archive content checks passed. Windows App Certification
Kit, installed-package activation, update/uninstall, and clean-machine runtime
checks are separate; this build does not claim that those checks have passed.
Store licensing (full, trial, expired, offline) must be tested using an actual
Store installation and account. Local flow tests do not validate Store entitlement.
Trial duration comes from Partner Center (7 days), not a local timer. After the
Store confirms a full purchase, a Windows-user protected local record skips
future Store queries. Missing or unreadable records require verification again.
Refunds and revocations are not automatically detected after that confirmation.
"@ | Set-Content -LiteralPath (Join-Path $outputPath 'PACKAGE-INFO.txt') -Encoding UTF8
Write-Output "Store package: $packagePath"
Write-Output "SHA256: $hash"
