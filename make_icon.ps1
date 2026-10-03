# Generates crosshair.ico (16/24/32/48/64/128/256 px, PNG-encoded frames).
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function Draw-Frame([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $u = $n / 32.0                      # design grid is 32x32
    $green = [System.Drawing.Color]::FromArgb(0, 255, 90)

    # Dark rounded-square background
    $r = 7 * $u; $pad = 0.5 * $u; $w = $n - 2 * $pad
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($pad, $pad, 2*$r, 2*$r, 180, 90)
    $path.AddArc($pad + $w - 2*$r, $pad, 2*$r, 2*$r, 270, 90)
    $path.AddArc($pad + $w - 2*$r, $pad + $w - 2*$r, 2*$r, 2*$r, 0, 90)
    $path.AddArc($pad, $pad + $w - 2*$r, 2*$r, 2*$r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $n), ([System.Drawing.Color]::FromArgb(40, 44, 52)), ([System.Drawing.Color]::FromArgb(14, 16, 20))
    $g.FillPath($bg, $path)

    $c = $n / 2.0
    # Ring (skip on 16px, too noisy)
    if ($n -ge 24) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, $green)), (1.6 * $u)
        $rr = 10 * $u
        $g.DrawEllipse($pen, $c - $rr, $c - $rr, 2*$rr, 2*$rr)
    }

    # Crosshair arms + dot, black outline under green
    $t = [Math]::Max(2.0, 3 * $u); $gap = 3.5 * $u; $len = 9 * $u; $o = [Math]::Max(1.0, 1 * $u)
    if ($n -le 16) { $t = 2; $gap = 2; $len = 5; $o = 0 }
    $arms = @(
        @(($c - $gap - $len), ($c - $t/2), $len, $t),
        @(($c + $gap), ($c - $t/2), $len, $t),
        @(($c - $t/2), ($c - $gap - $len), $t, $len),
        @(($c - $t/2), ($c + $gap), $t, $len),
        @(($c - $t/2), ($c - $t/2), $t, $t)
    )
    $g.SmoothingMode = 'None'
    $g.PixelOffsetMode = 'Half'
    if ($o -gt 0) {
        foreach ($a in $arms) { $g.FillRectangle([System.Drawing.Brushes]::Black, $a[0] - $o, $a[1] - $o, $a[2] + 2*$o, $a[3] + 2*$o) }
    }
    $fb = New-Object System.Drawing.SolidBrush $green
    foreach ($a in $arms) { $g.FillRectangle($fb, $a[0], $a[1], $a[2], $a[3]) }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($n -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Classic 32bpp DIB frame: header (height doubled), bottom-up BGRA, AND mask
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$n); $bw.Write([Int32]($n * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($n * $n * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0)
        $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $n - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $n; $x++) {
                $p = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$p.B); $bw.Write([byte]$p.G); $bw.Write([byte]$p.R); $bw.Write([byte]$p.A)
            }
        }
        $maskRow = [int]([Math]::Ceiling($n / 32.0) * 4)
        $bw.Write((New-Object byte[] ($maskRow * $n)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) { ,(Draw-Frame $s) }

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $frames[$i]
    $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$d.Length); $bw.Write([UInt32]$offset)
    $offset += $d.Length
}
foreach ($d in $frames) { $bw.Write($d) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'crosshair.ico'), $out.ToArray())
"crosshair.ico written"
