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
        'histolens-similarity-zh-TW-Ink-900.png', 'histolens-similarity-en-Dark-1280.png', 'histolens-similarity-ja-InkDark-1280.png',
        'histolens-history-all-zh-TW-Ink-900.png', 'histolens-history-all-en-Dark-1280.png', 'histolens-history-all-ja-InkDark-1280.png',
        'histolens-data-management-zh-TW-Ink-900.png', 'histolens-data-management-en-Dark-1280.png', 'histolens-data-management-ja-InkDark-1280.png',
        'histolens-finmind-data-zh-TW.png', 'histolens-finmind-data-en.png', 'histolens-finmind-data-ja.png',
        'histolens-indicators-zh-TW-Ink-900.png', 'histolens-indicators-en-Dark-1280.png', 'histolens-indicators-ja-InkDark-1280.png',
        'histolens-details-zh-TW-Ink-900.png', 'histolens-details-en-Dark-1280.png', 'histolens-details-ja-InkDark-1280.png',
        'histolens-calendar-zh-TW-Ink.png', 'histolens-calendar-en-Dark.png', 'histolens-calendar-ja-InkDark.png',
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
    Copy-Item -LiteralPath (Join-Path $histoLensRepo 'src\HistoLens\Assets\HistoLens.ico') -Destination (Join-Path $OutputDirectory 'HistoLens.ico') -Force
    $histoLensShell = New-Object -ComObject WScript.Shell
    $histoLensShortcut = $null
    try {
        $histoLensShortcut = $histoLensShell.CreateShortcut((Join-Path $OutputDirectory 'HistoLens.lnk'))
        $histoLensShortcut.TargetPath = Join-Path $OutputDirectory 'ToolKeeper.exe'
        $histoLensShortcut.Arguments = '--data-directory "{0}" --desktop-directory "{1}" --activate toolkeeper://run/006' -f (Join-Path $OutputDirectory 'profile'), (Join-Path $OutputDirectory 'empty-desktop')
        $histoLensShortcut.WorkingDirectory = $OutputDirectory
        $histoLensShortcut.IconLocation = (Join-Path $OutputDirectory 'HistoLens.ico') + ',0'
        $histoLensShortcut.Description = 'HistoLens - Historical Stock Research'
        $histoLensShortcut.Save()
    }
    finally {
        if ($null -ne $histoLensShortcut) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($histoLensShortcut) | Out-Null }
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($histoLensShell) | Out-Null
    }
    foreach ($histoLensFile in @('demo.histolens.json', 'demo.snapshot.json')) {
        Copy-Item -LiteralPath (Join-Path $EvidenceDirectory $histoLensFile) -Destination (Join-Path $OutputDirectory $histoLensFile) -Force
    }
    Copy-Item -LiteralPath (Join-Path $histoLensRepo 'docs\HISTOLENS-IMPLEMENTATION.md') -Destination (Join-Path $OutputDirectory 'IMPLEMENTATION-NOTES.md') -Force
    foreach ($histoLensDoc in @('HISTOLENS-VALUATION.md', 'HISTOLENS-STOCK-CATALOG-SOURCES.md', 'HISTOLENS-SIMILARITY.md', 'HISTOLENS-FINMIND-IMPLEMENTATION.md')) {
        Copy-Item -LiteralPath (Join-Path $histoLensRepo ('docs\' + $histoLensDoc)) -Destination (Join-Path $OutputDirectory $histoLensDoc) -Force
    }
    $histoLensReadme = @'
# HistoLens similarity-scan preview

Run `HistoLens.lnk` on this computer to use the dedicated product icon, or `Start-HistoLens.cmd` on Windows with the .NET 10 Desktop Runtime installed.
The shortcut targets this publish location; use `Start-HistoLens.cmd` if you move the folder to another location or computer.
The launcher uses this folder's `profile` and `empty-desktop`, disables desktop takeover by default, and does not register the system protocol.
The ToolKeeper host keeps its normal tray lifetime after the research window closes; use the preview host tray menu to exit it.

Select a saved stock in Magnifier to load it immediately. The built-in synthetic sample appears in Data management and the same stock selector; no separate sample-load button is needed. Set recent trading days, choose scan start/end using the calendar controls and a minimum total similarity (60% by default, inclusive), then select Find similar periods.
Use the header buttons to switch between Magnifier and Data management. Find similar periods and Cancel are directly below the stock selector. Cancel remains available in Data management while downloading. Numeric values are right-aligned.
Compare indicators is always visible: select any of ten indicators including price path, high/low positions, slope, volatility, window RSI, SMA deviation, Bollinger width, normalized ATR and volume shape. Choices are saved locally and restored next time; the original five are selected on first use. The total is the equal-weight average of selected scores. Results keep the completed run's selection until a successful rescan. See `HISTOLENS-SIMILARITY.md` for formulas, window conventions, sources and boundaries.
The chart contains the full loaded history, even before a scan. Select a result to locate its matching period with preceding and following historical prices. Recent prices overlay only that matched period as a thicker orange line, with both match starts at 100. Use All history, Locate selected, zoom controls and the horizontal scrollbar to explore the timeline.
The advanced condition-research entry is hidden. Data details, indicator choices, comparison details and formulas are always expanded; scroll to see longer content. Quality, source and raw daily-bar tabs remain accessible.
`demo.snapshot.json` is the raw synthetic snapshot for inspection, not the UI import format.
No real stock data, provider credentials, or automatic download is included.
To download, open Data management and use Update listed / OTC stock list to save the FinMind stock selection list locally. Entering a complete code selects its dropdown entry; choosing an entry fills the code and market. Shared codes prefer the explicitly selected market. An unknown or partial code clears the dropdown selection without replacing your input. The list reopens offline and updates only when requested; list updates and stock selection never download prices. Direct code entry remains available without a list. See `HISTOLENS-STOCK-CATALOG-SOURCES.md`.
The only download source is FinMind. Select market, stock and completed start/end dates, then select Download missing data. FinMind uses the website's public JSON data endpoint anonymously; it does not automate browser CSV downloads. Invalid, blank or reversed dates are rejected before downloading. The inventory shows each saved stock, market, source, saved date range, actual price dates and daily-row count. Open the selected stock in Magnifier or delete its active market data after confirmation; saved research snapshots and legacy backups are retained.
The downloader checks local dates first. FinMind requests only exact missing date intervals, including internal gaps. A fully cached selection performs no network requests. Each source, market and stable security identity has a separate active file; existing valid days are retained, and failures leave existing data intact. Raw-price scans retain known event markers and warn about unknown corporate-action coverage. FinMind volume is in shares. The separate FinMind market-calendar table is validated for format, duplicates and coverage; no other source is contacted. Full historical security lifetimes and event coverage remain unverified. New files use .market-data.json; existing local files remain readable without changing their provenance. See `HISTOLENS-FINMIND-IMPLEMENTATION.md` for live verification, calendar coverage and remaining limitations.
Starting analysis also requests the latest published FinMind P/E ratio for the selected market and stock. Its actual data date is displayed; unavailable valuation data does not interrupt the historical scan. P/E does not enter similarity scores.
See `HISTOLENS-FINMIND-IMPLEMENTATION.md` and `HISTOLENS-VALUATION.md` for supported scope, source evidence and limitations.

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
        SyntheticDataOnly = $false
        BundledSamplesAreSynthetic = $true
        MarketDataDownloadIsExplicit = $true
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
Write-Output 'Run HistoLens.lnk or Start-HistoLens.cmd to open 006 with the isolated profile.'
