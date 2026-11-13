$ErrorActionPreference = "Continue"

Write-Host "=========================================================="
Write-Host " [AUDIT] OMNIWHEEL EXHAUSTIVE CONVERTER AUDIT & TEST MATRIX"
Write-Host "=========================================================="

Add-Type -AssemblyName System.Drawing
$omniBin = Join-Path $PSScriptRoot "..\bin\OMNIWHEEL.exe"
if (-not (Test-Path $omniBin)) { $omniBin = "$env:LOCALAPPDATA\OMNIWHEEL\OMNIWHEEL.exe" }
if (-not (Test-Path $omniBin)) { $omniBin = "E:\ANTIGRAVITY\OMNIWHEEL\OMNIWHEEL.exe" }

$testDir = "E:\_audit_conversions"
if (Test-Path $testDir) { Remove-Item $testDir -Recurse -Force }
New-Item -ItemType Directory -Path $testDir | Out-Null

$fixturesDir = Join-Path $PSScriptRoot "..\test_fixtures"

$script:passCount = 0
$script:failCount = 0
$script:totalTests = 0

function Assert-Conversion {
    param(
        [string]$Category,
        [string]$InputPath,
        [string]$TargetFormat,
        [bool]$ExpectSuccess = $true,
        [string]$MagicBytesHex = ""
    )

    $script:totalTests++
    $fileName = [System.IO.Path]::GetFileName($InputPath)
    
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $omniBin
        $psi.Arguments = "--headless --input `"$InputPath`" --action `"$TargetFormat`""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true

        $proc = [System.Diagnostics.Process]::Start($psi)
        $stdout = $proc.StandardOutput.ReadToEnd()
        $stderr = $proc.StandardError.ReadToEnd()
        [void]$proc.WaitForExit(35000)

        $json = $null
        foreach ($line in ($stdout -split "`r?`n")) {
            $t = $line.Trim()
            if ($t.StartsWith("{") -and $t -match '"success"') {
                try { $json = $t | ConvertFrom-Json; break } catch { }
            }
        }

        $resSuccess = ($proc.ExitCode -eq 0 -and $json -ne $null -and $json.success -eq $true)
        $success = ($resSuccess -eq $ExpectSuccess)
        $outPath = if ($json -and $json.outputPath) { $json.outputPath } else { $null }
        $outExists = if ($outPath) { Test-Path $outPath } else { $false }
        $size = if ($outExists) { (Get-Item $outPath).Length } else { 0 }

        if ($ExpectSuccess -and -not $resSuccess) {
            $success = $false
        }

        # Verify magic bytes if specified
        if ($success -and $resSuccess -and $MagicBytesHex -ne "" -and $outExists) {
            $bytes = [System.IO.File]::ReadAllBytes($outPath)
            $expected = [byte[]]($MagicBytesHex.Split(' ') | ForEach-Object { [Convert]::ToByte($_, 16) })
            for ($i = 0; $i -lt $expected.Length; $i++) {
                if ($i -ge $bytes.Length -or $bytes[$i] -ne $expected[$i]) {
                    $success = $false
                    break
                }
            }
        }

        if ($success) {
            $script:passCount++
            Write-Host " [PASS] [$Category] $fileName -> $TargetFormat (Size: $size bytes)" -ForegroundColor Green
        } else {
            $script:failCount++
            $err = if ($json -and $json.message) { $json.message } elseif ($stderr) { $stderr.Trim() } else { "Exit $($proc.ExitCode)" }
            Write-Host " [FAIL] [$Category] $fileName -> $TargetFormat. Error: $err" -ForegroundColor Red
        }
    } catch {
        if (-not $ExpectSuccess) {
            $script:passCount++
            Write-Host " [PASS] [$Category] $fileName -> $TargetFormat (Expected exception caught)" -ForegroundColor Green
        } else {
            $script:failCount++
            Write-Host " [FAIL] [$Category] $fileName -> $TargetFormat. Exception: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

# --- PREPARE TEST FIXTURES ---
# 1. Raster Image
$bmp = New-Object System.Drawing.Bitmap(120, 120)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Orange)
$g.Dispose()
$pngFile = Join-Path $testDir "test_img.png"
$bmp.Save($pngFile, [System.Drawing.Imaging.ImageFormat]::Png)
$jpgFile = Join-Path $testDir "test_img.jpg"
$bmp.Save($jpgFile, [System.Drawing.Imaging.ImageFormat]::Jpeg)
$bmpFile = Join-Path $testDir "test_img.bmp"
$bmp.Save($bmpFile, [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

# 1B. OCR Test Image with clear rendered text
$ocrBmp = New-Object System.Drawing.Bitmap(400, 150)
$gOcr = [System.Drawing.Graphics]::FromImage($ocrBmp)
$gOcr.Clear([System.Drawing.Color]::White)
$font = New-Object System.Drawing.Font("Arial", 22, [System.Drawing.FontStyle]::Bold)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Black)
$gOcr.DrawString("OMNIWHEEL OCR TEST", $font, $brush, 20, 50)
$font.Dispose()
$brush.Dispose()
$gOcr.Dispose()
$ocrJpg = Join-Path $testDir "ocr_test_img.jpg"
$ocrBmp.Save($ocrJpg, [System.Drawing.Imaging.ImageFormat]::Jpeg)
$ocrBmp.Dispose()

# 2. Unicode and Special Char File
$unicodeTxt = Join-Path $testDir "unicode_test_sample.txt"
[System.IO.File]::WriteAllText($unicodeTxt, "Multi-language UTF-8: Welcome, Bonjour, Konnichiwa!", [System.Text.Encoding]::UTF8)

# 3. Documents
$txtFile = Join-Path $testDir "doc.txt"
[System.IO.File]::WriteAllText($txtFile, "Comprehensive document content for testing native conversions.")
$mdFile = Join-Path $testDir "doc.md"
[System.IO.File]::WriteAllText($mdFile, "# Document Title`r`n`r`nSome formatted text with **bold**.")
$htmlFile = Join-Path $testDir "doc.html"
[System.IO.File]::WriteAllText($htmlFile, "<!DOCTYPE html><html><body><h1>Header</h1><p>Test</p></body></html>")
$pdfFile = Join-Path $testDir "doc.pdf"
& $omniBin --headless --input $txtFile --action pdf --output $pdfFile | Out-Null
$docxFile = Join-Path $testDir "doc.docx"
& $omniBin --headless --input $txtFile --action docx --output $docxFile | Out-Null

# 4. Data
$csvFile = Join-Path $testDir "data.csv"
[System.IO.File]::WriteAllText($csvFile, "name,score,grade`r`nAlice,95,A`r`nBob,82,B")
$tsvFile = Join-Path $testDir "data.tsv"
[System.IO.File]::WriteAllText($tsvFile, "name`tscore`tgrade`r`nAlice`t95`tA`r`nBob`t82`tB")
$jsonFile = Join-Path $testDir "data.json"
[System.IO.File]::WriteAllText($jsonFile, "[{`"id`":1,`"val`":`"alpha`"},{`"id`":2,`"val`":`"beta`"}]")
$jsonlFile = Join-Path $testDir "data.jsonl"
[System.IO.File]::WriteAllText($jsonlFile, "{`"id`":1,`"val`":`"alpha`"}`r`n{`"id`":2,`"val`":`"beta`"}")

# 5. Subtitles
$srtFile = Join-Path $testDir "subs.srt"
[System.IO.File]::WriteAllText($srtFile, "1`r`n00:00:01,000 --> 00:00:03,500`r`nHello World`r`n`r`n2`r`n00:00:04,000 --> 00:00:06,000`r`nSubtitle test")
$vttFile = Join-Path $testDir "subs.vtt"
[System.IO.File]::WriteAllText($vttFile, "WEBVTT`r`n`r`n00:00:01.000 --> 00:00:03.500`r`nHello World`r`n`r`n00:00:04.000 --> 00:00:06.000`r`nSubtitle test")

# 6. Edge Case Fixtures
$zeroFile = Join-Path $testDir "zero_length.txt"
[System.IO.File]::WriteAllText($zeroFile, "")
$corruptImg = Join-Path $testDir "corrupt.jpg"
[System.IO.File]::WriteAllBytes($corruptImg, [byte[]]@(0xFF, 0xD8, 0x00, 0x00))

# --- RUNNING TESTS ---
Write-Host "`n>>> 1. RASTER & VECTOR IMAGE CONVERSIONS <<<" -ForegroundColor Cyan
Assert-Conversion "Raster" $pngFile "jpeg" $true "FF D8"
Assert-Conversion "Raster" $pngFile "jpg" $true "FF D8"
Assert-Conversion "Raster" $pngFile "webp" $true "52 49 46 46"
Assert-Conversion "Raster" $pngFile "bmp" $true "42 4D"
Assert-Conversion "Raster" $pngFile "ico" $true "00 00 01 00"
Assert-Conversion "Raster" $pngFile "tiff" $true
Assert-Conversion "Raster" $pngFile "pdf" $true "25 50 44 46"

Assert-Conversion "Raster" $jpgFile "png" $true "89 50 4E 47"
Assert-Conversion "Raster" $jpgFile "webp" $true "52 49 46 46"
Assert-Conversion "Raster" $jpgFile "bmp" $true "42 4D"
Assert-Conversion "Raster" $jpgFile "ico" $true "00 00 01 00"
Assert-Conversion "Raster" $jpgFile "tiff" $true
Assert-Conversion "Raster" $jpgFile "pdf" $true "25 50 44 46"

Assert-Conversion "Vector" $jpgFile "trace-svg" $true
Assert-Conversion "Vector" $pngFile "lineart-svg" $true

Write-Host "`n>>> 2. IMAGE EDITING & SUB-ARC ACTIONS <<<" -ForegroundColor Cyan
Assert-Conversion "Edit" $jpgFile "half-size" $true
Assert-Conversion "Edit" $jpgFile "crop-auto" $true
Assert-Conversion "Edit" $jpgFile "crop-1-1" $true
Assert-Conversion "Edit" $jpgFile "crop-16-9" $true
Assert-Conversion "Edit" $jpgFile "rotate-90-cw" $true
Assert-Conversion "Edit" $jpgFile "rotate-90-ccw" $true
Assert-Conversion "Edit" $jpgFile "rotate-180" $true
Assert-Conversion "Edit" $jpgFile "flip-h" $true
Assert-Conversion "Edit" $jpgFile "flip-v" $true
Assert-Conversion "Edit" $jpgFile "remove-bg" $true
Assert-Conversion "Edit" $jpgFile "grayscale" $true
Assert-Conversion "Edit" $jpgFile "gif" $true "47 49 46 38"
Assert-Conversion "Edit" $jpgFile "avif" $true

Write-Host "`n>>> 3. DOCUMENT CONVERSIONS <<<" -ForegroundColor Cyan
Assert-Conversion "Doc" $docxFile "pdf" $true "25 50 44 46"
Assert-Conversion "Doc" $docxFile "txt" $true
Assert-Conversion "Doc" $docxFile "html" $true
Assert-Conversion "Doc" $docxFile "md" $true
Assert-Conversion "Doc" $docxFile "doc" $true

Assert-Conversion "Doc" $pdfFile "docx" $true
Assert-Conversion "Doc" $pdfFile "doc" $true
Assert-Conversion "Doc" $pdfFile "txt" $true
Assert-Conversion "Doc" $pdfFile "html" $true
Assert-Conversion "Doc" $pdfFile "md" $true

Assert-Conversion "Doc" $txtFile "pdf" $true "25 50 44 46"
Assert-Conversion "Doc" $txtFile "docx" $true
Assert-Conversion "Doc" $txtFile "doc" $true
Assert-Conversion "Doc" $txtFile "html" $true
Assert-Conversion "Doc" $txtFile "md" $true

Assert-Conversion "Doc" $mdFile "pdf" $true "25 50 44 46"
Assert-Conversion "Doc" $mdFile "docx" $true
Assert-Conversion "Doc" $mdFile "doc" $true
Assert-Conversion "Doc" $mdFile "html" $true
Assert-Conversion "Doc" $mdFile "txt" $true

Assert-Conversion "Doc" $htmlFile "pdf" $true "25 50 44 46"
Assert-Conversion "Doc" $htmlFile "docx" $true
Assert-Conversion "Doc" $htmlFile "doc" $true
Assert-Conversion "Doc" $htmlFile "txt" $true
Assert-Conversion "Doc" $htmlFile "md" $true

Write-Host "`n>>> 3B. NATIVE WINDOWS OCR (IMAGE & PDF) <<<" -ForegroundColor Cyan
Assert-Conversion "OCR" $ocrJpg "ocr-txt" $true
Assert-Conversion "OCR" $ocrJpg "ocr-docx" $true
Assert-Conversion "OCR" $pdfFile "ocr-txt" $true
Assert-Conversion "OCR" $pdfFile "ocr-docx" $true

Write-Host "`n>>> 4. DATA & SUBTITLES <<<" -ForegroundColor Cyan
Assert-Conversion "Data" $csvFile "tsv" $true
Assert-Conversion "Data" $tsvFile "csv" $true
Assert-Conversion "Data" $jsonFile "jsonl" $true
Assert-Conversion "Data" $jsonlFile "json" $true

Assert-Conversion "Subs" $srtFile "vtt" $true
Assert-Conversion "Subs" $srtFile "txt" $true
Assert-Conversion "Subs" $vttFile "srt" $true

Write-Host "`n>>> 5. ARCHIVES & BUNDLES <<<" -ForegroundColor Cyan
Assert-Conversion "Archive" $txtFile "zip" $true "50 4B 03 04"
Assert-Conversion "Archive" $pngFile "zip" $true "50 4B 03 04"
Assert-Conversion "Archive" $jpgFile "merge-pdf" $true "25 50 44 46"

Write-Host "`n>>> 6. UNICODE & EDGE CASES <<<" -ForegroundColor Cyan
Assert-Conversion "Unicode" $unicodeTxt "pdf" $true "25 50 44 46"
Assert-Conversion "Unicode" $unicodeTxt "docx" $true
Assert-Conversion "Unicode" $unicodeTxt "html" $true
Assert-Conversion "Edge" $zeroFile "pdf" $true "25 50 44 46"
Assert-Conversion "Edge" $zeroFile "txt" $true
Assert-Conversion "Edge" "E:\non_existent_file_12345.xyz" "pdf" $false
Assert-Conversion "Edge" $txtFile "unsupported_xyz_format" $false

# Cleanup
Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n=========================================================="
Write-Host " RESULTS: $script:passCount passed, $script:failCount failed out of $script:totalTests total tests." -ForegroundColor $(if ($script:failCount -eq 0) { "Green" } else { "Red" })
Write-Host "=========================================================="

if ($script:failCount -gt 0) {
    exit 1
} else {
    exit 0
}
