#Requires -Version 5.1
<#
.SYNOPSIS
Renders HistoLens's vector master to a transparent PNG and multi-size Windows ICO.
.DESCRIPTION
Uses Windows WPF only. Run in an STA PowerShell process; no external asset tools
are required. Edit src/HistoLens/Assets/HistoLensIcon.xaml to change the artwork.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetDirectory = Join-Path $repositoryRoot 'src\HistoLens\Assets'
$source = [IO.File]::OpenRead((Join-Path $assetDirectory 'HistoLensIcon.xaml'))
try { $drawing = [Windows.Markup.XamlReader]::Load($source) }
finally { $source.Dispose() }
$drawing.Freeze()

function Render-IconPng([int]$Size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    try { $context.DrawImage($drawing, [Windows.Rect]::new(0, 0, $Size, $Size)) }
    finally { $context.Close() }
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    try {
        $encoder.Save($stream)
        return ,$stream.ToArray()
    }
    finally { $stream.Dispose() }
}

[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'HistoLens.png'), (Render-IconPng 512))
$frames = foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    [pscustomobject]@{ Size = $size; Bytes = (Render-IconPng $size) }
}
$iconPath = Join-Path $assetDirectory 'HistoLens.ico'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($iconPath))
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
Write-Output "Generated $iconPath (9 sizes) and HistoLens.png (512 px)."
