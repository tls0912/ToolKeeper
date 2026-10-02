[CmdletBinding()]
param(
    [string]$ResourceDirectory,
    [string]$OutputDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$transLampRepo = Split-Path $PSScriptRoot -Parent
if (-not $ResourceDirectory) { $ResourceDirectory = Join-Path $transLampRepo 'artifacts\translamp' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $transLampRepo 'artifacts\TransLamp-OfflineKit' }
$ResourceDirectory = [IO.Path]::GetFullPath($ResourceDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

foreach ($transLampRequired in @('Runtime\python.exe', 'Runtime\translate.py', 'Prerequisites\VC_redist.x64.exe', 'resource-build.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ResourceDirectory $transLampRequired) -PathType Leaf)) {
        throw 'Offline resources are not ready. Run scripts/Prepare-TransLampResources.ps1 first.'
    }
}
$transLampPacks = @(Get-ChildItem -LiteralPath (Join-Path $ResourceDirectory 'LanguagePacks') -Filter '*.tlpack' -File)
if ($transLampPacks.Count -ne 2) { throw 'The default Offline Kit requires both verified Chinese/English language packs.' }
if ($OutputDirectory -eq $ResourceDirectory -or $ResourceDirectory.StartsWith($OutputDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output must be separate from the prepared resources.'
}

& dotnet publish (Join-Path $transLampRepo 'src\TransLamp\TransLamp.csproj') -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "TransLamp publish failed ($LASTEXITCODE)." }
foreach ($transLampSubdirectory in @('Runtime', 'LanguagePacks')) {
    $transLampDestination = Join-Path $OutputDirectory $transLampSubdirectory
    New-Item -ItemType Directory -Path $transLampDestination -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $ResourceDirectory $transLampSubdirectory) -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $transLampDestination -Recurse -Force
    }
}
if (Test-Path -LiteralPath (Join-Path $ResourceDirectory 'Prerequisites')) {
    $transLampPrerequisites = Join-Path $OutputDirectory 'Prerequisites'
    New-Item -ItemType Directory -Path $transLampPrerequisites -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $ResourceDirectory 'Prerequisites') -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $transLampPrerequisites -Recurse -Force
    }
}
Copy-Item -LiteralPath (Join-Path $transLampRepo 'packaging\TransLamp\README.md') -Destination (Join-Path $OutputDirectory 'README.md') -Force
if (Test-Path -LiteralPath (Join-Path $ResourceDirectory 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $ResourceDirectory 'THIRD-PARTY-NOTICES.md') -Destination $OutputDirectory -Force
}
Copy-Item -LiteralPath (Join-Path $transLampRepo 'docs\TRANSLAMP-IMPLEMENTATION.md') -Destination (Join-Path $OutputDirectory 'IMPLEMENTATION-NOTES.md') -Force
foreach ($transLampEvidence in @('resource-build.json', 'verification.json', 'quality-samples.json', 'quality-review.json')) {
    if (Test-Path -LiteralPath (Join-Path $ResourceDirectory $transLampEvidence)) {
        $transLampEvidenceDirectory = Join-Path $OutputDirectory 'Verification'
        New-Item -ItemType Directory -Path $transLampEvidenceDirectory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $ResourceDirectory $transLampEvidence) -Destination $transLampEvidenceDirectory -Force
    }
}
Write-Output "Offline Kit: $OutputDirectory"
Write-Output 'Run TransLamp.exe. No Python or .NET installation is required on the target PC.'
Write-Output 'This is an unsigned development build; Store publishing and old-PC certification remain separate.'
