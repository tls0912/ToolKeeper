<#
Regression probes for publisher guards and preservation. Synthetic executables
are never executed. With -ResourceDirectory, dotnet is stubbed but the prepared
interpreter, pack checks and final worker smoke run for real. This does not
establish that the WPF application build succeeded.
#>
[CmdletBinding()]
param([string]$ResourceDirectory)

$ErrorActionPreference = 'Stop'
$testRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = Join-Path $testRepo ('artifacts\translamp-publish-tests\' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $testRoot 'resources'
$publisher = Join-Path $testRepo 'scripts\Publish-TransLamp.ps1'
$testResults = [Collections.Generic.List[object]]::new()
$global:transLampPublishProbeCalls = 0
$global:transLampPublishProbeMode = 'Forbidden'

function Assert-PublishProbe {
    param([string]$Name, [bool]$Condition)
    if (-not $Condition) { throw "FAIL: $Name" }
    $testResults.Add([ordered]@{ name = $Name; passed = $true })
    Write-Output "PASS: $Name"
}

function dotnet {
    $global:transLampPublishProbeCalls++
    if ($global:transLampPublishProbeMode -eq 'Forbidden') { throw 'SYNTHETIC_DOTNET_MUST_NOT_BE_REACHED' }
    if ($global:transLampPublishProbeMode -eq 'Fail') { Set-Variable -Name LASTEXITCODE -Value 1 -Scope 1; return }
    $publishArguments = @($args)
    $destination = $publishArguments[[Array]::IndexOf($publishArguments, '-o') + 1]
    foreach ($name in @('TransLamp.exe', 'TransLamp.dll', 'TransLamp.runtimeconfig.json')) {
        Set-Content -LiteralPath (Join-Path $destination $name) -Value 'SYNTHETIC BUILD OUTPUT; NEVER EXECUTE'
    }
    Set-Variable -Name LASTEXITCODE -Value 0 -Scope 1
}

function Invoke-RejectedPublish {
    param([string]$Name, [string]$Resources, [string]$Output, [string]$Expected)
    $previousCalls = $global:transLampPublishProbeCalls
    try {
        & $publisher -ResourceDirectory $Resources -OutputDirectory $Output | Out-Null
        throw "Unexpected publisher success: $Name"
    } catch {
        if ($_.Exception.Message -notmatch $Expected) { Write-Output "Unexpected rejection for ${Name}: $($_.Exception.Message)" }
        Assert-PublishProbe $Name ($_.Exception.Message -match $Expected -and $global:transLampPublishProbeCalls -eq $previousCalls)
    }
}

function New-SyntheticManagedOutput {
    param([string]$Output)
    New-Item -ItemType Directory -Path (Join-Path $Output 'Verification') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Output 'Runtime') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $Output 'Runtime\obsolete.dll') -Value 'SYNTHETIC PREVIOUS MANAGED KIT'
    $report = Join-Path $Output 'Verification\publish-verification.json'
    Set-Content -LiteralPath $report -Value '{"note":"SYNTHETIC guard fixture; never a real published application"}'
    [ordered]@{
        schemaVersion = 1; product = 'toolkeeper.translamp.offlinekit'; complete = $true
        publishVerificationSha256 = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash.ToLowerInvariant()
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output '.translamp-offline-kit.json') -Encoding UTF8
}

New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Set-Content -LiteralPath (Join-Path $fixture 'resource-build.json') -Value '{"schemaVersion":1}'
Invoke-RejectedPublish 'same source/output' $fixture $fixture 'must be separate'
Invoke-RejectedPublish 'source/output case and trailing separator' $fixture ($fixture.ToUpperInvariant() + '\') 'must be separate'
Invoke-RejectedPublish 'output within source' $fixture (Join-Path $fixture 'Runtime') 'must be separate'
Invoke-RejectedPublish 'output ancestor of source' $fixture $testRoot 'must be separate'
Invoke-RejectedPublish 'drive root output' $fixture ([IO.Path]::GetPathRoot($testRoot)) 'must be separate'
$unmanaged = Join-Path $testRoot 'user-documents'
New-Item -ItemType Directory -Path $unmanaged | Out-Null
Set-Content -LiteralPath (Join-Path $unmanaged 'important.txt') -Value 'SYNTHETIC USER DOCUMENT'
Invoke-RejectedPublish 'unmanaged output rejected' $fixture $unmanaged 'not a managed TransLamp kit'
Assert-PublishProbe 'unmanaged user file preserved' ((Get-Content -LiteralPath (Join-Path $unmanaged 'important.txt')) -eq 'SYNTHETIC USER DOCUMENT')
$oldOutput = Join-Path $testRoot 'old-managed-kit'
New-SyntheticManagedOutput $oldOutput
$oldHash = (Get-FileHash -LiteralPath (Join-Path $oldOutput 'Runtime\obsolete.dll')).Hash
Invoke-RejectedPublish 'old metadata cannot publish' $fixture $oldOutput 'old or has no inventory'
Assert-PublishProbe 'preflight failure preserves managed kit' ((Get-FileHash -LiteralPath (Join-Path $oldOutput 'Runtime\obsolete.dll')).Hash -eq $oldHash)

# Complete fixed-name synthetic files test hash rejection before execution.
$runtime = Join-Path $fixture 'Runtime'
$nativeNames = @('python.exe', 'python312.dll', 'python312.zip', 'python312._pth', 'translate.py', 'resources.lock.json',
    'Lib/site-packages/ctranslate2/_ext.cp312-win_amd64.pyd', 'Lib/site-packages/ctranslate2/ctranslate2.dll',
    'Lib/site-packages/sentencepiece/_sentencepiece.cp312-win_amd64.pyd')
foreach ($relative in $nativeNames) {
    $path = Join-Path $runtime $relative
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    Set-Content -LiteralPath $path -Value 'SYNTHETIC NONEXECUTABLE RUNTIME'
}
$source = Join-Path $testRepo 'runtime\TransLamp'
Copy-Item -LiteralPath (Join-Path $source 'translate.py') -Destination (Join-Path $runtime 'translate.py')
Copy-Item -LiteralPath (Join-Path $source 'resources.lock.json') -Destination (Join-Path $runtime 'resources.lock.json')
$lock = Get-Content -LiteralPath (Join-Path $source 'resources.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$inventory = @($nativeNames | ForEach-Object {
    $path = Join-Path $runtime $_
    [ordered]@{ path = $_; size = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() }
})
$metadata = [ordered]@{
    schemaVersion = 2; runtime = $lock.runtime; platform = $lock.platform
    resourcesLockSha256 = (Get-FileHash -LiteralPath (Join-Path $source 'resources.lock.json')).Hash.ToLowerInvariant()
    workerSha256 = (Get-FileHash -LiteralPath (Join-Path $source 'translate.py')).Hash.ToLowerInvariant()
    runtimeFiles = $inventory
    prerequisites = @([ordered]@{ file = 'VC_redist.x64.exe'; size = 1; sha256 = ('0' * 64) })
}
$metadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $fixture 'resource-build.json') -Encoding UTF8
Set-Content -LiteralPath (Join-Path $runtime 'python312.dll') -Value 'CORRUPTED SYNTHETIC RUNTIME'
Invoke-RejectedPublish 'wrong native hash rejected before executing python' $fixture $oldOutput 'missing, size or SHA256 mismatch'
Set-Content -LiteralPath (Join-Path $runtime 'python312.dll') -Value 'SYNTHETIC NONEXECUTABLE RUNTIME'
Set-Content -LiteralPath (Join-Path $runtime 'obsolete.dll') -Value 'UNINVENTORIED SYNTHETIC DLL'
Invoke-RejectedPublish 'stale extra runtime file rejected' $fixture $oldOutput 'inventory differs'

if ($ResourceDirectory) {
    $ResourceDirectory = [IO.Path]::GetFullPath($ResourceDirectory)
    $global:transLampPublishProbeMode = 'Fail'
    $callsBefore = $global:transLampPublishProbeCalls
    try {
        & $publisher -ResourceDirectory $ResourceDirectory -OutputDirectory $oldOutput | Out-Null
        throw 'Unexpected build-stub success'
    } catch {
        Assert-PublishProbe 'build failure preserves managed output' ($_.Exception.Message -match 'publish failed' -and
            $global:transLampPublishProbeCalls -eq $callsBefore + 1 -and (Get-FileHash -LiteralPath (Join-Path $oldOutput 'Runtime\obsolete.dll')).Hash -eq $oldHash)
    }
    $global:transLampPublishProbeMode = 'Success'
    & $publisher -ResourceDirectory $ResourceDirectory -OutputDirectory $oldOutput
    Assert-PublishProbe 'successful fresh stage removes old runtime DLL' (-not (Test-Path -LiteralPath (Join-Path $oldOutput 'Runtime\obsolete.dll')))
    Assert-PublishProbe 'current final verification exists' (Test-Path -LiteralPath (Join-Path $oldOutput 'Verification\publish-verification.json'))
    $report = Get-Content -LiteralPath (Join-Path $oldOutput 'Verification\publish-verification.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-PublishProbe 'both actual final workers passed smoke' ($report.resourceVerification.smokeChecks.Count -eq 2 -and
        @($report.resourceVerification.smokeChecks | Where-Object { $_.passed -ne $true }).Count -eq 0)
    Assert-PublishProbe 'historical prepared verification is not reused' (-not (Test-Path -LiteralPath (Join-Path $oldOutput 'Verification\verification.json')))
    Assert-PublishProbe 'report hashes bind final runtime bytes' ($report.resourceVerification.workerSha256 -eq
        (Get-FileHash -LiteralPath (Join-Path $oldOutput 'Runtime\translate.py')).Hash)
    $global:transLampPublishProbeMode = 'Fail'
    $appHash = (Get-FileHash -LiteralPath (Join-Path $oldOutput 'TransLamp.exe')).Hash
    try {
        & $publisher -ResourceDirectory $ResourceDirectory -OutputDirectory $oldOutput | Out-Null
        throw 'Unexpected second build-stub success'
    } catch {
        Assert-PublishProbe 'failed republish preserves newly verified kit' ($_.Exception.Message -match 'publish failed' -and
            (Get-FileHash -LiteralPath (Join-Path $oldOutput 'TransLamp.exe')).Hash -eq $appHash)
    }
}

$testResults | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $testRoot 'results.json') -Encoding UTF8
Write-Output "PASS: $($testResults.Count) packaging probes. Evidence: $testRoot"
