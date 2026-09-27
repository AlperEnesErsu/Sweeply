# Uygulama ikonunu (src\Sweeply\Assets\app.ico) ve README görselini (docs\icon.png) üretir.
# Kullanım: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$icoPath = Join-Path $root 'src\Sweeply\Assets\app.ico'
$pngPath = Join-Path $root 'docs\icon.png'
New-Item -ItemType Directory -Force (Split-Path $icoPath), (Split-Path $pngPath) | Out-Null

function P([double]$x, [double]$y) { [System.Drawing.PointF]::new([float]$x, [float]$y) }

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform([float]($size / 256.0), [float]($size / 256.0))

    # Zemin: düz kömür rengi yuvarlatılmış kare, ince açık kenar (koyu görev çubuğunda da seçilsin diye)
    $rect = [System.Drawing.RectangleF]::new(10, 10, 236, 236)
    $d = 100
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $bg.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $bg.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $bg.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $bg.CloseFigure()
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(28, 28, 31))
    $g.FillPath($fill, $bg)
    $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 70, 78)), 5
    $g.DrawPath($edge, $bg)

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    # Süpürge sapı: sağ üstten merkeze
    $hx1 = 196; $hy1 = 46; $hx2 = 126; $hy2 = 126
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 18
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, $hx1, $hy1, $hx2, $hy2)

    # Süpürge başı: sapa dik, aşağı doğru genişleyen yamuk
    $len = [Math]::Sqrt(($hx2 - $hx1) * ($hx2 - $hx1) + ($hy2 - $hy1) * ($hy2 - $hy1))
    $dx = ($hx2 - $hx1) / $len; $dy = ($hy2 - $hy1) / $len   # sap yönü
    $px = -$dy; $py = $dx                                      # dik yön
    $tx = $hx2 + $dx * 4; $ty = $hy2 + $dy * 4                 # üst kenar merkezi
    $bx = $hx2 + $dx * 74; $by = $hy2 + $dy * 74               # alt kenar merkezi
    $head = @((P ($tx - $px * 30) ($ty - $py * 30)), (P ($tx + $px * 30) ($ty + $py * 30)),
              (P ($bx + $px * 54) ($by + $py * 54)), (P ($bx - $px * 54) ($by - $py * 54)))
    $headPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $headPath.AddPolygon([System.Drawing.PointF[]]$head)
    $g.FillPath($white, $headPath)

    # Kıl çizgileri: zemin renginde, başın alt yarısında
    $cut = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(28, 28, 31)), 7
    $cut.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    foreach ($k in -0.5, 0, 0.5) {
        $sx = $tx + $dx * 34 + $px * 30 * $k * 1.6; $sy = $ty + $dy * 34 + $py * 30 * $k * 1.6
        $ex = $bx + $px * 54 * $k; $ey = $by + $py * 54 * $k
        $g.DrawLine($cut, [float]$sx, [float]$sy, [float]$ex, [float]$ey)
    }

    $ms = New-Object System.IO.MemoryStream
    $g.Dispose()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
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
