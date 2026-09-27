# Uygulama ikonunu (Assets\app.ico) ve README görselini (docs\icon.png) üretir.
# Kullanım: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$icoPath = Join-Path $root 'src\Optimayzir\Assets\app.ico'
$pngPath = Join-Path $root 'docs\icon.png'
New-Item -ItemType Directory -Force (Split-Path $icoPath), (Split-Path $pngPath) | Out-Null

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Yuvarlatılmış kare, turkuazdan maviye geçişli
    $rect = [System.Drawing.RectangleF]::new(8 * $s, 8 * $s, 240 * $s, 240 * $s)
    $d = 112 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(20, 184, 166)), ([System.Drawing.Color]::FromArgb(59, 130, 246)), 45.0
    $g.FillPath($brush, $path)

    # Şimşek
    $pts = @(@(150, 24), @(64, 144), @(118, 144), @(98, 232), @(192, 104), @(136, 104), @(170, 24)) |
        ForEach-Object { [System.Drawing.PointF]::new($_[0] * $s, $_[1] * $s) }
    $g.FillPolygon([System.Drawing.Brushes]::White, [System.Drawing.PointF[]]$pts)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $brush.Dispose(); $path.Dispose(); $g.Dispose(); $bmp.Dispose()
    return , $ms.ToArray()
}

# En büyük boyut ilk sırada: WPF ikonun ilk karesini kullanır.
$sizes = 256, 64, 48, 32, 24, 16
$pngs = @($sizes | ForEach-Object { , (New-IconPng $_) })

$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $data = $pngs[$i]
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write([byte[]]$data) }
$bw.Close()

[System.IO.File]::WriteAllBytes($pngPath, $pngs[0])
"İkon oluşturuldu: $icoPath"
