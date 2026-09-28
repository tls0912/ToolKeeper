#Requires -Version 5.1
<#
.SYNOPSIS
Rebuilds the Hanqing Store artwork and current bamboo M package logos.
.DESCRIPTION
Run from Windows PowerShell. Native System.Drawing vector rendering; no app build,
installed graphics tools, network, or user preference changes. Shared bamboo M
artwork is used for app-icon/box/poster. Hero is entirely text-free artwork.
Package logos are generated from current source by default. Pass an explicit
PackageAssetsDirectory only when an exact copy of a particular package is needed.
#>
[CmdletBinding()]
param(
    [string]$PackageAssetsDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '..\assets' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$packageAssets = if ($PackageAssetsDirectory) { [IO.Path]::GetFullPath($PackageAssetsDirectory) } else { $null }
$sourceSvg = Join-Path $repositoryRoot 'src\MarkPad\Assets\MarkPad.svg'
$sourceIco = Join-Path $repositoryRoot 'src\MarkPad\Assets\MarkPad.ico'
$optional = Join-Path $output 'optional-art'
$brandCopies = Join-Path $output 'source-brand'
$packageCopies = Join-Path $output 'msix-package'
foreach ($path in @($output, $optional, $brandCopies, $packageCopies)) {
    [IO.Directory]::CreateDirectory($path) | Out-Null
}
Add-Type -AssemblyName System.Drawing
if (-not ('HanqingStoreArtwork' -as [type])) {
    Add-Type -Path @((Join-Path $repositoryRoot 'scripts\MarkPadIconRenderer.cs'), (Join-Path $PSScriptRoot 'StoreArtworkRenderer.cs')) -ReferencedAssemblies System.Drawing
}
[HanqingStoreArtwork]::Render((Join-Path $output 'app-icon-300.png'), 300, 300, 'icon')
[HanqingStoreArtwork]::Render((Join-Path $optional 'box-art-1080.png'), 1080, 1080, 'box')
[HanqingStoreArtwork]::Render((Join-Path $optional 'poster-art-720x1080.png'), 720, 1080, 'poster')
[HanqingStoreArtwork]::Render((Join-Path $optional 'hero-art-1920x1080.png'), 1920, 1080, 'hero')
Copy-Item -LiteralPath $sourceSvg -Destination (Join-Path $brandCopies 'MarkPad.svg') -Force
Copy-Item -LiteralPath $sourceIco -Destination (Join-Path $brandCopies 'MarkPad.ico') -Force
if ($packageAssets) {
    $logos = @(Get-ChildItem -LiteralPath $packageAssets -File -Filter '*.png')
    if ($logos.Count -ne 23) { throw "Expected 23 package logos, found $($logos.Count). Review the package asset set before continuing." }
    foreach ($logo in $logos) {
        $copiedPath = Join-Path $packageCopies $logo.Name
        Copy-Item -LiteralPath $logo.FullName -Destination $copiedPath -Force
        if ((Get-FileHash -LiteralPath $copiedPath).Hash -ne (Get-FileHash -LiteralPath $logo.FullName).Hash) {
            throw "Package logo copy verification failed: $($logo.Name)"
        }
    }
} else {
    & (Join-Path $repositoryRoot 'scripts\New-MarkPadPackageAssets.ps1') -OutputDirectory $packageCopies
}
$inventory = foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Extension -In '.png', '.ico', '.svg') {
    $dimensions = $null
    if ($file.Extension -eq '.png') {
        $image = [Drawing.Image]::FromFile($file.FullName)
        try { $dimensions = @{ width = $image.Width; height = $image.Height } }
        finally { $image.Dispose() }
        if ($file.Length -ge 50MB) { throw "Store image exceeds 50 MB: $($file.Name)" }
    }
    [ordered]@{
        file = $file.FullName.Substring($output.Length + 1).Replace('\', '/')
        bytes = $file.Length
        dimensions = $dimensions
        sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    }
}
$metadata = [ordered]@{
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    sourceBrand = 'src/MarkPad/Assets/MarkPad.svg'
    sourceRenderer = 'scripts/MarkPadIconRenderer.cs'
    packageAssetsSource = $(if (-not $packageAssets) { 'generated from scripts/MarkPadIconRenderer.cs; no MSIX archive selected for comparison in this run' }
    elseif ($packageAssets.StartsWith($repositoryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        $packageAssets.Substring($repositoryRoot.Length + 1).Replace('\', '/')
    } else { $packageAssets.Replace('\', '/') })
    officialGuidance = 'https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images'
    notes = @('app-icon-300.png is the Store listing icon.', $(if ($packageAssets) { 'msix-package contains exact copies of the selected 23 package logos.' } else { 'msix-package contains 23 regenerated bamboo M logos from current source. No MSIX archive was selected for comparison or modified in this run.' }),
        'optional-art contains promotional illustrations, not app screenshots.', 'Box and poster are optional backup art; game-oriented main-logo guidance does not make them required for this app.',
        'Hero has no title, text, monogram, application UI, or device imagery.')
    files = @($inventory)
}
$json = $metadata | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $output 'asset-manifest.json'), $json, [Text.UTF8Encoding]::new($false))
$inventory | ForEach-Object {
    $size = if ($_.dimensions) { '{0}x{1}' -f $_.dimensions.width, $_.dimensions.height } else { 'source' }
    '{0}: {1}, {2:N0} bytes' -f $_.file, $size, $_.bytes
}
