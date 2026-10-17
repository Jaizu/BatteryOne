# Regenerates the Stream Deck plugin images in streamdeck\com.batteryone.wheel.sdPlugin\images.
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\streamdeck\com.batteryone.wheel.sdPlugin\images'
New-Item -ItemType Directory -Force $out | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90)
    $p.AddArc($x + $w - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
    $p.AddArc($x + $w - 2 * $r, $y + $h - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $p.AddArc($x, $y + $h - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $p.CloseFigure()
    $p
}

function Save-Image([int]$s, [string]$name, [scriptblock]$draw) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'
    & $draw $g ($s / 256.0) $s
    $g.Dispose()
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Battery outline in a 256-unit box; $color fills the charge level
function Draw-Battery($g, $k, [System.Drawing.Color]$stroke, $fill) {
    $pen = New-Object System.Drawing.Pen $stroke, ([Math]::Max(1.5, 16 * $k))
    $g.DrawPath($pen, (New-RoundedRect (36 * $k) (76 * $k) (164 * $k) (104 * $k) (20 * $k)))
    $g.FillPath((New-Object System.Drawing.SolidBrush $stroke), (New-RoundedRect (204 * $k) (106 * $k) (20 * $k) (44 * $k) (6 * $k)))
    if ($fill) { $g.FillPath((New-Object System.Drawing.SolidBrush $fill), (New-RoundedRect (56 * $k) (96 * $k) (96 * $k) (64 * $k) (8 * $k))) }
}

# Plugin icon (Stream Deck store / plugin list): same as the app icon
$appIcon = {
    param($g, $k, $s)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x00, 0x67, 0xC0))), (New-RoundedRect 0 0 $s $s (56 * $k)))
    Draw-Battery $g $k ([System.Drawing.Color]::White) ([System.Drawing.Color]::FromArgb(0x6C, 0xCB, 0x5F))
}
Save-Image 256 'plugin.png' $appIcon
Save-Image 512 'plugin@2x.png' $appIcon

# Category and action list icons: white glyph on transparent, as Elgato recommends
$glyph = { param($g, $k, $s) Draw-Battery $g $k ([System.Drawing.Color]::White) ([System.Drawing.Color]::White) }
Save-Image 28 'category.png' $glyph
Save-Image 56 'category@2x.png' $glyph
Save-Image 20 'action.png' $glyph
Save-Image 40 'action@2x.png' $glyph

# Default key image before the first reading: the "…" state of the tray icon
$key = {
    param($g, $k, $s)
    $g.Clear([System.Drawing.Color]::FromArgb(0x6E, 0x6E, 0x6E))
    $font = New-Object System.Drawing.Font 'Segoe UI', ($s * 18 / 32), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString([string][char]0x2026, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, $s, $s), $fmt)
}
Save-Image 72 'key.png' $key
Save-Image 144 'key@2x.png' $key

Get-ChildItem $out | ForEach-Object { "Wrote $($_.Name)" }
