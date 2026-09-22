#Requires -Version 5.1
<#
.SYNOPSIS
Regenerates the MarkPad ICO from the geometry and colors in Assets/MarkPad.svg.
.DESCRIPTION
Uses Windows System.Drawing only. The ICO contains transparent PNG frames at
16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels for crisp Windows scaling.
#>
[CmdletBinding()]
param([string]$PreviewPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$iconPath = Join-Path $repositoryRoot 'src\MarkPad\Assets\MarkPad.ico'

function New-RoundedRectangle([single]$X, [single]$Y, [single]$Width, [single]$Height, [single]$Radius) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$Size) {
    # Supersample the vector artwork to preserve clean edges at small sizes.
    $large = New-Object Drawing.Bitmap ($Size * 4), ($Size * 4)
    $canvas = [Drawing.Graphics]::FromImage($large)
    $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $canvas.ScaleTransform($Size / 64.0, $Size / 64.0)
    $background = New-RoundedRectangle 10 10 236 236 50
    $gradient = New-Object Drawing.Drawing2D.LinearGradientBrush (
        (New-Object Drawing.PointF 0, 10), (New-Object Drawing.PointF 0, 246),
        [Drawing.ColorTranslator]::FromHtml('#2784EE'), [Drawing.ColorTranslator]::FromHtml('#0958C2'))
    $canvas.FillPath($gradient, $background)
    $points = @(
        @(58, 176), @(58, 80), @(83, 80), @(128, 135), @(173, 80), @(198, 80),
        @(198, 176), @(171, 176), @(171, 122), @(128, 174), @(85, 122), @(85, 176)
    ) | ForEach-Object { New-Object Drawing.PointF ([single]$_[0]), ([single]$_[1]) }
    $canvas.FillPolygon([Drawing.Brushes]::White, [Drawing.PointF[]]$points)
    $line = New-RoundedRectangle 88 196 80 8 4
    $lineBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(128, 255, 255, 255))
    $canvas.FillPath($lineBrush, $line)
    $canvas.Dispose()
    $lineBrush.Dispose()
    $line.Dispose()
    $gradient.Dispose()
    $background.Dispose()

    $bitmap = New-Object Drawing.Bitmap $Size, $Size
    $downsample = [Drawing.Graphics]::FromImage($bitmap)
    $downsample.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
    $downsample.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $downsample.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $downsample.DrawImage($large, (New-Object Drawing.Rectangle 0, 0, $Size, $Size))
    $downsample.Dispose()
    $large.Dispose()
    return $bitmap
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256 -and $PreviewPath) {
        $resolvedPreview = [IO.Path]::GetFullPath($PreviewPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedPreview)) | Out-Null
        $bitmap.Save($resolvedPreview, [Drawing.Imaging.ImageFormat]::Png)
    }
    [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose()
    $bitmap.Dispose()
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
Write-Output "Generated $iconPath with $($frames.Count) sizes."
