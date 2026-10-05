[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ResourceDirectory,
    [Parameter(Mandatory = $true)][string]$ReportPath,
    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'
$transLampRepo = Split-Path $PSScriptRoot -Parent
$transLampSource = Join-Path $transLampRepo 'runtime\TransLamp'
$ResourceDirectory = [IO.Path]::GetFullPath($ResourceDirectory)
$ReportPath = [IO.Path]::GetFullPath($ReportPath)

function Assert-TransLampRegularPath {
    param([string]$Path)
    $current = $Path
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Linked resource paths are not allowed: $Path"
            }
        }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Get-TransLampRegularFiles {
    param([string]$Directory, [switch]$Nested)
    if (-not $Nested) { Assert-TransLampRegularPath $Directory }
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "Resource directory missing: $Directory" }
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked resource is not allowed: $($entry.FullName)" }
        if ($entry.PSIsContainer) { Get-TransLampRegularFiles $entry.FullName -Nested }
        else { $entry }
    }
}

function Assert-TransLampHash {
    param([string]$Path, [object]$Size, [string]$Hash)
    Assert-TransLampRegularPath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
        $Hash -cnotmatch '^[0-9a-f]{64}$' -or
        (($Size -isnot [long]) -and ($Size -isnot [int])) -or $Size -lt 0 -or
        (Get-Item -LiteralPath $Path).Length -ne $Size -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) {
        throw "Resource missing, size or SHA256 mismatch: $Path"
    }
}

function Test-TransLampInventory {
    param([string]$Directory, [object[]]$Inventory, [string]$PathKey = 'path')
    if (-not $Inventory -or $Inventory.Count -eq 0) {
        throw 'Resource inventory is missing. Rerun scripts/Prepare-TransLampResources.ps1.'
    }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $actual = @(Get-TransLampRegularFiles $Directory)
    $actualByName = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $actual) {
        $relative = $file.FullName.Substring($Directory.TrimEnd('\', '/').Length + 1).Replace('\', '/')
        $actualByName.Add($relative, $file)
    }
    foreach ($entry in $Inventory) {
        $relative = $entry.$PathKey
        if ($relative -isnot [string] -or $relative -match '[\\:\x00]' -or $relative.StartsWith('/') -or
            @($relative.Split('/') | Where-Object { -not $_ -or $_ -in @('.', '..') -or $_ -match '[. ]$' }).Count -gt 0 -or
            -not $expected.Add($relative)) { throw 'Invalid or duplicate resource inventory path.' }
        # All ancestors and tree entries were inspected once above. Use only
        # those regular files here rather than rewalking ancestors per file.
        $file = $null
        if (-not $actualByName.TryGetValue($relative, [ref]$file) -or $entry.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            (($entry.size -isnot [long]) -and ($entry.size -isnot [int])) -or $entry.size -lt 0 -or
            $file.Length -ne $entry.size -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "Resource missing, size or SHA256 mismatch: $relative"
        }
    }
    if ($actual.Count -ne $expected.Count) { throw 'Resource inventory differs from directory contents (missing or stale files).' }
    foreach ($file in $actual) {
        $relative = $file.FullName.Substring($Directory.TrimEnd('\', '/').Length + 1).Replace('\', '/')
        if (-not $expected.Contains($relative)) { throw "Uninventoried resource: $relative" }
    }
    return ,$expected
}

Assert-TransLampRegularPath $ResourceDirectory
$transLampBuildPath = Join-Path $ResourceDirectory 'resource-build.json'
if (-not (Test-Path -LiteralPath $transLampBuildPath -PathType Leaf)) {
    throw 'Offline resources are not ready. Run scripts/Prepare-TransLampResources.ps1 first.'
}
if ((Get-Item -LiteralPath $transLampBuildPath).Length -gt 4MB) { throw 'Resource metadata is unexpectedly large.' }
$transLampBuild = Get-Content -LiteralPath $transLampBuildPath -Raw -Encoding UTF8 | ConvertFrom-Json
$transLampLockPath = Join-Path $transLampSource 'resources.lock.json'
$transLampWorkerPath = Join-Path $transLampSource 'translate.py'
$transLampLock = Get-Content -LiteralPath $transLampLockPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($transLampBuild.schemaVersion -ne 2 -or -not $transLampBuild.runtimeFiles -or -not $transLampBuild.prerequisites) {
    throw 'Resource metadata is old or has no inventory. Rerun scripts/Prepare-TransLampResources.ps1.'
}
if ($transLampBuild.runtime -ne $transLampLock.runtime -or $transLampBuild.platform -ne $transLampLock.platform -or
    $transLampBuild.resourcesLockSha256 -ne (Get-FileHash -LiteralPath $transLampLockPath -Algorithm SHA256).Hash -or
    $transLampBuild.workerSha256 -ne (Get-FileHash -LiteralPath $transLampWorkerPath -Algorithm SHA256).Hash) {
    throw 'Prepared resources differ from current runtime/worker sources. Rerun scripts/Prepare-TransLampResources.ps1.'
}
$transLampRuntime = Join-Path $ResourceDirectory 'Runtime'
$transLampRuntimeNames = Test-TransLampInventory $transLampRuntime @($transLampBuild.runtimeFiles)
foreach ($required in @('python.exe', 'python312.dll', 'python312.zip', 'python312._pth', 'translate.py', 'resources.lock.json',
    'Lib/site-packages/ctranslate2/_ext.cp312-win_amd64.pyd', 'Lib/site-packages/ctranslate2/ctranslate2.dll',
    'Lib/site-packages/sentencepiece/_sentencepiece.cp312-win_amd64.pyd')) {
    if (-not $transLampRuntimeNames.Contains($required) -or (Get-Item -LiteralPath (Join-Path $transLampRuntime $required)).Length -le 0) {
        throw "Required native runtime inventory missing or empty: $required"
    }
}
if ((Get-FileHash -LiteralPath (Join-Path $transLampRuntime 'translate.py') -Algorithm SHA256).Hash -ne $transLampBuild.workerSha256 -or
    (Get-FileHash -LiteralPath (Join-Path $transLampRuntime 'resources.lock.json') -Algorithm SHA256).Hash -ne $transLampBuild.resourcesLockSha256) {
    throw 'Prepared worker or resource lock is stale. Rerun scripts/Prepare-TransLampResources.ps1.'
}
$transLampPrerequisites = Join-Path $ResourceDirectory 'Prerequisites'
$null = Test-TransLampInventory $transLampPrerequisites @($transLampBuild.prerequisites) 'file'
foreach ($asset in @($transLampLock.assets | Where-Object { $_.kind -eq 'prerequisite' })) {
    $path = Join-Path $transLampPrerequisites $asset.fileName
    Assert-TransLampHash $path $asset.size $asset.sha256
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw "Microsoft prerequisite signature validation failed: $($asset.fileName)"
    }
}

# Only execute the interpreter after every runtime byte and native prerequisite
# was checked. The repository verifier then checks every archive payload and
# pinned direction/version; optional smoke uses the current shipped files.
$transLampVerifier = Join-Path $transLampSource 'verify_publish_resources.py'
$transLampArguments = @('-I', '-B', $transLampVerifier, '--resources', $ResourceDirectory, '--lock', $transLampLockPath,
    '--source-worker', $transLampWorkerPath, '--report', $ReportPath)
if ($Smoke) { $transLampArguments += '--smoke' }
& (Join-Path $transLampRuntime 'python.exe') @transLampArguments
if ($LASTEXITCODE -ne 0) { throw "TransLamp resource verification failed ($LASTEXITCODE). No offline kit was published." }
