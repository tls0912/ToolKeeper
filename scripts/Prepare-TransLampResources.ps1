[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/translamp'),
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$sourceDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../runtime/TransLamp'))
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$cachePath = Join-Path $outputPath 'downloads'
$runtimePath = Join-Path $outputPath 'Runtime'
$lock = Get-Content -LiteralPath (Join-Path $sourceDirectory 'resources.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json

if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) {
    throw 'TransLamp resource preparation currently supports Windows x64 only.'
}

function Test-ResourceHash {
    param([string] $Path, [object] $Asset)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    if ((Get-Item -LiteralPath $Path).Length -ne $Asset.size) { return $false }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Asset.sha256
}

New-Item -ItemType Directory -Path $cachePath -Force | Out-Null
foreach ($asset in $lock.assets) {
    $destination = Join-Path $cachePath $asset.fileName
    if (-not (Test-ResourceHash -Path $destination -Asset $asset)) {
        if ($Offline) { throw "Missing or invalid offline cache: $($asset.fileName)" }
        if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) { throw 'curl.exe is required to download the pinned resources.' }
        $partial = "$destination.partial"
        Write-Host "Downloading $($asset.fileName) ($($asset.size) bytes)..."
        # Resume interrupted transfers; only verified final files are consumed.
        & curl.exe --fail --location --silent --show-error --continue-at - --connect-timeout 20 --max-time 1800 --output $partial $asset.url
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $($asset.fileName). Rerun to resume." }
        if (-not (Test-ResourceHash -Path $partial -Asset $asset)) {
            throw "Resource integrity check failed: $($asset.fileName). Do not use this download; verify the source before retrying."
        }
        Move-Item -LiteralPath $partial -Destination $destination -Force
    }
    if ($asset.kind -eq 'prerequisite') {
        $signature = Get-AuthenticodeSignature -LiteralPath $destination
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
            throw "Microsoft prerequisite signature validation failed: $($asset.fileName)"
        }
    }
    Write-Host "Verified $($asset.fileName)"
}

$pythonAsset = $lock.assets | Where-Object { $_.kind -eq 'python' } | Select-Object -First 1
New-Item -ItemType Directory -Path $runtimePath -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $cachePath $pythonAsset.fileName))
try {
    foreach ($entry in $archive.Entries) {
        # Official embeddable Python is a flat ZIP. Refuse unexpected paths.
        if ($entry.Name -ne $entry.FullName -or $entry.Name -match '[:\\/]') { throw 'Unexpected Python archive layout.' }
        $destination = Join-Path $runtimePath $entry.Name
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
    }
} finally {
    $archive.Dispose()
}
[IO.File]::WriteAllText((Join-Path $runtimePath 'python312._pth'), "python312.zip`n.`nLib/site-packages`n", [Text.UTF8Encoding]::new($false))
$python = Join-Path $runtimePath 'python.exe'
& $python -I -B (Join-Path $sourceDirectory 'build_resources.py') --output $outputPath
if ($LASTEXITCODE -ne 0) { throw 'TransLamp resource packaging failed.' }

$unitTests = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/runtime/test_translamp_worker.py'))
& $python -I -B $unitTests
if ($LASTEXITCODE -ne 0) { throw 'TransLamp worker regression tests failed.' }
$verification = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/runtime/verify_translamp_runtime.py'))
& $python -I -B $verification --resources $outputPath
if ($LASTEXITCODE -ne 0) { throw 'TransLamp CPU translation verification failed.' }
Write-Host "TransLamp resources are ready in $outputPath"
