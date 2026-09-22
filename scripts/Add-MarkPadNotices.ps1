#Requires -Version 5.1
# Internal publish step. Check actual restored versions before copying reviewed notices.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Runtime,
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assets = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\MarkPad\obj\project.assets.json') -Raw | ConvertFrom-Json
$reviewed = Get-Content -LiteralPath (Join-Path $repositoryRoot 'packaging\reviewed-packages.json') -Raw | ConvertFrom-Json
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    $parts = $library.Name.Split('/')
    $entry = $reviewed.PSObject.Properties[$parts[0]]
    if ($null -eq $entry -or $entry.Value -ne $parts[1]) {
        throw "Review and update packaging license sources for $($library.Name) before publishing."
    }
}
$downloads = @($assets.project.frameworks.PSObject.Properties | ForEach-Object { $_.Value.downloadDependencies })
$sdk = $downloads | Where-Object { $_.name -eq 'Microsoft.Windows.SDK.NET.Ref' } | Select-Object -First 1
if ($null -eq $sdk -or $sdk.version -ne '[10.0.17763.57, 10.0.17763.57]') {
    throw 'Review the Windows SDK license source after changing the restored Windows SDK version.'
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\THIRD-PARTY-NOTICES.md') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\licenses') -Destination $OutputDirectory -Recurse

if (-not $FrameworkDependent) {
    foreach ($packageName in @("Microsoft.NETCore.App.Runtime.$Runtime", "Microsoft.WindowsDesktop.App.Runtime.$Runtime")) {
        $dependency = $downloads | Where-Object { $_.name -eq $packageName } | Select-Object -First 1
        if ($null -eq $dependency) { throw "Runtime package metadata is missing: $packageName" }
        $version = ($dependency.version.Trim('[', ']') -split ',')[0].Trim()
        $packageDirectory = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder ($packageName.ToLowerInvariant() + '\' + $version)
            if (Test-Path -LiteralPath $candidate -PathType Container) { $packageDirectory = $candidate; break }
        }
        if ($null -eq $packageDirectory) { throw "Cannot locate runtime license files: $packageName/$version" }
        $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY.NOTICES)(\..*)?$' })
        if ($notices.Count -eq 0) { throw "Runtime license files are missing: $packageName/$version" }
        $destination = Join-Path $OutputDirectory ("licenses\" + $packageName + '-' + $version)
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        foreach ($file in $notices) { Copy-Item -LiteralPath $file.FullName -Destination $destination }
        $line = '- ' + $packageName + ' ' + $version + ': original license/notice files copied from the restored runtime pack into `licenses/' + $packageName + '-' + $version + '/`.'
        Add-Content -LiteralPath (Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.md') -Value $line -Encoding UTF8
    }
}
