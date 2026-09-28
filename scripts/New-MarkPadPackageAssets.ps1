#Requires -Version 5.1
<#
.SYNOPSIS
Generates the PNG logos used by the Hanqing MSIX package.
.DESCRIPTION
Uses the shared bamboo M artwork in MarkPadIconRenderer.cs. The ICO, SVG,
package logos and Store listing icon share this source. Only named logo files
in OutputDirectory are replaced; existing MSIX archives are never modified.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing
if (-not ('HanqingIconRenderer' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'MarkPadIconRenderer.cs') -ReferencedAssemblies System.Drawing
}
$destination = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($destination) | Out-Null
function Write-Logo([string]$FileName, [int]$Size) {
    $bitmap = [HanqingIconRenderer]::CreateBitmap($Size)
    try { $bitmap.Save((Join-Path $destination $FileName), [Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
}
$assetCount = 0
foreach ($logo in @(
    @{ Name = 'StoreLogo'; Size = 50 },
    @{ Name = 'Square44x44Logo'; Size = 44 },
    @{ Name = 'Square150x150Logo'; Size = 150 }
)) {
    Write-Logo "$($logo.Name).png" $logo.Size
    $assetCount++
    foreach ($scale in @(100, 125, 150, 200, 400)) {
        $size = [int][Math]::Round($logo.Size * $scale / 100.0, [MidpointRounding]::AwayFromZero)
        Write-Logo "$($logo.Name).scale-$scale.png" $size
        $assetCount++
    }
}
foreach ($size in @(16, 24, 32, 48, 256)) {
    Write-Logo "Square44x44Logo.targetsize-${size}_altform-unplated.png" $size
    $assetCount++
}
Write-Output "Generated $assetCount Hanqing package logos in $destination."