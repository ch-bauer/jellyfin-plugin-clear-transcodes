# Renders images/icon.png — the plugin icon shown in the Jellyfin catalogue.
# Run from the repo root:  powershell -NoProfile -File images/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$size = 512
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Background: Jellyfin's blue-to-purple gradient on a rounded square.
$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point($size, $size)),
    [System.Drawing.Color]::FromArgb(0, 164, 220),
    [System.Drawing.Color]::FromArgb(170, 92, 195))
$bg = New-RoundedPath 0 0 $size $size 104
$g.FillPath($bgBrush, $bg)

$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)

# Lid handle and lid bar.
$g.FillPath($white, (New-RoundedPath 214 100 84 40 14))
$g.FillPath($white, (New-RoundedPath 100 148 312 46 20))

# Bin body: tapered, rounded at the bottom.
$body = New-Object System.Drawing.Drawing2D.GraphicsPath
$body.AddLine(134, 214, 378, 214)
$body.AddLine(378, 214, 352, 404)
$body.AddArc(316, 384, 36, 36, 0, 90)
$body.AddLine(316, 420, 196, 420)
$body.AddArc(160, 384, 36, 36, 90, 90)
$body.CloseFigure()
$g.FillPath($white, $body)

# Film strip cut out of the body: the background shows through, so the bin
# reads as "full of transcode segments".
$strip = New-RoundedPath 152 252 208 104 10
$g.SetClip($body)
$g.FillPath($bgBrush, $strip)
$g.ResetClip()

# Perforations along both edges of the strip.
for ($i = 0; $i -lt 4; $i++) {
    $x = 168 + ($i * 48)
    $g.FillPath($white, (New-RoundedPath $x 264 30 20 6))
    $g.FillPath($white, (New-RoundedPath $x 324 30 20 6))
}

$out = Join-Path $PSScriptRoot 'icon.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Output "wrote $out"
