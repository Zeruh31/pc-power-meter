param([Parameter(Mandatory)] [string] $OutIco, [string] $OutPng)
# Draws the app icon (amber rounded square with a dark lightning bolt) and writes a
# multi-size .ico: 32-bit DIB frames for 16-48 px, a PNG frame for 256 px.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-Frame([int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [double]$size
    $pad = [math]::Max(0.5, $s * 0.04)
    $r = $s * 0.22
    $rect = [System.Drawing.RectangleF]::new($pad, $pad, $s - 2 * $pad, $s - 2 * $pad)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect,
        [System.Drawing.Color]::FromArgb(255, 0xF7, 0xC2, 0x4A), [System.Drawing.Color]::FromArgb(255, 0xE0, 0x84, 0x1A), 90.0)
    $g.FillPath($brush, $path)
    $pts = @(@(0.60, 0.09), @(0.25, 0.57), @(0.47, 0.57), @(0.39, 0.92), @(0.77, 0.41), @(0.54, 0.41), @(0.65, 0.09)) |
        ForEach-Object { [System.Drawing.PointF]::new([float]($_[0] * $s), [float]($_[1] * $s)) }
    $g.FillPolygon([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 0x1B, 0x1D, 0x21)), [System.Drawing.PointF[]]$pts)
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = [IO.MemoryStream]::new()
    $bw = [IO.BinaryWriter]::new($ms)
    # BITMAPINFOHEADER; height is doubled for the XOR + AND masks.
    $bw.Write([int]40); $bw.Write([int]$n); $bw.Write([int]($n * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([math]::Ceiling($n / 32.0) * 4)
    for ($y = 0; $y -lt $n; $y++) { $bw.Write([byte[]]::new($maskRow)) }   # alpha channel decides; mask all 0
    $bw.Flush()
    return $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 256
$images = foreach ($size in $sizes) {
    $bmp = New-Frame $size
    if ($size -eq 256) {
        $ms = [IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        if ($OutPng) { $bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png) }
        , $ms.ToArray()
    } else {
        , (Get-DibBytes $bmp)
    }
    $bmp.Dispose()
}

$out = [IO.MemoryStream]::new()
$w = [IO.BinaryWriter]::new($out)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write([byte[]]$img) }
$w.Flush()
[IO.File]::WriteAllBytes($OutIco, $out.ToArray())
"Wrote $OutIco ($($out.Length) bytes)"
