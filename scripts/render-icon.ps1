#Requires -Version 7.0
<#
.SYNOPSIS
    Renders the application icon from its vector source into a multi-size ICO file.

.DESCRIPTION
    Loads the WPF DrawingGroup of assets\icon\GitHubBackup.Icon.xaml, drawn on a 256 x 256 canvas, renders it with
    WPF into PNG frames of 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels and writes them into one ICO file. Nothing
    is downloaded.

    The build uses the committed ICO and never runs this script: run it after changing the source and commit both
    files.

    XamlReader can create any object the XAML names and run code while it loads, so run the script only on a source
    you have reviewed. As a safeguard, the script refuses a source that mentions ObjectDataProvider, x:Code or the
    System.Diagnostics namespace.

.PARAMETER SourcePath
    The XAML file with the DrawingGroup. Defaults to assets\icon\GitHubBackup.Icon.xaml under the repository root.

.PARAMETER OutputPath
    The ICO file to write. Defaults to assets\icon\GitHubBackup.ico under the repository root.

.EXAMPLE
    pwsh -File scripts/render-icon.ps1
#>
[CmdletBinding()]
param(
    [string] $SourcePath = (Join-Path $PSScriptRoot '..\assets\icon\GitHubBackup.Icon.xaml'),
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\assets\icon\GitHubBackup.ico')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# WPF renders only on a single-threaded apartment thread, which pwsh uses unless started with -MTA.
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
    throw 'Rendering with WPF needs a single-threaded apartment: run the script with pwsh -STA.'
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$canvasSize = 256.0

$source = [System.IO.Path]::GetFullPath($SourcePath)
$output = [System.IO.Path]::GetFullPath($OutputPath)

# A drawing needs none of these; they let XAML start processes or run code while it loads.
$text = [System.IO.File]::ReadAllText($source)
foreach ($forbidden in 'ObjectDataProvider', 'x:Code', 'clr-namespace:System.Diagnostics') {
    if ($text.IndexOf($forbidden, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "'$source' contains '$forbidden', which an icon source must not use."
    }
}

$reader = [System.IO.File]::OpenRead($source)
try {
    $drawing = [System.Windows.Markup.XamlReader]::Load($reader)
}
finally {
    $reader.Dispose()
}

if ($drawing -isnot [System.Windows.Media.Drawing]) {
    throw "The root element of '$source' must be a Drawing, such as a DrawingGroup."
}

$drawing.Freeze()

# Renders one square PNG frame of the drawing scaled from the canvas to the given size.
function ConvertTo-PngFrame([int] $size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $scale = $size / $canvasSize
    $context.PushTransform([System.Windows.Media.ScaleTransform]::new($scale, $scale))
    $context.DrawDrawing($drawing)
    $context.Pop()
    $context.Close()

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $buffer = [System.IO.MemoryStream]::new()
    $encoder.Save($buffer)
    return , $buffer.ToArray()
}

$frames = foreach ($size in $sizes) {
    [pscustomobject]@{ Size = $size; Data = (ConvertTo-PngFrame $size) }
}

# ICO layout: a 6-byte header, one 16-byte directory entry per frame, then the PNG data of the frames in order.
$ico = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($ico)
$writer.Write([uint16] 0)
$writer.Write([uint16] 1)
$writer.Write([uint16] $frames.Count)

$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    # A width or height of 256 is stored as 0, because the field is one byte.
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte] $dimension)
    $writer.Write([byte] $dimension)
    $writer.Write([byte] 0)
    $writer.Write([byte] 0)
    $writer.Write([uint16] 1)
    $writer.Write([uint16] 32)
    $writer.Write([uint32] $frame.Data.Length)
    $writer.Write([uint32] $offset)
    $offset += $frame.Data.Length
}

foreach ($frame in $frames) {
    $writer.Write([byte[]] $frame.Data)
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($output, $ico.ToArray())
Write-Host "Wrote $output with $($frames.Count) frames: $($sizes -join ', ') px."
