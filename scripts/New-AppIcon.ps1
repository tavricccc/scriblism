$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)
    $blue = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 0, 95, 184))
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $light = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 176, 214, 250))
    $page = [Drawing.PointF[]]@([Drawing.PointF]::new(12,4),[Drawing.PointF]::new(40,4),[Drawing.PointF]::new(54,18),[Drawing.PointF]::new(54,60),[Drawing.PointF]::new(12,60))
    $graphics.FillPolygon($blue,$page)
    $fold = [Drawing.PointF[]]@([Drawing.PointF]::new(40,4),[Drawing.PointF]::new(40,18),[Drawing.PointF]::new(54,18))
    $graphics.FillPolygon($light,$fold)
    $graphics.FillRectangle($white,21,27,24,4)
    $graphics.FillRectangle($white,21,37,18,4)
    $graphics.FillRectangle($white,21,47,11,4)
    $memory = [IO.MemoryStream]::new()
    $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$memory.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $assets 'Scriblism.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $blue.Dispose(); $white.Dispose(); $light.Dispose()
}
$stream = [IO.File]::Create((Join-Path $assets 'Scriblism.ico'))
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
$sizes = @(16,24,32,48,64,128,256)
for ($i=0;$i -lt $frames.Count;$i++) {
    $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Dispose()
Write-Output "Created $assets/Scriblism.ico"
