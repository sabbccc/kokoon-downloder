# Generates kokoon.ico with multiple resolutions (16, 32, 48, 256) from the
# app's logo source image (Assets\logo-source.png — see logo-source.svg for
# the editable vector) rather than drawing a glyph in code.

Add-Type -AssemblyName System.Drawing

$sourcePath = "F:\AI\KokoonDownloader\Kokoon.UI\Assets\logo-source.png"

function New-KokoonBitmap([int]$size) {
    $src = [System.Drawing.Image]::FromFile($sourcePath)
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $src.Dispose()
    return $bmp
}

function Save-Icon([System.Drawing.Bitmap[]]$bitmaps, [string]$outputPath) {
    $ms = New-Object System.IO.MemoryStream

    # ICO header: reserved(2) + type(2) + count(2)
    $writer = New-Object System.IO.BinaryWriter($ms)
    $writer.Write([uint16]0)                    # Reserved
    $writer.Write([uint16]1)                    # Type: ICO
    $writer.Write([uint16]$bitmaps.Count)       # Image count

    # Collect PNG data for each bitmap
    $pngDataList = @()
    foreach ($bmp in $bitmaps) {
        $pngStream = New-Object System.IO.MemoryStream
        $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngDataList += ,($pngStream.ToArray())
        $pngStream.Dispose()
    }

    # Directory entries offset starts after header (6 bytes) + entries (16 bytes each)
    $dataOffset = 6 + ($bitmaps.Count * 16)

    for ($i = 0; $i -lt $bitmaps.Count; $i++) {
        $bmp = $bitmaps[$i]
        $pngData = $pngDataList[$i]

        $w = if ($bmp.Width -ge 256) { 0 } else { $bmp.Width }
        $h = if ($bmp.Height -ge 256) { 0 } else { $bmp.Height }

        $writer.Write([byte]$w)              # Width
        $writer.Write([byte]$h)              # Height
        $writer.Write([byte]0)               # Color palette
        $writer.Write([byte]0)               # Reserved
        $writer.Write([uint16]1)             # Color planes
        $writer.Write([uint16]32)            # Bits per pixel
        $writer.Write([uint32]$pngData.Length) # Size of image data
        $writer.Write([uint32]$dataOffset)   # Offset to image data

        $dataOffset += $pngData.Length
    }

    # Write image data
    foreach ($pngData in $pngDataList) {
        $writer.Write($pngData)
    }

    # Save to file
    [System.IO.File]::WriteAllBytes($outputPath, $ms.ToArray())

    $writer.Dispose()
    $ms.Dispose()
}

# Generate all sizes
$sizes = @(16, 32, 48, 256)
$bitmaps = @()

foreach ($s in $sizes) {
    $bitmaps += New-KokoonBitmap $s
    Write-Host "  Generated ${s}x${s}"
}

$outPath = "F:\AI\KokoonDownloader\Kokoon.UI\Assets\kokoon.ico"
Save-Icon $bitmaps $outPath
Write-Host "Icon saved to $outPath"

# Also save a 256px PNG for general use
$bitmaps[3].Save("F:\AI\KokoonDownloader\Kokoon.UI\Assets\kokoon-256.png", [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "PNG saved to F:\AI\KokoonDownloader\Kokoon.UI\Assets\kokoon-256.png"

# Cleanup
foreach ($bmp in $bitmaps) { $bmp.Dispose() }
