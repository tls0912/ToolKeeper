#Requires -Version 5.1
<#
.SYNOPSIS
Regenerates Hanqing's SVG and ICO from the shared bamboo icon renderer.
.DESCRIPTION
Uses Windows System.Drawing only. MarkPadIconRenderer.cs is the common vector
source for SVG, ICO, package PNGs, and Store artwork. The ICO retains transparent
PNG frames at 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels.
#>
[CmdletBinding()]
param(
    [string]$PreviewPath,
    [ValidateRange(16, 4096)]
    [int]$PreviewSize = 512
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$iconPath = Join-Path $repositoryRoot 'src\MarkPad\Assets\MarkPad.ico'
$svgPath = Join-Path $repositoryRoot 'src\MarkPad\Assets\MarkPad.svg'
if (-not ('HanqingIconRenderer' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'MarkPadIconRenderer.cs') -ReferencedAssemblies System.Drawing
}
[IO.File]::WriteAllText($svgPath, [HanqingIconRenderer]::CreateSvg(), [Text.UTF8Encoding]::new($false))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    $bitmap = [HanqingIconRenderer]::CreateBitmap($size)
    $stream = New-Object IO.MemoryStream
    try {
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}
$file = [IO.File]::Create($iconPath)
$writer = New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
}
finally { $writer.Dispose() }
if ($PreviewPath) {
    $resolvedPreview = [IO.Path]::GetFullPath($PreviewPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedPreview)) | Out-Null
    $bitmap = [HanqingIconRenderer]::CreateBitmap($PreviewSize)
    try { $bitmap.Save($resolvedPreview, [Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
}
Write-Output "Generated $iconPath with $($frames.Count) sizes, plus the matching $svgPath."