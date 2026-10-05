#Requires -Version 5.1
<# Runs the actual installer in a fresh workspace folder, then removes its own
   registration/shortcuts through the actual uninstaller. Refuses to run when
   this user already has a registered TransLamp installation or named shortcut. #>
[CmdletBinding()]
param([string]$Installer)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$version = [string](([xml](Get-Content (Join-Path $repo 'src\TransLamp\TransLamp.csproj') -Raw)).Project.PropertyGroup.Version | Select-Object -First 1)
if (-not $Installer) { $Installer = Join-Path $repo "artifacts\installers\TransLamp-Setup-$version-win-x64.exe" }
$Installer = [IO.Path]::GetFullPath($Installer)
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ToolKeeper.TransLamp'
$startLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'ToolKeeper\TransLamp.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'TransLamp.lnk'
foreach ($existing in @($registry, $startLink, $desktopLink)) {
    if (Test-Path -LiteralPath $existing) { throw "Existing installation/shortcut would be affected; test refused: $existing" }
}
# Do not alter shared runtime prerequisites on the development machine.
$vc = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue
if ($vc.Installed -ne 1 -or [version]$vc.Version.TrimStart('v') -lt [version]'14.44.35211.0') {
    throw 'This test requires an already installed compatible VC++ x64 runtime; it will not install shared prerequisites.'
}
$testParent = Join-Path $repo 'artifacts\translamp-installer-tests'
$root = Join-Path $testParent ([Guid]::NewGuid().ToString('N'))
if (-not $root.StartsWith($repo.TrimEnd('\') + '\artifacts\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test directory.' }
New-Item -ItemType Directory -Path $root | Out-Null
$installed = Join-Path $root 'Installed App'
$protected = Join-Path $root 'Existing Files'
New-Item -ItemType Directory -Path $protected | Out-Null
$protectedFile = Join-Path $protected 'keep.txt'
Set-Content -LiteralPath $protectedFile -Value 'Do not overwrite this unrelated directory.'
$protectedHash = (Get-FileHash -LiteralPath $protectedFile).Hash
$results = [Collections.Generic.List[object]]::new()
function Assert-Installer([string]$Name, [bool]$Passed) {
    if (-not $Passed) { throw "FAIL: $Name" }
    $results.Add([ordered]@{ name = $Name; passed = $true })
    Write-Output "PASS: $Name"
}
function Run-Setup([string]$Destination) {
    # NSIS /D is the last argument; it consumes spaces without surrounding quotes.
    $process = Start-Process -FilePath $Installer -ArgumentList @('/S', "/D=$Destination") -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(180000)) { throw 'Installer timed out; process was left intact for inspection.' }
    $process.Refresh()
    return $process.ExitCode
}
function Assert-InstalledPayload {
    $buildReport = Get-Content -LiteralPath ($Installer + '.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Installer 'Installed publish report matches verified source kit' ((Get-FileHash -LiteralPath (Join-Path $installed 'Verification\publish-verification.json')).Hash -eq $buildReport.sourcePublishVerificationSha256)
    $publish = Get-Content -LiteralPath (Join-Path $installed 'Verification\publish-verification.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($entry in $publish.files) {
        $path = Join-Path $installed $entry.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) {
            throw "Installed payload mismatch: $($entry.path)"
        }
    }
    Assert-Installer "All $(@($publish.files).Count) installed payload hashes" $true
}
$complete = $false
try {
    Assert-Installer 'Reject unrelated nonempty install directory' ((Run-Setup $protected) -ne 0)
    Assert-Installer 'Unrelated file unchanged' ((Get-FileHash -LiteralPath $protectedFile).Hash -eq $protectedHash)
    Assert-Installer 'Rejected installation creates no registration' (-not (Test-Path -LiteralPath $registry))
    Assert-Installer 'Actual silent installation' ((Run-Setup $installed) -eq 0)
    Assert-Installer 'Per-user Add/Remove Programs registration' ((Get-ItemProperty -LiteralPath $registry).InstallLocation -eq $installed)
    Assert-Installer 'Start menu shortcut' (Test-Path -LiteralPath $startLink)
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startLink)
    Assert-Installer 'Shortcut points to installed EXE' ($shortcut.TargetPath -eq (Join-Path $installed 'TransLamp.exe'))
    Assert-InstalledPayload
    & (Join-Path $repo 'scripts\Test-TransLampResources.ps1') -ResourceDirectory $installed -ReportPath (Join-Path $root 'installed-runtime-verification.json') -Smoke
    Assert-Installer 'Installed runtime translates both directions' $true
    $userFile = Join-Path $installed 'user-added-file.txt'
    Set-Content -LiteralPath $userFile -Value 'User content must survive reinstall and uninstall.'
    $userHash = (Get-FileHash -LiteralPath $userFile).Hash
    Assert-Installer 'Actual reinstall' ((Run-Setup $installed) -eq 0)
    Assert-Installer 'Reinstall keeps user-added file' ((Get-FileHash -LiteralPath $userFile).Hash -eq $userHash)
    Assert-InstalledPayload
    $uninstaller = Join-Path $root 'Uninstall-test.exe'
    Copy-Item -LiteralPath (Join-Path $installed 'Uninstall.exe') -Destination $uninstaller
    # Run an exact copy outside INSTDIR; _?= makes the wait cover actual removal.
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/S', "_?=$installed") -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(180000)) { throw 'Uninstaller timed out; evidence retained.' }
    $process.Refresh()
    Assert-Installer 'Actual silent uninstall' ($process.ExitCode -eq 0)
    Assert-Installer 'Uninstall removes registration' (-not (Test-Path -LiteralPath $registry))
    Assert-Installer 'Uninstall removes shortcuts' (-not (Test-Path -LiteralPath $startLink) -and -not (Test-Path -LiteralPath $desktopLink))
    Assert-Installer 'Uninstall removes application' (-not (Test-Path -LiteralPath (Join-Path $installed 'TransLamp.exe')))
    Assert-Installer 'Uninstall keeps user-added file' ((Get-FileHash -LiteralPath $userFile).Hash -eq $userHash)
    $complete = $true
} finally {
    [ordered]@{
        passed = $complete; installer = $Installer; installerSha256 = (Get-FileHash -LiteralPath $Installer).Hash
        testDirectory = $root; checks = $results; prerequisiteInstallationTested = $false
        note = 'Actual per-user install, reinstall and uninstall on this machine; VC++ was already present. Not clean-PC prerequisite acceptance.'
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath ($Installer + '.installation-test.json') -Encoding UTF8
    $buildRecord = Get-Content -LiteralPath ($Installer + '.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($buildRecord.sha256 -ne (Get-FileHash -LiteralPath $Installer).Hash) { throw 'Installer changed during verification.' }
    $buildRecord.installUninstallTestPassed = $complete
    $buildRecord | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($Installer + '.json') -Encoding UTF8
    if (-not $complete) { Write-Warning "Test incomplete; preserve and inspect $root and registration before rerunning." }
}
