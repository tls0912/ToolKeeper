#Requires -Version 5.1
<#
.SYNOPSIS
Builds an unpackaged Hanqing (汗青) folder, optionally with a ZIP archive.
.EXAMPLE
.\scripts\Publish-MarkPad.ps1 -Runtime win-x64 -Zip
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [switch]$FrameworkDependent,

    [switch]$NoRestore,

    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT') { throw 'Build Hanqing on Windows with the .NET 10 SDK.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src\MarkPad\MarkPad.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET 10 SDK is required.' }

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\portable\Hanqing-$Runtime-$stamp"
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) { throw "Output is a file: $outputPath" }
    if (@(Get-ChildItem -LiteralPath $outputPath -Force).Count -ne 0) {
        throw "Choose an empty OutputDirectory to avoid shipping stale files: $outputPath"
    }
}
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$arguments = @(
    'publish', $projectPath,
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--self-contained', $selfContained,
    '--output', $outputPath,
    '--nologo',
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false'
)
if ($NoRestore) { $arguments += '--no-restore' }

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE. Partial output: $outputPath" }
foreach ($requiredFile in @('Hanqing.exe', 'ToolKeeper.UI.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputPath $requiredFile) -PathType Leaf)) {
        throw "Publish completed without ${requiredFile}: $outputPath"
    }
}
& (Join-Path $PSScriptRoot 'Add-MarkPadNotices.ps1') -OutputDirectory $outputPath -Runtime $Runtime -FrameworkDependent:$FrameworkDependent
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\samples\Welcome.md') -Destination $outputPath

foreach ($name in @('Register-MarkPad.ps1', 'Unregister-MarkPad.ps1', 'Registration.Common.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $outputPath
}
$runtimeNote = if ($FrameworkDependent) { 'Requires the .NET 10 Desktop Runtime.' } else { 'Includes the .NET runtime.' }
@"
汗青 portable build ($Runtime)

Run Hanqing.exe. Keep every file in this folder together.
Try the bundled sample with: .\Hanqing.exe .\Welcome.md
$runtimeNote
Microsoft Edge WebView2 Runtime is required for Markdown preview.
Runtime download: https://developer.microsoft.com/microsoft-edge/webview2/

Settings, recovery drafts, and local logs use:
%LOCALAPPDATA%\ToolKeeper\MarkPad

Optional registration for this Windows user, from this folder:
  .\Register-MarkPad.ps1 -ExecutablePath .\Hanqing.exe -WhatIf
  .\Register-MarkPad.ps1 -ExecutablePath .\Hanqing.exe
Remove that registration before moving or deleting the folder:
  .\Unregister-MarkPad.ps1

Registration adds .md Open With support and the toolkeeper-markpad: URI.
Internal registration names and the existing data folder remain compatible with MarkPad.
Choose default apps yourself in Windows Settings. No administrator rights are required.
This development build is not a signed Microsoft Store package.
Third-party license texts and notices are in THIRD-PARTY-NOTICES.md and licenses/.
"@ | Set-Content -LiteralPath (Join-Path $outputPath 'README-PORTABLE.txt') -Encoding UTF8

if ($Zip) {
    $archivePath = $outputPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + '.zip'
    if (Test-Path -LiteralPath $archivePath) { throw "Archive already exists: $archivePath" }
    Compress-Archive -LiteralPath $outputPath -DestinationPath $archivePath -CompressionLevel Optimal
    Write-Output "Archive: $archivePath"
}
Write-Output "Portable folder: $outputPath"
