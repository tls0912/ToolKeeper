[CmdletBinding()]
param(
    [string]$ResourceDirectory,
    [string]$OutputDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$transLampRepo = Split-Path $PSScriptRoot -Parent
$transLampProject = Join-Path $transLampRepo 'src\TransLamp\TransLamp.csproj'
$transLampVersion = ([xml](Get-Content -LiteralPath $transLampProject -Raw)).Project.PropertyGroup.Version | Select-Object -First 1
if (-not $ResourceDirectory) { $ResourceDirectory = Join-Path $transLampRepo 'artifacts\translamp' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $transLampRepo "artifacts\TransLamp-OfflineKit-$transLampVersion" }

function Get-TransLampNormalPath {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -eq [IO.Path]::GetPathRoot($full)) { return $full }
    return $full.TrimEnd('\', '/')
}

function Test-TransLampContainedPath {
    param([string]$Child, [string]$Parent)
    return $Child.Equals($Parent, [StringComparison]::OrdinalIgnoreCase) -or
        $Child.StartsWith($Parent.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-TransLampRegularPath {
    param([string]$Path)
    $current = $Path
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Linked publish paths are not allowed: $Path"
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
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked publish content is not allowed: $($entry.FullName)" }
        if ($entry.PSIsContainer) { Get-TransLampRegularFiles $entry.FullName -Nested }
        else { $entry }
    }
}

function Assert-TransLampManagedOutput {
    if (-not (Test-Path -LiteralPath $OutputDirectory)) { return }
    if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) { throw 'Publish output already exists as a file.' }
    Assert-TransLampRegularPath $OutputDirectory
    if (@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -eq 0) { return }
    $markerPath = Join-Path $OutputDirectory '.translamp-offline-kit.json'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf) -or (Get-Item -LiteralPath $markerPath).Length -gt 64KB) {
        throw 'Nonempty publish output is not a managed TransLamp kit. Choose a new OutputDirectory; existing files were preserved.'
    }
    Assert-TransLampRegularPath $markerPath
    $marker = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $report = Join-Path $OutputDirectory 'Verification\publish-verification.json'
    Assert-TransLampRegularPath $report
    if ($marker.schemaVersion -ne 1 -or $marker.product -ne 'toolkeeper.translamp.offlinekit' -or $marker.complete -ne $true -or
        -not (Test-Path -LiteralPath $report -PathType Leaf) -or
        $marker.publishVerificationSha256 -ne (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash) {
        throw 'Publish output marker is invalid. Choose a new OutputDirectory; existing files were preserved.'
    }
    $null = @(Get-TransLampRegularFiles $OutputDirectory)
}

function Assert-TransLampOwnedSibling {
    param([string]$Path, [ValidateSet('stage', 'backup')][string]$Kind)
    $normal = Get-TransLampNormalPath $Path
    if (-not (Split-Path $normal -Parent).Equals($transLampOutputParent, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $normal -Leaf) -ne ".$transLampOutputName.translamp-$Kind-$transLampRunId" -or
        (Test-TransLampContainedPath $normal $ResourceDirectory) -or (Test-TransLampContainedPath $ResourceDirectory $normal)) {
        throw "Refusing unsafe $Kind path: $Path"
    }
    Assert-TransLampRegularPath $normal
    if (Test-Path -LiteralPath $normal -PathType Container) { $null = @(Get-TransLampRegularFiles $normal) }
}

function Remove-TransLampOwnedSibling {
    param([string]$Path, [ValidateSet('stage', 'backup')][string]$Kind)
    if (Test-Path -LiteralPath $Path) {
        # Resolve and inspect before each recursive operation. Only this run's
        # exact sibling staging/backup directory is eligible for cleanup.
        Assert-TransLampOwnedSibling $Path $Kind
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

$ResourceDirectory = Get-TransLampNormalPath $ResourceDirectory
$OutputDirectory = Get-TransLampNormalPath $OutputDirectory
if ($OutputDirectory -eq [IO.Path]::GetPathRoot($OutputDirectory) -or
    (Test-TransLampContainedPath $OutputDirectory $ResourceDirectory) -or
    (Test-TransLampContainedPath $ResourceDirectory $OutputDirectory)) {
    throw 'Publish output must be separate from prepared resources (neither equal, ancestor nor descendant).'
}
Assert-TransLampRegularPath $ResourceDirectory
Assert-TransLampRegularPath $OutputDirectory
$transLampOutputParent = Split-Path $OutputDirectory -Parent
$transLampOutputName = Split-Path $OutputDirectory -Leaf
$transLampRunId = [Guid]::NewGuid().ToString('N')
$transLampStage = Join-Path $transLampOutputParent ".$transLampOutputName.translamp-stage-$transLampRunId"
$transLampBackup = Join-Path $transLampOutputParent ".$transLampOutputName.translamp-backup-$transLampRunId"
Assert-TransLampManagedOutput
New-Item -ItemType Directory -Path $transLampOutputParent -Force | Out-Null
$transLampPublishLock = $null
$transLampCommitted = $false
$transLampOldMoved = $false

try {
    Assert-TransLampRegularPath (Join-Path $transLampOutputParent ".$transLampOutputName.translamp-publish.lock")
    $transLampPublishLock = [IO.FileStream]::new((Join-Path $transLampOutputParent ".$transLampOutputName.translamp-publish.lock"),
        [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
    Assert-TransLampManagedOutput
    Assert-TransLampOwnedSibling $transLampStage 'stage'
    New-Item -ItemType Directory -Path $transLampStage | Out-Null
    $transLampEvidenceDirectory = Join-Path $transLampStage 'Verification'
    New-Item -ItemType Directory -Path $transLampEvidenceDirectory | Out-Null
    $transLampVerifier = Join-Path $PSScriptRoot 'Test-TransLampResources.ps1'
    & $transLampVerifier -ResourceDirectory $ResourceDirectory -ReportPath (Join-Path $transLampEvidenceDirectory 'preflight-resource-verification.json')

    & dotnet publish $transLampProject -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:TransLampResourceDirectory=$ResourceDirectory" -o $transLampStage
    if ($LASTEXITCODE -ne 0) { throw "TransLamp publish failed ($LASTEXITCODE). Existing output was preserved." }
    foreach ($required in @('TransLamp.exe', 'TransLamp.dll', 'TransLamp.runtimeconfig.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $transLampStage $required) -PathType Leaf)) { throw "Published application file missing: $required" }
    }
    foreach ($subdirectory in @('Runtime', 'LanguagePacks', 'Prerequisites')) {
        $destination = Join-Path $transLampStage $subdirectory
        # Build output must not supply an unexpected second runtime tree.
        if (Test-Path -LiteralPath $destination) { throw "Build unexpectedly supplied $subdirectory; verify project publish settings." }
        New-Item -ItemType Directory -Path $destination | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $ResourceDirectory $subdirectory) -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
        }
    }
    Copy-Item -LiteralPath (Join-Path $ResourceDirectory 'resource-build.json') -Destination $transLampStage
    Copy-Item -LiteralPath (Join-Path $transLampRepo 'packaging\TransLamp\README.md') -Destination (Join-Path $transLampStage 'README.md')
    Copy-Item -LiteralPath (Join-Path $transLampRepo 'docs\TRANSLAMP-IMPLEMENTATION.md') -Destination (Join-Path $transLampStage 'IMPLEMENTATION-NOTES.md')
    Copy-Item -LiteralPath (Join-Path $transLampRepo 'runtime\TransLamp\verify_publish_resources.py') -Destination $transLampEvidenceDirectory
    $transLampResourceReportPath = Join-Path $transLampEvidenceDirectory 'resource-verification.json'
    & $transLampVerifier -ResourceDirectory $transLampStage -ReportPath $transLampResourceReportPath -Smoke
    $transLampReport = Get-Content -LiteralPath $transLampResourceReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $transLampFiles = @(Get-TransLampRegularFiles $transLampStage | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($transLampStage.Length + 1).Replace('\', '/')
            size = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $transLampFinalReportPath = Join-Path $transLampEvidenceDirectory 'publish-verification.json'
    $transLampFinalReport = [ordered]@{
        schemaVersion = 1
        product = 'toolkeeper.translamp.offlinekit'
        productVersion = [string]$transLampVersion
        publishedAtUtc = [DateTime]::UtcNow.ToString('o')
        passed = $true
        validationScope = 'This staged kit file inventory and its actual worker in both directions; no semantic translation-quality acceptance.'
        translationQualityAccepted = $null
        resourceVerification = $transLampReport
        files = $transLampFiles
        inventoryExclusions = @('Verification/publish-verification.json', '.translamp-offline-kit.json')
    }
    $transLampFinalReport | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $transLampFinalReportPath -Encoding UTF8
    [ordered]@{
        schemaVersion = 1
        product = 'toolkeeper.translamp.offlinekit'
        productVersion = [string]$transLampVersion
        complete = $true
        publishRunId = $transLampRunId
        publishVerificationSha256 = (Get-FileHash -LiteralPath $transLampFinalReportPath -Algorithm SHA256).Hash.ToLowerInvariant()
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $transLampStage '.translamp-offline-kit.json') -Encoding UTF8

    Assert-TransLampManagedOutput
    Assert-TransLampOwnedSibling $transLampStage 'stage'
    Assert-TransLampOwnedSibling $transLampBackup 'backup'
    if (Test-Path -LiteralPath $OutputDirectory) {
        if (@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -eq 0) { Remove-Item -LiteralPath $OutputDirectory }
        else {
            [IO.Directory]::Move($OutputDirectory, $transLampBackup)
            $transLampOldMoved = $true
        }
    }
    try {
        [IO.Directory]::Move($transLampStage, $OutputDirectory)
        $transLampCommitted = $true
    } catch {
        if ($transLampOldMoved -and -not (Test-Path -LiteralPath $OutputDirectory)) {
            Assert-TransLampOwnedSibling $transLampBackup 'backup'
            [IO.Directory]::Move($transLampBackup, $OutputDirectory)
            $transLampOldMoved = $false
        }
        throw
    }
    if ($transLampOldMoved) {
        try { Remove-TransLampOwnedSibling $transLampBackup 'backup' }
        catch { Write-Warning "New kit is ready; previous kit cleanup failed and was preserved at $transLampBackup. $($_.Exception.Message)" }
    }
    Write-Output "Offline Kit: $OutputDirectory"
    Write-Output 'Verified the exact shipped runtime and both translation directions. Run TransLamp.exe.'
    Write-Output 'This is an unsigned development build; semantic quality, clean-PC and old-PC acceptance remain separate.'
} finally {
    if (-not $transLampCommitted -and (Test-Path -LiteralPath $transLampStage)) {
        try { Remove-TransLampOwnedSibling $transLampStage 'stage' }
        catch { Write-Warning "Failed publish staging was preserved at $transLampStage. $($_.Exception.Message)" }
    }
    if ($transLampPublishLock) { $transLampPublishLock.Dispose() }
}
