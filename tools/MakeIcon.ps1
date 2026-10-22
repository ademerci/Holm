# Draws Holm's icon - the notch as a small island on the horizon: a black pill where a warm sky
# meets the sea - and writes WinNotch\notch.ico (16 to 256 px) and docs\icon.png. Windows PowerShell:
#   powershell -ExecutionPolicy Bypass -File tools\MakeIcon.ps1 [-Preview path.png]
param([string]$Preview)

Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent

$Sky = [System.Drawing.Color]::FromArgb(255, 0xFF, 0xC8, 0x57)
$Sea = [System.Drawing.Color]::FromArgb(255, 0x12, 0x7E, 0x8C)
$Notch = [System.Drawing.Color]::FromArgb(255, 0x0B, 0x0D, 0x10)

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [single]$size

    # Tile: sky above the horizon, sea below.
    $m = [Math]::Max(0.5, $s * 0.035)
    $tile = New-RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.24)
    $horizon = [single][Math]::Round($s * 0.56)
    $g.SetClip($tile)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush($Sky)), 0, 0, $s, $horizon)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush($Sea)), 0, $horizon, $s, $s - $horizon)
    $g.ResetClip()

    # The notch, sitting on the horizon like an island.
    $pw = $s * 0.58; $ph = [Math]::Max(3, $s * 0.21)
    $pill = New-RoundRect (($s - $pw) / 2) ($horizon - $ph / 2) $pw $ph ($ph / 2)
    $g.FillPath((New-Object System.Drawing.SolidBrush($Notch)), $pill)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($size in $sizes) {
    $bmp = Draw-Icon $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { $bmp.Save((Join-Path $root 'docs\icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO: header, one directory entry per size, then the PNGs.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $root 'WinNotch\notch.ico'), $out.ToArray())

if ($Preview) {
    # All sizes side by side on light and dark, at 1:1 and 4x, to judge the small ones.
    $sheet = New-Object System.Drawing.Bitmap(1100, 620)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 243, 243, 243))), 0, 0, 1100, 310)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 32, 32, 32))), 0, 310, 1100, 310)
    foreach ($band in 0, 310) {
        $x = 10
        foreach ($size in 16, 24, 32, 48) {
            $bmp = Draw-Icon $size
            $g.DrawImage($bmp, $x, $band + 10, $size, $size)
            $g.DrawImage($bmp, $x, $band + 70, $size * 4, $size * 4)
            $x += [Math]::Max($size * 4, 70) + 20
            $bmp.Dispose()
        }
        $big = Draw-Icon 256
        $g.DrawImage($big, 830, $band + 25, 256, 256)
        $big.Dispose()
    }
    $g.Dispose()
    $sheet.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
}
