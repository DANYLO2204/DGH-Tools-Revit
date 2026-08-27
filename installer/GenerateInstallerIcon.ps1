Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "DGH_Tools.ico"

$size = 64
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$bg = [System.Drawing.Color]::FromArgb(24,55,91)
$accent = [System.Drawing.Color]::FromArgb(31,144,255)
$white = [System.Drawing.Color]::White

$bgBrush = New-Object System.Drawing.SolidBrush($bg)
$accentBrush = New-Object System.Drawing.SolidBrush($accent)
$whitePen = New-Object System.Drawing.Pen($white, 3.0)
$accentPen = New-Object System.Drawing.Pen($accent, 3.0)

$g.FillEllipse($bgBrush, 2, 2, 60, 60)

$xs = @(19, 32, 45)
foreach ($x in $xs) {
    $g.DrawLine($whitePen, $x, 20, $x, 45)
    $g.DrawEllipse($accentPen, $x-4, 12, 8, 8)
    $g.FillEllipse($accentBrush, $x-2.5, 42.5, 5, 5)
}
$g.DrawLine($accentPen, 13, 45, 51, 45)

$hIcon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$stream = [System.IO.File]::Open($out, [System.IO.FileMode]::Create)
try {
    $icon.Save($stream)
}
finally {
    $stream.Dispose()
    $icon.Dispose()
    $whitePen.Dispose()
    $accentPen.Dispose()
    $bgBrush.Dispose()
    $accentBrush.Dispose()
    $g.Dispose()
    $bmp.Dispose()
}

Write-Host "Generated installer icon: $out"
