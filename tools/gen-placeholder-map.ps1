# Generates the placeholder map package maps/arena01/ (background.png,
# solid.png, destructible.png, map.json). Deterministic (fixed RNG seed),
# safe to re-run. Requires Windows PowerShell (System.Drawing / GDI+).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$W = 1280
$H = 720
$outDir = Join-Path $PSScriptRoot '..\content\Base\maps\arena01'
New-Item -ItemType Directory -Force $outDir | Out-Null
$outDir = (Resolve-Path $outDir).Path
$rand = New-Object System.Random(7)

function New-Layer {
    $bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

# ---------- background.png: dark gradient + faint dots ----------
$bmp, $g = New-Layer
$rect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
$top = [System.Drawing.Color]::FromArgb(255, 24, 26, 38)
$bottom = [System.Drawing.Color]::FromArgb(255, 38, 32, 30)
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
$g.FillRectangle($grad, $rect)
for ($i = 0; $i -lt 220; $i++) {
    $x = $rand.Next($W); $y = $rand.Next($H)
    $a = 20 + $rand.Next(40)
    $dot = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($a, 200, 205, 220))
    $g.FillRectangle($dot, $x, $y, 2, 2)
    $dot.Dispose()
}
$g.Dispose()
$bmp.Save((Join-Path $outDir 'background.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# ---------- solid.png: border frame + a few platforms ----------
$bmp, $g = New-Layer
$solidBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 92, 97, 108))
# 16 px frame
$g.FillRectangle($solidBrush, 0, 0, $W, 16)
$g.FillRectangle($solidBrush, 0, ($H - 16), $W, 16)
$g.FillRectangle($solidBrush, 0, 0, 16, $H)
$g.FillRectangle($solidBrush, ($W - 16), 0, 16, $H)
# floating platforms + a center pillar
$g.FillRectangle($solidBrush, 190, 430, 190, 14)
$g.FillRectangle($solidBrush, 900, 370, 170, 14)
$g.FillRectangle($solidBrush, 610, 470, 60, 234)
$solidBrush.Dispose()
$g.Dispose()
$bmp.Save((Join-Path $outDir 'solid.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# ---------- destructible.png: ground mass, blobs, tunnels, speckles ----------
$bmp, $g = New-Layer
$base = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 133, 94, 60))
# ground mass with a bumpy top edge
$g.FillRectangle($base, 16, 560, ($W - 32), ($H - 560 - 16))
for ($x = 16; $x -lt ($W - 32); $x += 70) {
    $bump = 20 + $rand.Next(60)
    $g.FillEllipse($base, $x, (560 - $bump), 110, ($bump * 2))
}
# hanging blob + floating island
$g.FillEllipse($base, 90, 60, 150, 120)
$g.FillEllipse($base, 780, 220, 240, 70)
# tunnels (erase with SourceCopy transparent fill)
$g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
$hole = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
$g.FillEllipse($hole, 300, 590, 90, 70)
$g.FillEllipse($hole, 340, 620, 200, 60)
$g.FillEllipse($hole, 950, 600, 120, 80)
$hole.Dispose()
$base.Dispose()
$g.Dispose()
# speckles for particle color variety (only on existing pixels)
for ($i = 0; $i -lt 16000; $i++) {
    $x = $rand.Next($W); $y = $rand.Next($H)
    $p = $bmp.GetPixel($x, $y)
    if ($p.A -gt 0) {
        if ($rand.Next(2) -eq 0) { $c = [System.Drawing.Color]::FromArgb(255, 104, 70, 43) }
        else { $c = [System.Drawing.Color]::FromArgb(255, 158, 116, 78) }
        $bmp.SetPixel($x, $y, $c)
        if ($x + 1 -lt $W) { $q = $bmp.GetPixel(($x + 1), $y); if ($q.A -gt 0) { $bmp.SetPixel(($x + 1), $y, $c) } }
    }
}
$bmp.Save((Join-Path $outDir 'destructible.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# ---------- map.json ----------
@'
{
  "name": "Arena 01",
  "suggestedPlayers": 4
}
'@ | Out-File -Encoding ascii (Join-Path $outDir 'map.json')

Write-Host "map package written to $outDir"
