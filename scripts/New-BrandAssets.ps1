$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../assets'))
[System.IO.Directory]::CreateDirectory($assetRoot) | Out-Null
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(25,29,36))
    $outline = New-Object System.Drawing.Drawing2D.GraphicsPath
    $outline.AddArc(0,0,128,128,180,90)
    $outline.AddArc(128,0,128,128,270,90)
    $outline.AddArc(128,128,128,128,0,90)
    $outline.AddArc(0,128,128,128,90,90)
    $outline.CloseFigure()
    $graphics.FillPath($background, $outline)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(89,218,173),17)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $bars = @(@(56,102,154),@(92,74,182),@(128,98,158),@(164,74,182),@(200,102,154))
    foreach ($bar in $bars) { $graphics.DrawLine($pen, $bar[0], $bar[1], $bar[0], $bar[2]) }
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size=$size; Bytes=$stream.ToArray() }
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetRoot 'hushtype.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $pen.Dispose(); $outline.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $assetRoot 'hushtype.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
