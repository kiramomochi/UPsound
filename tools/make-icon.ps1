# Gera assets\UPsound.ico (16 a 256 px) e assets\UPsound.png: tres barras subindo,
# a ultima em laranja-vermelho, sobre um quadrado arredondado grafite.
param([string]$OutDir = (Join-Path $PSScriptRoot '..\assets'))
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [Math]::Max(0.5, $s * 0.04)
    $bg = RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) (($s - 2 * $m) * 0.235)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new(0, $s)), ([System.Drawing.Color]::FromArgb(255, 0x3C, 0x3C, 0x41)), ([System.Drawing.Color]::FromArgb(255, 0x16, 0x16, 0x18))
    $g.FillPath($grad, $bg)
    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), ([Math]::Max(1, $s / 128))
        $g.DrawPath($pen, $bg)
    }

    $bw = $s * 0.15; $gap = $s * 0.07
    $x0 = ($s - (3 * $bw + 2 * $gap)) / 2
    $bottom = $s * 0.75
    $heights = @(0.22, 0.36, 0.52)
    for ($i = 0; $i -lt 3; $i++) {
        $h = $s * $heights[$i]
        $x = $x0 + $i * ($bw + $gap)
        $bar = RoundRect $x ($bottom - $h) $bw $h ($bw * 0.5)
        if ($i -lt 2) {
            $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xF5, 0xF5, 0xF7))
        } else {
            $b = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, $bottom)), ([System.Drawing.PointF]::new(0, $bottom - $h - 1)), ([System.Drawing.Color]::FromArgb(255, 0xFF, 0x9F, 0x0A)), ([System.Drawing.Color]::FromArgb(255, 0xFF, 0x45, 0x3A))
        }
        $g.FillPath($b, $bar)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}
(Render 512).Save((Join-Path $OutDir 'UPsound.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'UPsound.ico'), $out.ToArray())
Write-Host "Icone gerado em $OutDir"

# Icones da bandeja: so as tres barras, sem fundo. Variantes para barra de tarefas escura e clara,
# com o reforco ligado (ultima barra laranja) e desligado (ultima barra apagada).
function Tray([int]$s, [bool]$darkBar, [bool]$on) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $s / 16.0
    $bw = [Math]::Round(3 * $u); $gap = [Math]::Round(2 * $u)
    $x0 = [Math]::Floor(($s - (3 * $bw + 2 * $gap)) / 2)
    $bottom = [Math]::Round(14.5 * $u)
    $hs = @(5, 9, 13)
    $ink = if ($darkBar) { [System.Drawing.Color]::FromArgb(255, 255, 255, 255) } else { [System.Drawing.Color]::FromArgb(255, 0x1D, 0x1D, 0x1F) }
    for ($i = 0; $i -lt 3; $i++) {
        $h = [Math]::Round($hs[$i] * $u); $x = $x0 + $i * ($bw + $gap)
        $bar = RoundRect $x ($bottom - $h) $bw $h ($bw / 2)
        if ($i -eq 2 -and $on) {
            $b = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, $bottom)), ([System.Drawing.PointF]::new(0, $bottom - $h - 1)), ([System.Drawing.Color]::FromArgb(255, 0xFF, 0x9F, 0x0A)), ([System.Drawing.Color]::FromArgb(255, 0xFF, 0x45, 0x3A))
        } elseif ($i -eq 2) {
            $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, $ink.R, $ink.G, $ink.B))
        } else {
            $b = New-Object System.Drawing.SolidBrush $ink
        }
        $g.FillPath($b, $bar)
    }
    $g.Dispose()
    return $bmp
}

function WriteIco($path, $bitmaps, $sizes) {
    $pngs = @()
    foreach ($bmp in $bitmaps) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs += , $ms.ToArray(); $bmp.Dispose() }
    $out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $w.Write([byte]$s); $w.Write([byte]$s); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset); $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Flush(); [System.IO.File]::WriteAllBytes($path, $out.ToArray())
}

$traySizes = 16, 20, 24, 32, 40, 48
foreach ($v in @(@('dark', $true), @('light', $false))) {
    foreach ($state in @(@('on', $true), @('off', $false))) {
        $bmps = @(); foreach ($s in $traySizes) { $bmps += , (Tray $s $v[1] $state[1]) }
        WriteIco (Join-Path $OutDir "tray-$($v[0])-$($state[0]).ico") $bmps $traySizes
    }
}
Write-Host "Icones da bandeja gerados"
