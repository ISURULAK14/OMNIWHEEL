# Generate Test Fixtures for LocalConvert Pro
$ErrorActionPreference = "Stop"
$fixturesDir = "E:\ANTIGRAVITY\Native\test_fixtures"
if (-not (Test-Path $fixturesDir)) {
    New-Item -ItemType Directory -Path $fixturesDir | Out-Null
}

Add-Type -AssemblyName System.Drawing

# 1. standard.jpg (640x480 RGB with gradient and geometric shapes)
$stdPath = Join-Path $fixturesDir "standard.jpg"
$bmpStd = New-Object System.Drawing.Bitmap 640, 480
$gStd = [System.Drawing.Graphics]::FromImage($bmpStd)
$p1 = New-Object System.Drawing.Point 0, 0
$p2 = New-Object System.Drawing.Point 640, 480
$c1 = [System.Drawing.Color]::DeepSkyBlue
$c2 = [System.Drawing.Color]::DarkSlateBlue
$brushStd = New-Object System.Drawing.Drawing2D.LinearGradientBrush $p1, $p2, $c1, $c2
$gStd.FillRectangle($brushStd, 0, 0, 640, 480)
$gStd.FillEllipse([System.Drawing.Brushes]::OrangeRed, 120, 100, 240, 240)
$gStd.DrawString("LocalConvert Test", (New-Object System.Drawing.Font "Arial", 28), [System.Drawing.Brushes]::White, 160, 200)
$gStd.Dispose()
$bmpStd.Save($stdPath, [System.Drawing.Imaging.ImageFormat]::Jpeg)
$bmpStd.Dispose()
Write-Host "Created standard.jpg"

# 2. exif_rotated.jpg (EXIF orientation tag 0x0112 = 6, 90 deg cw)
$exifPath = Join-Path $fixturesDir "exif_rotated.jpg"
$bmpExif = New-Object System.Drawing.Bitmap 300, 200
$gExif = [System.Drawing.Graphics]::FromImage($bmpExif)
$gExif.Clear([System.Drawing.Color]::Gold)
$gExif.DrawString("EXIF Orientation 6", (New-Object System.Drawing.Font "Arial", 16), [System.Drawing.Brushes]::Black, 20, 80)
$gExif.Dispose()

# Clone property item to set EXIF orientation = 6 (90 CW)
$dummyBmp = New-Object System.Drawing.Bitmap $stdPath
$propItem = $dummyBmp.PropertyItems[0]
$dummyBmp.Dispose()
$propItem.Id = 0x0112
$propItem.Type = 3 # SHORT
$propItem.Len = 2
$propItem.Value = [byte[]]@(6, 0)
$bmpExif.SetPropertyItem($propItem)
$bmpExif.Save($exifPath, [System.Drawing.Imaging.ImageFormat]::Jpeg)
$bmpExif.Dispose()
Write-Host "Created exif_rotated.jpg"

# 3. transparent.png (320x320 transparent background with central circle)
$pngPath = Join-Path $fixturesDir "transparent.png"
$bmpPng = New-Object System.Drawing.Bitmap 320, 320, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gPng = [System.Drawing.Graphics]::FromImage($bmpPng)
$gPng.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0)) # Alpha 0
$gPng.FillEllipse([System.Drawing.Brushes]::Purple, 60, 60, 200, 200)
$gPng.Dispose()
$bmpPng.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmpPng.Dispose()
Write-Host "Created transparent.png"

# 4. sample.pdf (Standard PDF)
$pdfPath = Join-Path $fixturesDir "sample.pdf"
$pdfContent = "%PDF-1.4`n1 0 obj`n<< /Type /Catalog /Pages 2 0 R >>`nendobj`n2 0 obj`n<< /Type /Pages /Kids [3 0 R] /Count 1 >>`nendobj`n3 0 obj`n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 300] /Contents 4 0 R >>`nendobj`n4 0 obj`n<< /Length 44 >>`nstream`nBT /F1 18 Tf 50 200 Td (Sample Test PDF) Tj ET`nendstream`nendobj`nxref`n0 5`n0000000000 65535 f `n0000000009 00000 n `n0000000058 00000 n `n0000000115 00000 n `n0000000206 00000 n `ntrailer`n<< /Size 5 /Root 1 0 R >>`nstartxref`n301`n%%EOF`n"
[System.IO.File]::WriteAllText($pdfPath, $pdfContent, [System.Text.Encoding]::ASCII)
Write-Host "Created sample.pdf"

# 5. motion_photo.jpg (JPEG with appended ftyp box MP4 stream)
$mpPath = Join-Path $fixturesDir "motion_photo.jpg"
$jpgBytes = [System.IO.File]::ReadAllBytes($stdPath)
# Generate a small 1-sec mp4 via ffmpeg
$tempMp4 = Join-Path $env:TEMP "temp_motion.mp4"
$ffmpeg = "E:\ANTIGRAVITY\Native\bin\ffmpeg.exe"
$proc = Start-Process -FilePath $ffmpeg -ArgumentList "-y", "-f", "lavfi", "-i", "color=c=red:s=160x120:d=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "`"$tempMp4`"" -NoNewWindow -Wait -PassThru
$mp4Bytes = [System.IO.File]::ReadAllBytes($tempMp4)
Remove-Item $tempMp4 -Force -ErrorAction SilentlyContinue

# Combine: JPEG + MP4 stream
$combined = New-Object byte[] ($jpgBytes.Length + $mp4Bytes.Length)
[System.Buffer]::BlockCopy($jpgBytes, 0, $combined, 0, $jpgBytes.Length)
[System.Buffer]::BlockCopy($mp4Bytes, 0, $combined, $jpgBytes.Length, $mp4Bytes.Length)
[System.IO.File]::WriteAllBytes($mpPath, $combined)
Write-Host "Created motion_photo.jpg (with embedded MP4 stream)"

# 6. trip (copy) 2026.jpg (Filename with spaces, parentheses)
$tripPath = Join-Path $fixturesDir "trip (copy) 2026.jpg"
Copy-Item $stdPath $tripPath -Force
Write-Host "Created trip (copy) 2026.jpg"

# 7. zero_byte.txt (0-byte file)
$zeroPath = Join-Path $fixturesDir "zero_byte.txt"
[System.IO.File]::WriteAllBytes($zeroPath, (New-Object byte[] 0))
Write-Host "Created zero_byte.txt"

Write-Host "All 7 test fixtures generated in $fixturesDir"
