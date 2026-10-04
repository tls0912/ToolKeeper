#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$KitDirectory,
    [string]$NsisCompiler,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$version = [string](([xml](Get-Content (Join-Path $repo 'src\TransLamp\TransLamp.csproj') -Raw)).Project.PropertyGroup.Version | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part TransLamp version.' }
if (-not $KitDirectory) { $KitDirectory = Join-Path $repo "artifacts\TransLamp-OfflineKit-$version" }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts\installers' }
if (-not $NsisCompiler) { $NsisCompiler = Join-Path $repo 'artifacts\installer-tools\nsis-3.13\makensis.exe' }
$KitDirectory = [IO.Path]::GetFullPath($KitDirectory).TrimEnd('\')
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$NsisCompiler = [IO.Path]::GetFullPath($NsisCompiler)

function Assert-RegularPath([string]$Path) {
    $current = $Path
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Linked packaging paths are not supported: $Path"
        }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
function Get-RegularFiles([string]$Directory) {
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked payload entry: $($item.FullName)" }
        if ($item.PSIsContainer) { Get-RegularFiles $item.FullName } else { $item }
    }
}
function Test-Within([string]$Child, [string]$Parent) {
    return $Child.Equals($Parent, [StringComparison]::OrdinalIgnoreCase) -or $Child.StartsWith($Parent + '\', [StringComparison]::OrdinalIgnoreCase)
}
foreach ($path in @($KitDirectory, $OutputDirectory, $NsisCompiler)) {
    Assert-RegularPath $path
    if ($path -match '[\r\n"$]') { throw 'Packaging paths must not contain quotes, dollar signs or newlines.' }
}
if ((Test-Within $OutputDirectory $KitDirectory) -or (Test-Within $KitDirectory $OutputDirectory)) {
    throw 'Installer output and input kit must be separate, non-overlapping directories.'
}
if (-not (Test-Path -LiteralPath $NsisCompiler -PathType Leaf)) {
    throw 'NSIS compiler missing. See docs/TRANSLAMP-OFFLINE-INSTALLER.md; pass -NsisCompiler for an existing portable compiler.'
}
$compilerVersion = (& $NsisCompiler /VERSION | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $compilerVersion -ne 'v3.13') { throw "Expected NSIS v3.13, got $compilerVersion" }

$markerPath = Join-Path $KitDirectory '.translamp-offline-kit.json'
$reportPath = Join-Path $KitDirectory 'Verification\publish-verification.json'
Assert-RegularPath $markerPath
Assert-RegularPath $reportPath
$marker = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json
$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($marker.product -ne 'toolkeeper.translamp.offlinekit' -or $marker.complete -ne $true -or
    $marker.productVersion -ne $version -or $report.productVersion -ne $version -or $report.passed -ne $true -or
    $marker.publishVerificationSha256 -ne (Get-FileHash -LiteralPath $reportPath).Hash) {
    throw 'Offline Kit marker/version/report invalid. Run scripts/Publish-TransLamp.ps1 first.'
}
$files = @(Get-RegularFiles $KitDirectory | Sort-Object FullName)
$actual = @{}
foreach ($file in $files) { $actual[$file.FullName.Substring($KitDirectory.Length + 1).Replace('\', '/')] = $file }
if ($actual.Count -ne @($report.files).Count + 2) { throw 'Offline Kit inventory count changed.' }
foreach ($entry in $report.files) {
    if (-not $actual.ContainsKey($entry.path)) { throw "Missing kit file: $($entry.path)" }
    $file = $actual[$entry.path]
    if ($file.Length -ne $entry.size -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $entry.sha256) {
        throw "Kit file hash mismatch: $($entry.path)"
    }
}
if ($actual.ContainsKey('Uninstall.exe') -or $actual.ContainsKey('.translamp-install.ini')) { throw 'Kit contains installer-reserved files.' }

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$work = Join-Path $OutputDirectory ('.translamp-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$include = Join-Path $work 'uninstall-files.nsh'
$lines = [Collections.Generic.List[string]]::new()
$directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($KitDirectory.Length + 1)
    if ($relative -match '[\r\n"$]') { throw "Unsupported NSIS filename: $relative" }
    $lines.Add('ClearErrors')
    $lines.Add('Delete "$INSTDIR\' + $relative + '"')
    $lines.Add('IfErrors 0 +2')
    $lines.Add('  StrCpy $UninstallDeleteFailed 1')
    $directory = Split-Path $relative -Parent
    while ($directory) {
        $null = $directories.Add($directory)
        $directory = Split-Path $directory -Parent
    }
}
foreach ($directory in ($directories | Sort-Object Length -Descending)) { $lines.Add('RMDir "$INSTDIR\' + $directory + '"') }
[IO.File]::WriteAllLines($include, $lines, [Text.UTF8Encoding]::new($true))

& (Join-Path $PSScriptRoot 'Test-TransLampResources.ps1') -ResourceDirectory $KitDirectory -ReportPath (Join-Path $work 'resource-verification.json') -Smoke
$name = "TransLamp-Setup-$version-win-x64.exe"
$stagedExe = Join-Path $work $name
$sizeKb = [int][Math]::Ceiling(($files | Measure-Object Length -Sum).Sum / 1024)
$arguments = @('/V2', '/NOCONFIG', '/NOCD', '/INPUTCHARSET', 'UTF8', "/DAPP_VERSION=$version", "/DPAYLOAD_DIR=$KitDirectory", "/DOUTPUT_FILE=$stagedExe",
    "/DUNINSTALL_FILES=$include", "/DINSTALLED_SIZE_KB=$sizeKb", (Join-Path $repo 'packaging\TransLamp\TransLamp.nsi'))
& $NsisCompiler @arguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $stagedExe)) { throw "NSIS compilation failed. Evidence retained in $work" }
$destination = Join-Path $OutputDirectory $name
Assert-RegularPath $destination
# Preserve any previous deliverable in this build's evidence directory.
if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $work ('previous-' + $name)
    Move-Item -LiteralPath $destination -Destination $backup
}
Move-Item -LiteralPath $stagedExe -Destination $destination
$hash = (Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($destination + '.sha256', "$hash  $name`r`n", [Text.Encoding]::ASCII)
[ordered]@{
    product = 'toolkeeper.translamp.installer'; version = $version; compiler = $compilerVersion
    file = $name; bytes = (Get-Item -LiteralPath $destination).Length; sha256 = $hash
    sourceKit = $KitDirectory; payloadFiles = $files.Count; payloadBytes = ($files | Measure-Object Length -Sum).Sum
    sourcePublishVerificationSha256 = $marker.publishVerificationSha256
    sourceInventoryVerified = $true; runtimeSmokePassed = $true; installUninstallTestPassed = $null
    signed = $false; builtAtUtc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($destination + '.json') -Encoding UTF8
Write-Output "Installer: $destination"
Write-Output "SHA256: $hash"
