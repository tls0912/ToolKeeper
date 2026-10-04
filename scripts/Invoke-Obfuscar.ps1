#Requires -Version 5.1
<# Invoked by Obfuscar.targets after publish inputs are resolved. Never edits compiler outputs. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ToolPath,
    [Parameter(Mandatory)][string]$WorkDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Get-AssemblyHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '') }
    finally { $sha256.Dispose(); $stream.Dispose() }
}
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$inputDirectory = Join-Path $WorkDirectory 'input'
$outputDirectory = Join-Path $WorkDirectory 'output'
if ((Test-Path -LiteralPath $inputDirectory) -or (Test-Path -LiteralPath $outputDirectory)) {
    throw 'Obfuscar requires a fresh work directory; stale obfuscated files must never be reused.'
}
New-Item -ItemType Directory -Path $inputDirectory, $outputDirectory | Out-Null
$assemblies = @(Get-Content -LiteralPath (Join-Path $WorkDirectory 'assemblies.txt') -Encoding UTF8 | Where-Object { $_.Trim() })
if ($assemblies.Count -eq 0) { throw 'No assemblies supplied to Obfuscar.' }
[xml]$configuration = Get-Content -LiteralPath (Join-Path $repositoryRoot 'build\Obfuscar.xml') -Raw -Encoding UTF8
$configuredNames = @($configuration.Obfuscator.Module | ForEach-Object { $_.GetAttribute('file') })
$inputNames = @($assemblies | ForEach-Object { [IO.Path]::GetFileName($_) })
if (@($inputNames | Sort-Object -Unique).Count -ne $inputNames.Count) { throw 'Duplicate assembly names in publish inputs.' }
foreach ($assembly in $assemblies) {
    if ([IO.Path]::GetFileName($assembly) -notin $configuredNames) { throw "Unconfigured assembly: $assembly" }
    Copy-Item -LiteralPath $assembly -Destination $inputDirectory
}

foreach ($setting in @{ InPath = $inputDirectory; OutPath = $outputDirectory; LogFile = (Join-Path $WorkDirectory 'Mapping.xml') }.GetEnumerator()) {
    $element = $configuration.CreateElement('Var')
    $element.SetAttribute('name', $setting.Key)
    $element.SetAttribute('value', $setting.Value)
    $null = $configuration.Obfuscator.AppendChild($element)
}
foreach ($module in @($configuration.Obfuscator.Module)) {
    $name = $module.GetAttribute('file')
    if ($name -notin $inputNames) { $null = $configuration.Obfuscator.RemoveChild($module); continue }
    $module.SetAttribute('file', (Join-Path $inputDirectory $name))
    # Enum text is part of JSON, persisted settings and UI contracts.
    $skipEnums = $configuration.CreateElement('SkipEnums')
    $skipEnums.SetAttribute('value', 'true')
    $null = $module.AppendChild($skipEnums)
    # System.Text.Json binds positional-record constructor parameters by name,
    # including constructors on internal/private DTOs.
    $constructor = $configuration.CreateElement('SkipMethod')
    $constructor.SetAttribute('type', '*')
    $constructor.SetAttribute('name', '.ctor')
    $null = $module.AppendChild($constructor)
}
$referenceDirectories = @(Get-Content -LiteralPath (Join-Path $WorkDirectory 'references.txt') -Encoding UTF8 |
    Where-Object { $_.Trim() -and [IO.Path]::GetExtension($_) -eq '.dll' } |
    ForEach-Object { [IO.Path]::GetDirectoryName($_) } | Select-Object -Unique)
foreach ($directory in $referenceDirectories) {
    $search = $configuration.CreateElement('AssemblySearchPath')
    $search.SetAttribute('path', $directory)
    $null = $configuration.Obfuscator.AppendChild($search)
}
$configPath = Join-Path $WorkDirectory 'obfuscar.xml'
$configuration.Save($configPath)
& $ToolPath $configPath
if ($LASTEXITCODE -ne 0) { throw "Obfuscar failed ($LASTEXITCODE); publish stopped." }
$evidence = foreach ($assembly in $assemblies) {
    $name = [IO.Path]::GetFileName($assembly)
    $rewritten = Join-Path $outputDirectory $name
    if (-not (Test-Path -LiteralPath $rewritten -PathType Leaf)) { throw "Obfuscar did not produce $name; publish stopped." }
    $before = Get-AssemblyHash $assembly
    $after = Get-AssemblyHash $rewritten
    if ($before -eq $after) { throw "Obfuscar left $name unchanged; publish stopped." }
    [ordered]@{ assembly = $name; originalSha256 = $before; obfuscatedSha256 = $after }
}
if (-not (Test-Path -LiteralPath (Join-Path $WorkDirectory 'Mapping.xml') -PathType Leaf)) { throw 'Obfuscar mapping was not generated.' }
@{ tool = 'Obfuscar'; version = '2.2.50'; assemblies = @($evidence) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $WorkDirectory 'verification.json') -Encoding UTF8
