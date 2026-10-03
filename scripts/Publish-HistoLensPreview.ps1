[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$EvidenceDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$histoLensRepo = Split-Path $PSScriptRoot -Parent
$histoLensArtifacts = [IO.Path]::GetFullPath((Join-Path $histoLensRepo 'artifacts'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $histoLensArtifacts 'histolens-preview' }
if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $histoLensArtifacts ('histolens-verification\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}

function Resolve-PreviewDirectory([string]$Value) {
    if (-not [IO.Path]::IsPathRooted($Value)) { $Value = Join-Path $histoLensRepo $Value }
    $resolved = [IO.Path]::GetFullPath($Value).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $resolved.StartsWith($histoLensArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Preview and evidence directories must be subdirectories of this repository artifacts directory.'
    }
    return $resolved
}
$OutputDirectory = Resolve-PreviewDirectory $OutputDirectory
$EvidenceDirectory = Resolve-PreviewDirectory $EvidenceDirectory
if ($OutputDirectory -eq $EvidenceDirectory -or
    $OutputDirectory.StartsWith($EvidenceDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $EvidenceDirectory.StartsWith($OutputDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Preview output and evidence directories must be separate, non-nested directories.'
}

# Preserve research/preferences in a previously used preview while disabling desktop takeover.
$histoLensProfile = Join-Path $OutputDirectory 'profile\toolkeeper-host'
$histoLensPlatformFile = Join-Path $histoLensProfile 'platform.json'
$histoLensPlatform = [pscustomobject]@{ DesktopEnabled = $false }
if (Test-Path -LiteralPath $histoLensPlatformFile -PathType Leaf) {
    $histoLensPlatform = Get-Content -LiteralPath $histoLensPlatformFile -Raw | ConvertFrom-Json
    if ($null -eq $histoLensPlatform -or $histoLensPlatform -isnot [pscustomobject]) {
        throw 'The existing preview platform profile is not a JSON object; preserve it and choose a fresh output directory.'
    }
    $histoLensPlatform | Add-Member -NotePropertyName DesktopEnabled -NotePropertyValue $false -Force
}

$histoLensPreviousScreenshots = [Environment]::GetEnvironmentVariable('HISTOLENS_SCREENSHOT_DIR', 'Process')
Push-Location -LiteralPath $histoLensRepo
try {
    New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
    [Environment]::SetEnvironmentVariable('HISTOLENS_SCREENSHOT_DIR', $EvidenceDirectory, 'Process')
    & dotnet test tests/HistoLens.Tests/HistoLens.Tests.csproj -c $Configuration --logger 'trx;LogFileName=histolens.trx' --results-directory $EvidenceDirectory
    if ($LASTEXITCODE -ne 0) { throw "HistoLens tests failed ($LASTEXITCODE); preview was not published." }

    $histoLensHostFilter = 'FullyQualifiedName~ProductCatalogTests|FullyQualifiedName~ProductLauncherTests|FullyQualifiedName~UriActivationTests|FullyQualifiedName~MainWindowIntegrationTests|FullyQualifiedName~ModuleWindowManagerTests|FullyQualifiedName~LauncherShowsAllModuleEntriesAtMinimumSize'
    & dotnet test tests/ToolKeeper.Tests/ToolKeeper.Tests.csproj -c $Configuration --filter $histoLensHostFilter --logger 'trx;LogFileName=histolens-host.trx' --results-directory $EvidenceDirectory
    if ($LASTEXITCODE -ne 0) { throw "HistoLens host tests failed ($LASTEXITCODE); preview was not published." }

    foreach ($histoLensFile in @('demo.histolens.json', 'demo.snapshot.json',
        'histolens-zh-TW-Ink-900.png', 'histolens-en-Dark-1280.png', 'histolens-ja-InkDark-1280.png',
        'histolens-cases-zh-TW-Ink-900.png', 'histolens-cases-en-Dark-1280.png', 'histolens-cases-ja-InkDark-1280.png')) {
        if (-not (Test-Path -LiteralPath (Join-Path $EvidenceDirectory $histoLensFile) -PathType Leaf)) {
            throw "Verification artifact was not generated: $histoLensFile"
        }
    }

    & dotnet publish src/ToolKeeper/ToolKeeper.csproj -c $Configuration --no-restore --self-contained false -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "HistoLens host publish failed ($LASTEXITCODE)." }
    New-Item -ItemType Directory -Path $histoLensProfile, (Join-Path $OutputDirectory 'empty-desktop') -Force | Out-Null
    $histoLensUtf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($histoLensPlatformFile, ($histoLensPlatform | ConvertTo-Json -Depth 30), $histoLensUtf8)
    $histoLensLauncher = @'
@echo off
start "" "%~dp0ToolKeeper.exe" --data-directory "%~dp0profile" --desktop-directory "%~dp0empty-desktop" --activate toolkeeper://run/006
'@
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'Start-HistoLens.cmd'), $histoLensLauncher.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n", [Text.Encoding]::ASCII)
    foreach ($histoLensFile in @('demo.histolens.json', 'demo.snapshot.json')) {
        Copy-Item -LiteralPath (Join-Path $EvidenceDirectory $histoLensFile) -Destination (Join-Path $OutputDirectory $histoLensFile) -Force
    }
    Copy-Item -LiteralPath (Join-Path $histoLensRepo 'docs\HISTOLENS-IMPLEMENTATION.md') -Destination (Join-Path $OutputDirectory 'IMPLEMENTATION-NOTES.md') -Force
    $histoLensReadme = @'
# HistoLens synthetic-data preview

Run `Start-HistoLens.cmd` on Windows with the .NET 10 Desktop Runtime installed.
The launcher uses this folder's `profile` and `empty-desktop`, disables desktop takeover by default, and does not register the system protocol.
The ToolKeeper host keeps its normal tray lifetime after the research window closes; use the preview host tray menu to exit it.

Open `demo.histolens.json` in HistoLens to view the saved synthetic research.
`demo.snapshot.json` is the raw synthetic snapshot for inspection, not the UI import format.
No real stock data, provider credentials, or automatic download is included.

Regenerate this package and its test evidence with `scripts/Publish-HistoLensPreview.ps1` from the repository.
This is a framework-dependent local development preview, not a standalone product or Store package.
'@
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'README.md'), $histoLensReadme, $histoLensUtf8)
    $histoLensManifest = [ordered]@{
        CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Configuration = $Configuration
        EvidenceDirectory = $EvidenceDirectory
        PreviewDirectory = $OutputDirectory
        Runtime = 'Microsoft.WindowsDesktop.App 10.0; framework dependent'
        SyntheticDataOnly = $true
        Artifacts = @(Get-ChildItem -LiteralPath $EvidenceDirectory -File | ForEach-Object {
            [ordered]@{ Name = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
        })
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'verification-manifest.json'), ($histoLensManifest | ConvertTo-Json -Depth 5), $histoLensUtf8)
}
finally {
    [Environment]::SetEnvironmentVariable('HISTOLENS_SCREENSHOT_DIR', $histoLensPreviousScreenshots, 'Process')
    Pop-Location
}
Write-Output "Preview: $OutputDirectory"
Write-Output "Evidence: $EvidenceDirectory"
Write-Output 'Run Start-HistoLens.cmd to open 006 with the isolated profile.'
