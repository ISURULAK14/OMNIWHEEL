# Master Comprehensive Converter Test Suite for OMNIWHEEL
$ErrorActionPreference = "Continue"

$nativeDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$exe = Join-Path $nativeDir "bin\OMNIWHEEL.exe"
if (-not (Test-Path $exe)) {
    $exe = "$env:LOCALAPPDATA\OMNIWHEEL\OMNIWHEEL.exe"
}
$fixturesDir = Join-Path $nativeDir "test_fixtures"
$outDir = Join-Path $PSScriptRoot "output"

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

$passCount = 0
$failCount = 0
$totalTests = 0

function Test-Converter {
    param(
        [string]$Category,
        [string]$TestName,
        [string]$InputFile,
        [string]$Action,
        [string]$ExpectedExt,
        [string]$MagicBytesHex = ""
    )

    $global:totalTests++
    $safeName = $TestName -replace '[^a-zA-Z0-9_-]', '_'
    $outPath = Join-Path $outDir "$safeName.$ExpectedExt"
    if (Test-Path $outPath) { Remove-Item $outPath -Force -ErrorAction SilentlyContinue }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = "--headless --input `"$InputFile`" --action `"$Action`" --output `"$outPath`""
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    $p.WaitForExit(50000)
    $exitCode = $p.ExitCode

    $isExtract = ($Action -eq "extract")
    if ($isExtract) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($InputFile)
        $extractDir = Join-Path $fixturesDir "${base}_extracted"
        $success = ($exitCode -eq 0) -and (Test-Path $extractDir)
    } else {
        $success = ($exitCode -eq 0) -and (Test-Path $outPath) -and ((Get-Item $outPath).Length -gt 0)
    }

    if ($success -and $MagicBytesHex -ne "") {
        $bytes = [System.IO.File]::ReadAllBytes($outPath)
        $expectedBytes = [byte[]]($MagicBytesHex.Split(' ') | ForEach-Object { [Convert]::ToByte($_, 16) })
        for ($i = 0; $i -lt $expectedBytes.Length; $i++) {
            if ($i -ge $bytes.Length -or $bytes[$i] -ne $expectedBytes[$i]) {
                $success = $false
                $stderr += " Magic bytes mismatch at index $i."
                break
            }
        }
    }

    if ($success) {
        $global:passCount++
        $size = if (Test-Path $outPath) { (Get-Item $outPath).Length } else { 0 }
        Write-Host " [PASS] [$Category] $TestName -> Size: $size bytes" -ForegroundColor Green
    } else {
        $global:failCount++
        Write-Host " [FAIL] [$Category] $TestName -> ExitCode $exitCode. Error: $stderr Output: $stdout" -ForegroundColor Red
    }
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " OMNIWHEEL COMPLETE CONVERTER VERIFICATION SUITE" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$stdJpg = Join-Path $fixturesDir "standard.jpg"
$transPng = Join-Path $fixturesDir "transparent.png"
$motionJpg = Join-Path $fixturesDir "motion_photo.jpg"
$samplePdf = Join-Path $fixturesDir "sample.pdf"
$testTxt = Join-Path $fixturesDir "test_txt.txt"
$testMd = Join-Path $fixturesDir "test_md.md"
$testHtml = Join-Path $fixturesDir "test_html.html"
$testCsv = Join-Path $fixturesDir "test_csv.csv"
$testJson = Join-Path $fixturesDir "test_json.json"
$testSrt = Join-Path $fixturesDir "test_srt.srt"
$testWav = Join-Path $fixturesDir "test_wav.wav"
$testZip = Join-Path $fixturesDir "test_zip.zip"

# --- 1. Images (Primary Formats) ---
Test-Converter "Image" "Convert JPG to PNG" $stdJpg "png" "png" "89 50 4E 47"
Test-Converter "Image" "Convert JPG to WEBP" $stdJpg "webp" "webp" "52 49 46 46"
Test-Converter "Image" "Convert JPG to PDF" $stdJpg "pdf" "pdf" "25 50 44 46"
Test-Converter "Image" "Convert JPG to TIFF" $stdJpg "tiff" "tiff"
Test-Converter "Image" "Convert JPG to BMP" $stdJpg "bmp" "bmp" "42 4D"
Test-Converter "Image" "Convert JPG to ICO" $stdJpg "ico" "ico" "00 00 01 00"
Test-Converter "Image" "Convert JPG to AVIF" $stdJpg "avif" "avif"
Test-Converter "Image" "Convert JPG to GIF" $stdJpg "gif" "gif" "47 49 46 38"
Test-Converter "Image" "Trace JPG to SVG" $stdJpg "trace-svg" "svg"
Test-Converter "Image" "Line art to SVG" $stdJpg "lineart-svg" "svg"
Test-Converter "Image" "Half size JPG" $stdJpg "half-size" "jpg"

# --- 2. Image Tools & Manipulations ---
Test-Converter "Tools" "Auto-Crop Border" $stdJpg "crop-auto" "jpg"
Test-Converter "Tools" "Crop Square 1:1" $stdJpg "crop-1-1" "jpg"
Test-Converter "Tools" "Crop 16:9" $stdJpg "crop-16-9" "jpg"
Test-Converter "Tools" "Rotate 90 CW" $stdJpg "rotate-90-cw" "jpg"
Test-Converter "Tools" "Rotate 90 CCW" $stdJpg "rotate-90-ccw" "jpg"
Test-Converter "Tools" "Rotate 180" $stdJpg "rotate-180" "jpg"
Test-Converter "Tools" "Flip Horizontal" $stdJpg "flip-h" "jpg"
Test-Converter "Tools" "Flip Vertical" $stdJpg "flip-v" "jpg"
Test-Converter "Tools" "Grayscale Filter" $stdJpg "grayscale" "jpg"
Test-Converter "Tools" "Remove Background" $transPng "remove-bg" "png" "89 50 4E 47"
Test-Converter "Tools" "Extract Live Photo" $motionJpg "live-photo" "mp4"
Test-Converter "Tools" "Merge to PDF" $stdJpg "merge-pdf" "pdf" "25 50 44 46"
Test-Converter "Tools" "One ZIP Bundle" $stdJpg "zip" "zip" "50 4B 03 04"

# --- 3. Documents & Text ---
Test-Converter "Docs" "TXT to PDF" $testTxt "pdf" "pdf" "25 50 44 46"
Test-Converter "Docs" "TXT to HTML" $testTxt "html" "html"
Test-Converter "Docs" "Markdown to HTML" $testMd "html" "html"
Test-Converter "Docs" "Markdown to PDF" $testMd "pdf" "pdf" "25 50 44 46"
Test-Converter "Docs" "Markdown to TXT" $testMd "txt" "txt"
Test-Converter "Docs" "HTML to TXT" $testHtml "txt" "txt"
Test-Converter "Docs" "HTML to PDF" $testHtml "pdf" "pdf" "25 50 44 46"

# --- 4. Data Formats ---
Test-Converter "Data" "CSV to JSON" $testCsv "json" "json"
Test-Converter "Data" "CSV to XML" $testCsv "xml" "xml"
Test-Converter "Data" "CSV to TSV" $testCsv "tsv" "tsv"
Test-Converter "Data" "CSV to HTML Table" $testCsv "html" "html"
Test-Converter "Data" "JSON to CSV" $testJson "csv" "csv"
Test-Converter "Data" "JSON to JSONL" $testJson "jsonl" "jsonl"
Test-Converter "Data" "JSON to XML" $testJson "xml" "xml"

# --- 5. Subtitles ---
Test-Converter "Subtitles" "SRT to WebVTT" $testSrt "vtt" "vtt"
Test-Converter "Subtitles" "SRT to TXT" $testSrt "txt" "txt"

# --- 6. Audio & Media ---
Test-Converter "Audio" "WAV to MP3" $testWav "mp3" "mp3"
Test-Converter "Audio" "WAV to FLAC" $testWav "flac" "flac"

# --- 7. Archives ---
Test-Converter "Archive" "Extract ZIP Archive" $testZip "extract" "txt"

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " Final Results: $passCount passed, $failCount failed out of $totalTests converters tested." -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
} else {
    exit 0
}
