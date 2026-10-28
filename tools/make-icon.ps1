param([Parameter(Mandatory = $true)][string]$Out)
# Draws the NACHO logo (a chip holding a picture) at the standard icon sizes and writes a multi-size .ico (PNG frames).
# The picture loses detail as the icon shrinks: sun and two peaks from 48 px, two peaks from 24 px, one peak below.
# Same drawing as src/Nacho.Linux/Assets/nacho.svg (a 128 x 128 grid).
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = New-Object System.Collections.Generic.List[byte[]]
function P([double]$x, [double]$y, [double]$k) { New-Object System.Drawing.PointF -ArgumentList ($x * $k), ($y * $k) }
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 128.0
    $chip = [System.Drawing.Color]::FromArgb(255, 0xF2, 0xB3, 0x3D)
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x00, 0x5F, 0xB8))
    $fill = New-Object System.Drawing.SolidBrush $chip
    $pen = New-Object System.Drawing.Pen $chip, (16 * $k)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $tri = [System.Drawing.PointF[]]@((P 64 16 $k), (P 114 106 $k), (P 14 106 $k))
    $g.FillPolygon($fill, $tri)
    $g.DrawPolygon($pen, $tri)
    if ($s -ge 48) {
        $g.FillEllipse($blue, [single](70 * $k), [single](51 * $k), [single](14 * $k), [single](14 * $k))
        $g.FillPolygon($blue, [System.Drawing.PointF[]]@((P 33 96 $k), (P 53 67 $k), (P 65 82 $k), (P 75 71 $k), (P 96 96 $k)))
    } elseif ($s -ge 24) {
        $g.FillPolygon($blue, [System.Drawing.PointF[]]@((P 30 98 $k), (P 54 62 $k), (P 70 82 $k), (P 80 70 $k), (P 100 98 $k)))
    } else {
        $g.FillPolygon($blue, [System.Drawing.PointF[]]@((P 28 100 $k), (P 58 56 $k), (P 100 100 $k)))
    }
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($ms.ToArray())
    $pen.Dispose(); $fill.Dispose(); $blue.Dispose(); $g.Dispose(); $bmp.Dispose()
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
