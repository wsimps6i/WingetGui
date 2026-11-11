#requires -version 5
# Generates Assets\AppIcon.ico (multi-size, transparent PNG entries) and icon-preview.png:
# a teal tile carrying a package box with a down arrow (Winget GUI). Adapted from MarkItDown-GUI's script.
# Re-run after editing the design below to refresh the icon.

Add-Type -AssemblyName System.Drawing

function New-IconBitmap {
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded tile with a top-to-bottom blue gradient.
    $r = [single]($size * 0.2)
    $tile = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($size - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($size - $r * 2, $size - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $size - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $top = [System.Drawing.Color]::FromArgb(255, 20, 184, 166)
    $bottom = [System.Drawing.Color]::FromArgb(255, 15, 118, 110)
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $tile, $top, $bottom, 90.0
    $g.FillPath($fill, $path)

    # Package box (front face and lid) with a down arrow dropping into it, in white.
    $s = [single]$size
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single][Math]::Max(1.5, $s * 0.075))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawRectangle($pen, ($s * 0.20), ($s * 0.50), ($s * 0.60), ($s * 0.30))
    $g.DrawLine($pen, ($s * 0.20), ($s * 0.50), ($s * 0.12), ($s * 0.40))
    $g.DrawLine($pen, ($s * 0.80), ($s * 0.50), ($s * 0.88), ($s * 0.40))

    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Flat
    $g.DrawLine($pen, ($s * 0.50), ($s * 0.14), ($s * 0.50), ($s * 0.38))
    $arrow = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($s * 0.36), ($s * 0.34))
        (New-Object System.Drawing.PointF ($s * 0.64), ($s * 0.34))
        (New-Object System.Drawing.PointF ($s * 0.50), ($s * 0.52))
    )
    $g.FillPolygon([System.Drawing.Brushes]::White, $arrow)

    $pen.Dispose()
    $fill.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

function Save-Ico {
    param(
        [string[]]$PngPaths,
        [string]  $OutPath
    )

    $count = $PngPaths.Count
    $imgData = New-Object 'System.Collections.Generic.List[byte[]]'
    foreach ($p in $PngPaths) {
        $imgData.Add([System.IO.File]::ReadAllBytes($p))
    }

    $offset = 6 + 16 * $count
    $offsets = New-Object int[] $count
    for ($i = 0; $i -lt $count; $i++) {
        $offsets[$i] = $offset
        $offset += $imgData[$i].Length
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$count)

    for ($i = 0; $i -lt $count; $i++) {
        $bytes = $imgData[$i]
        $w = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
        $h = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
        $bw.Write([byte]$(if ($w -ge 256) { 0 } else { $w }))
        $bw.Write([byte]$(if ($h -ge 256) { 0 } else { $h }))
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$bytes.Length)
        $bw.Write([uint32]$offsets[$i])
    }

    for ($i = 0; $i -lt $count; $i++) {
        $bw.Write($imgData[$i])
    }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
    $bw.Dispose()
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "WingetGuiIcon"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

$pngPaths = @()
foreach ($s in 16, 20, 24, 32, 40, 48, 64, 256) {
    $bmp = New-IconBitmap -size $s
    $p = Join-Path $tempDir "icon-$s.png"
    $bmp.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngPaths += $p
}

$icoPath = Join-Path $here "AppIcon.ico"
Save-Ico -PngPaths $pngPaths -OutPath $icoPath
Copy-Item (Join-Path $tempDir "icon-256.png") (Join-Path $here "icon-preview.png") -Force
Remove-Item $tempDir -Recurse -Force

Write-Host "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes)"
