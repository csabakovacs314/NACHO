param([Parameter(Mandatory = $true)][string]$Out)
# Draws the NACHO triangle at the standard icon sizes and writes a multi-size .ico (PNG frames).
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = New-Object System.Collections.Generic.List[byte[]]
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 20.0
    $outer = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF -ArgumentList (10 * $k), (2 * $k)),
        (New-Object System.Drawing.PointF -ArgumentList (19 * $k), (17.5 * $k)),
        (New-Object System.Drawing.PointF -ArgumentList (1 * $k), (17.5 * $k)))
    $inner = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF -ArgumentList (10 * $k), (9 * $k)),
        (New-Object System.Drawing.PointF -ArgumentList (13.6 * $k), (15.2 * $k)),
        (New-Object System.Drawing.PointF -ArgumentList (6.4 * $k), (15.2 * $k)))
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xE8, 0xA3, 0x3D))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x8A, 0x50, 0x00)), ([Math]::Max(1.0, 1.5 * $k))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.FillPolygon($fill, $outer)
    $g.DrawPolygon($pen, $outer)
    $g.FillPolygon([System.Drawing.Brushes]::White, $inner)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($ms.ToArray())
    $pen.Dispose(); $fill.Dispose(); $g.Dispose(); $bmp.Dispose()
}
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $frames[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$d.Length); $bw.Write([UInt32]$offset)
    $offset += $d.Length
}
foreach ($d in $frames) { $bw.Write($d) }
$bw.Close()
Write-Output "Icon written: $Out ($((Get-Item $Out).Length) bytes)"
