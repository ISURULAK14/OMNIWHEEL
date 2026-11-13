# Automated Test Suite: All 19 Wheel Actions
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

$script:passCount = 0
$script:failCount = 0
$script:totalTests = 0

function Run-ActionTest {
    param(
        [string]$TestName,
        [string]$InputFile,
        [string]$Action,
        [string]$ExpectedExt,
        [string]$MagicBytesHex = ""
    )

    $script:totalTests++
    $outPath = Join-Path $outDir "$Action.$ExpectedExt"
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
    $p.WaitForExit(35000)
    $exitCode = $p.ExitCode

    $success = ($exitCode -eq 0) -and (Test-Path $outPath) -and ((Get-Item $outPath).Length -gt 0)

    if ($success -and $MagicBytesHex -ne "") {
        $bytes = [System.IO.File]::ReadAllBytes($outPath)
        $expectedBytes = [byte[]]($MagicBytesHex.Split(' ') | ForEach-Object { [Convert]::ToByte($_, 16) })
        for ($i = 0; $i -lt $expectedBytes.Length; $i++) {
            if ($i -ge $bytes.Length -or $bytes[$i] -ne $expectedBytes[$i]) {
                $success = $false
                $stderr += " Magic bytes mismatch at index $i (expected $MagicBytesHex)."
                break
            }
        }
    }

    if ($success) {
        $script:passCount++
        $size = (Get-Item $outPath).Length
        Write-Host " [PASS] $TestName -> ExitCode 0, Size: $size bytes" -ForegroundColor Green
    } else {
        $script:failCount++
        Write-Host " [FAIL] $TestName -> ExitCode $exitCode. Error: $stderr Output: $stdout" -ForegroundColor Red
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Testing Phase 4: All 19 Front & Back Wheel Actions" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$stdJpg = Join-Path $fixturesDir "standard.jpg"
$transPng = Join-Path $fixturesDir "transparent.png"
$samplePdf = Join-Path $fixturesDir "sample.pdf"
$motionJpg = Join-Path $fixturesDir "motion_photo.jpg"
$tripJpg = Join-Path $fixturesDir "trip (copy) 2026.jpg"

# --- Front Wheel (9 Actions) ---
Run-ActionTest "Front 1: Convert to PNG" $stdJpg "png" "png" "89 50 4E 47"
Run-ActionTest "Front 2: Convert to WEBP" $stdJpg "webp" "webp" "52 49 46 46"
Run-ActionTest "Front 3: Convert to PDF" $stdJpg "pdf" "pdf" "25 50 44 46"
Run-ActionTest "Front 4: Trace to SVG" $stdJpg "trace-svg" "svg"
Run-ActionTest "Front 5: Line art to SVG" $stdJpg "lineart-svg" "svg"
Run-ActionTest "Front 6: Convert to TIFF" $stdJpg "tiff" "tiff"
Run-ActionTest "Front 7: Convert to BMP" $stdJpg "bmp" "bmp" "42 4D"
Run-ActionTest "Front 8: Convert to ICO" $stdJpg "ico" "ico" "00 00 01 00"
Run-ActionTest "Front 9: Half Size" $stdJpg "half-size" "jpg"

# --- Back Wheel (10 Actions & Sub-Options) ---
Run-ActionTest "Back 10a: Crop (Auto-Crop)" $stdJpg "crop-auto" "jpg"
Run-ActionTest "Back 10b: Crop (Square 1:1)" $stdJpg "crop-1-1" "jpg"
Run-ActionTest "Back 10c: Crop (16:9)" $stdJpg "crop-16-9" "jpg"

Run-ActionTest "Back 11a: Rotate (90° CW)" $stdJpg "rotate-90-cw" "jpg"
Run-ActionTest "Back 11b: Rotate (90° CCW)" $stdJpg "rotate-90-ccw" "jpg"
Run-ActionTest "Back 11c: Rotate (180°)" $stdJpg "rotate-180" "jpg"

Run-ActionTest "Back 12a: Flip Horizontal" $stdJpg "flip-h" "jpg"
Run-ActionTest "Back 12b: Flip Vertical" $stdJpg "flip-v" "jpg"

Run-ActionTest "Back 13: Remove Background" $stdJpg "remove-bg" "png" "89 50 4E 47"
Run-ActionTest "Back 14: Grayscale" $stdJpg "grayscale" "jpg"
Run-ActionTest "Back 15: Extract Live Photo (MP4)" $motionJpg "live-photo" "mp4"
Run-ActionTest "Back 16: Convert to AVIF" $stdJpg "avif" "avif"
Run-ActionTest "Back 17: Convert to GIF" $stdJpg "gif" "gif" "47 49 46 38"
Run-ActionTest "Back 18: Merge PDF" $stdJpg "merge-pdf" "pdf" "25 50 44 46"
Run-ActionTest "Back 19: ZIP Archive" $stdJpg "zip" "zip" "50 4B 03 04"

# Specialized Robustness Tests
Run-ActionTest "Robustness 1: Filename with spaces and parentheses" $tripJpg "webp" "webp" "52 49 46 46"
$zeroTxt = Join-Path $fixturesDir "zero_byte.txt"
Run-ActionTest "Robustness 2: Zero-byte file handling" $zeroTxt "pdf" "pdf"

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " Wheel Action Results: $script:passCount passed, $script:failCount failed out of $script:totalTests tests." -ForegroundColor $(if ($script:failCount -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($script:failCount -gt 0) {
    exit 1
} else {
    exit 0
}
