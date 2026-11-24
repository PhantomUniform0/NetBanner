<#
  Builds assets\NetBanner.ico (concept B, "marking spectrum") from simple shapes on a 64-unit grid.
  Each size is drawn separately; at small sizes shapes are snapped to whole pixels so they stay crisp.
  Usage: .\tools\Make-Icon.ps1   (writes assets\NetBanner.ico and assets\png\*.png previews)
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root   = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'assets'
$pngDir = Join-Path $assets 'png'
New-Item -ItemType Directory -Force $pngDir | Out-Null

$sizes   = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frame   = '#1F2937'
$screen  = '#F3F4F6'
$line    = '#D1D5DB'
$marking = '#007A33', '#502B85', '#0033A0', '#C8102E', '#FF8C00', '#FCE83A'

function Get-Brush([string]$hex) { New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex)) }

function Add-Rect($g, [double]$s, [bool]$snap, [double]$x, [double]$y, [double]$w, [double]$h, [string]$hex, [double]$r = 0) {
    if ($snap) {
        $x0 = [math]::Round($x * $s); $y0 = [math]::Round($y * $s)
        $x1 = [math]::Max($x0 + 1, [math]::Round(($x + $w) * $s)); $y1 = [math]::Max($y0 + 1, [math]::Round(($y + $h) * $s))
        $rect = New-Object System.Drawing.RectangleF ([single]$x0), ([single]$y0), ([single]($x1 - $x0)), ([single]($y1 - $y0))
    } else {
        $rect = New-Object System.Drawing.RectangleF ([single]($x * $s)), ([single]($y * $s)), ([single]($w * $s)), ([single]($h * $s))
    }

    $brush = Get-Brush $hex
    $rad = $r * $s
    if ($rad -ge 1.5) {
        $d = [single]([math]::Min($rad * 2, [math]::Min($rect.Width, $rect.Height)))
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $p.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $p.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $p.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $p.CloseFigure()
        $g.FillPath($brush, $p)
        $p.Dispose()
    } else {
        $g.FillRectangle($brush, $rect)
    }

    $brush.Dispose()
}

# 64 px and up: the 64-unit design, scaled.
function Draw-Large($g, [double]$s) {
    Add-Rect $g $s $false 5 8 54 48 $frame 6
    for ($i = 0; $i -lt 6; $i++) {
        $a = 9 + 46 * $i / 6; $b = 9 + 46 * ($i + 1) / 6
        Add-Rect $g $s $false $a 12 ($b - $a) 10 $marking[$i]
    }
    Add-Rect $g $s $false 9 25 46 27 $screen 2
    Add-Rect $g $s $false 13 30 22 3 $line 1.5
    Add-Rect $g $s $false 13 36 30 3 $line 1.5
    Add-Rect $g $s $false 13 42 18 3 $line 1.5
}

# 48 px and below: laid out in whole pixels, colour band a multiple of 6 so all six stripes are equal.
function Draw-Small($g, [int]$px) {
    $border = [int][math]::Max(1, [math]::Round($px / 16.0, [MidpointRounding]::AwayFromZero))
    $margin = if ($px -le 20) { 0 } else { 2 }
    $stripe = [int][math]::Floor(($px - $margin - 2 * $border) / 6)
    $innerW = 6 * $stripe
    $frameW = $innerW + 2 * $border
    $left = [int][math]::Floor(($px - $frameW) / 2)
    $bandH = [int][math]::Max(3, [math]::Round($px * 0.17, [MidpointRounding]::AwayFromZero))
    $top = [int][math]::Max(1, [math]::Round($px * 0.11, [MidpointRounding]::AwayFromZero))
    $bottom = $px - [int][math]::Max(1, [math]::Round($px * 0.06, [MidpointRounding]::AwayFromZero))
    $gap = [int][math]::Max(1, [math]::Floor($border / 2))

    Add-Rect $g 1 $false $left $top $frameW ($bottom - $top) $frame ([math]::Max(0, $px / 10.0))
    $x = $left + $border; $y = $top + $border
    for ($i = 0; $i -lt 6; $i++) { Add-Rect $g 1 $false ($x + $i * $stripe) $y $stripe $bandH $marking[$i] }

    $sy = $y + $bandH + $gap
    $sh = $bottom - $border - $sy
    Add-Rect $g 1 $false $x $sy $innerW $sh $screen
    if ($px -ge 20) {
        $lh = [int][math]::Max(1, [math]::Round($px / 24.0, [MidpointRounding]::AwayFromZero))
        $pad = [int][math]::Max(2, [math]::Round($px / 12.0, [MidpointRounding]::AwayFromZero))
        $step = $lh * 2 + [int]($px -ge 32)
        $widths = 0.5, 0.7, 0.4
        for ($j = 0; $j -lt 3; $j++) {
            $ly = $sy + $pad + $j * $step
            if ($ly + $lh -gt $sy + $sh - $pad + 1) { break }
            Add-Rect $g 1 $false ($x + $pad) $ly ([math]::Round(($innerW - 2 * $pad) * $widths[$j] / 0.7)) $lh $line
        }
    }
}

function New-IconPng([int]$px) {
    $bmp = New-Object System.Drawing.Bitmap $px, $px, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'Half'
    $g.Clear([System.Drawing.Color]::Transparent)

    if ($px -le 48) {
        Draw-Small $g $px
    } else {
        Draw-Large $g ($px / 64.0)
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Save((Join-Path $pngDir "NetBanner-$px.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# ICO container with PNG-compressed images (supported for every size on Windows Vista and later).
$images = foreach ($px in $sizes) { , (New-IconPng $px) }
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $px = $sizes[$i]; $data = $images[$i]
    $dim = if ($px -ge 256) { 0 } else { $px }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $w.Write($data) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'NetBanner.ico'), $out.ToArray())
"Wrote $(Join-Path $assets 'NetBanner.ico') ($($sizes -join ', ') px)"
