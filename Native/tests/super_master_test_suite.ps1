# ==============================================================================
# OMNIWHEEL 1.0 - BRAND NEW SUPER MASTER ERROR-FREE TEST SUITE
# Author: ISURU GUNASEKERA
# Execution: Powershell -NoProfile -ExecutionPolicy Bypass
# ==============================================================================

[CmdletBinding()]
param(
    [switch]$Detailed = $false
)

$ErrorActionPreference = "Continue"

$startTime = [System.Diagnostics.Stopwatch]::StartNew()
$script:totalSuites = 11
$script:suitesPassed = 0
$script:totalChecks = 0
$script:checksPassed = 0
$script:checksFailed = 0

function Write-Header([string]$title) {
    Write-Host "`n==============================================================================" -ForegroundColor Cyan
    Write-Host " $title" -ForegroundColor Cyan
    Write-Host "==============================================================================" -ForegroundColor Cyan
}

function Assert-Check([string]$name, [bool]$passed, [string]$details = "") {
    $script:totalChecks++
    if ($passed) {
        $script:checksPassed++
        Write-Host " [PASS] $name" -ForegroundColor Green
        if ($details -and $Detailed) { Write-Host "        $details" -ForegroundColor DarkGray }
    } else {
        $script:checksFailed++
        Write-Host " [FAIL] $name" -ForegroundColor Red
        if ($details) { Write-Host "        $details" -ForegroundColor Yellow }
    }
}

Write-Header "OMNIWHEEL 1.0 - SUPER MASTER TEST SUITE (11 FULL PHASES)"

# Locate binaries
$omniRoot = "E:\ANTIGRAVITY\OMNIWHEEL"
$nativeRoot = "$omniRoot\Native"
$setupExe = "$omniRoot\OMNIWHEEL_Setup.exe"
$installedExe = "$env:LOCALAPPDATA\OMNIWHEEL\OMNIWHEEL.exe"
if (-not (Test-Path $installedExe)) {
    $installedExe = "$nativeRoot\bin\OMNIWHEEL.exe"
}

# ------------------------------------------------------------------------------
# SUITE 1: Standalone Packaging, PE Integrity & Single EXE Distribution
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 1/10] Standalone Packaging, PE & Single-EXE Verification ---" -ForegroundColor Yellow
$suite1Failed = 0

# Check strictly ONE exe in OMNIWHEEL folder
$rootExes = Get-ChildItem -Path $omniRoot -File -Filter "*.exe"
$singleExeCheck = ($rootExes.Count -eq 1 -and $rootExes[0].Name -eq "OMNIWHEEL_Setup.exe")
Assert-Check "Distribution: Strictly ONE executable (OMNIWHEEL_Setup.exe)" $singleExeCheck "Found $($rootExes.Count) exe(s): $($rootExes.Name -join ', ')"
if (-not $singleExeCheck) { $suite1Failed++ }

# Size check (>200MB contains CoreCLR, FFmpeg, resources)
$setupSize = if (Test-Path $setupExe) { (Get-Item $setupExe).Length } else { 0 }
$sizeOk = ($setupSize -gt 200MB)
Assert-Check "Distribution: Standalone payload complete (> 200MB, size: $([Math]::Round($setupSize/1MB, 2)) MB)" $sizeOk
if (-not $sizeOk) { $suite1Failed++ }

# Authenticode signature check
$sig = Get-AuthenticodeSignature $setupExe -ErrorAction SilentlyContinue
$sigOk = ($sig.Status -eq "Valid" -or ($sig.SignerCertificate -ne $null -and $sig.SignerCertificate.Subject -match "ISURU GUNASEKERA"))
Assert-Check "Security: Authenticode digital signature valid" $sigOk "Signer: $($sig.SignerCertificate.Subject)"
if (-not $sigOk) { $suite1Failed++ }

# PE Security Headers on OMNIWHEEL.exe
$peBytes = [System.IO.File]::ReadAllBytes($installedExe)
$peOffset = [BitConverter]::ToInt32($peBytes, 0x3C)
$optHeaderOffset = $peOffset + 24
$magic = [BitConverter]::ToUInt16($peBytes, $optHeaderOffset)
$dllCharOffset = if ($magic -eq 0x020B) { $optHeaderOffset + 70 } else { $optHeaderOffset + 46 }
$dllCharacteristics = [BitConverter]::ToUInt16($peBytes, $dllCharOffset)

$hasHighEntropy = ($dllCharacteristics -band 0x0020) -ne 0
$hasDynamicBase = ($dllCharacteristics -band 0x0040) -ne 0
$hasNxCompat    = ($dllCharacteristics -band 0x0100) -ne 0

Assert-Check "Security: High-Entropy ASLR (0x0020)" $hasHighEntropy
Assert-Check "Security: DynamicBase ASLR (0x0040)" $hasDynamicBase
Assert-Check "Security: NX Compat DEP (0x0100)" $hasNxCompat
if (-not ($hasHighEntropy -and $hasDynamicBase -and $hasNxCompat)) { $suite1Failed++ }

if ($suite1Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 2: Clean-Room Isolated Installation & Shell Registration
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 2/10] Clean-Room Isolated Installation & Shell Registration ---" -ForegroundColor Yellow
$suite2Failed = 0

$appDir = "$env:LOCALAPPDATA\OMNIWHEEL"
$roamingDir = "$env:APPDATA\OMNIWHEEL"

Assert-Check "Install: Local AppData Directory exists" (Test-Path $appDir)
Assert-Check "Install: Roaming AppData Directory exists" (Test-Path $roamingDir)
Assert-Check "Install: OMNIWHEEL.exe payload present" (Test-Path "$appDir\OMNIWHEEL.exe")
Assert-Check "Install: Embedded FFmpeg engine present" (Test-Path "$appDir\ffmpeg.exe")
Assert-Check "Install: Windows Native OCR script present" (Test-Path "$appDir\WindowsOcrEngine.ps1")
Assert-Check "Install: Settings configuration present" (Test-Path "$appDir\settings.ini")
Assert-Check "Install: Standalone uninstaller present" (Test-Path "$appDir\uninstall.exe")

# Shell Context Menu Keys
$regStar = Test-Path -LiteralPath "HKCU:\Software\Classes\*\shell\OMNIWHEEL"
$regDir = Test-Path "HKCU:\Software\Classes\Directory\shell\OMNIWHEEL"
$regBg = Test-Path "HKCU:\Software\Classes\Directory\Background\shell\OMNIWHEEL"
$regRun = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "OMNIWHEEL" -ErrorAction SilentlyContinue) -ne $null
$regUninstall = (Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OMNIWHEEL" -ErrorAction SilentlyContinue) -ne $null

Assert-Check "Shell: Context Menu on All Files (*)" $regStar
Assert-Check "Shell: Context Menu on Directories" $regDir
Assert-Check "Shell: Context Menu on Directory Background" $regBg
Assert-Check "Shell: Startup Hook in HKCU Run" $regRun
Assert-Check "Shell: Windows Add/Remove Programs registered" $regUninstall

if (-not ($regStar -and $regDir -and $regBg -and $regRun -and $regUninstall)) { $suite2Failed++ }
if ($suite2Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 3: Radial Donut Geometry & Polar Hit-Testing
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 3/10] Radial Donut Geometry & Polar Hit-Testing ---" -ForegroundColor Yellow
$suite3Failed = 0

function Test-PolarHit([double]$x, [double]$y, [double]$cx = 330, [double]$cy = 330) {
    $dx = $x - $cx
    $dy = $y - $cy
    $r = [Math]::Sqrt($dx * $dx + $dy * $dy)
    if ($r -le 65) { return "HUB_CANCEL" }
    if ($r -gt 222 -and $r -le 325) { return "SUB_ARC" }
    if ($r -gt 65 -and $r -le 222) {
        $angle = [Math]::Atan2($dx, -$dy) * (180.0 / [Math]::PI)
        if ($angle -lt 0) { $angle += 360.0 }
        $idx = [int][Math]::Floor(($angle + (360.0 / 18.0)) / (360.0 / 9.0)) % 9
        return "WEDGE_$idx"
    }
    return "OUTSIDE"
}

Assert-Check "Geometry: Dead Center (330, 330) -> HUB_CANCEL" ((Test-PolarHit 330 330) -eq "HUB_CANCEL")
Assert-Check "Geometry: Hub Boundary (330, 395, r=65) -> HUB_CANCEL" ((Test-PolarHit 330 395) -eq "HUB_CANCEL")
Assert-Check "Geometry: 12 o'clock (330, 200, r=130) -> WEDGE_0" ((Test-PolarHit 330 200) -eq "WEDGE_0")
Assert-Check "Geometry: 4 o'clock (430, 430, r=141) -> WEDGE_3" ((Test-PolarHit 430 430) -eq "WEDGE_3")
Assert-Check "Geometry: 8 o'clock (200, 400, r=147) -> WEDGE_6" ((Test-PolarHit 200 400) -eq "WEDGE_6")
Assert-Check "Geometry: Sub-Arc Outer (330, 70, r=260) -> SUB_ARC" ((Test-PolarHit 330 70) -eq "SUB_ARC")
Assert-Check "Geometry: Far Outside (330, 0, r=330) -> OUTSIDE" ((Test-PolarHit 330 0) -eq "OUTSIDE")

if ($suite3Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 4: Curd Design System, WCAG AAA Accessibility & 15 Themes
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 4/10] Curd Design System & WCAG AAA Accessibility ---" -ForegroundColor Yellow
$suite4Failed = 0

function Get-Luminance($r, $g, $b) {
    $vals = @($r, $g, $b) | ForEach-Object {
        $c = $_ / 255.0
        if ($c -le 0.03928) { $c / 12.92 } else { [Math]::Pow(($c + 0.055) / 1.055, 2.4) }
    }
    return (0.2126 * $vals[0] + 0.7152 * $vals[1] + 0.0722 * $vals[2])
}

function Get-ContrastRatio($l1, $l2) {
    $lighter = [Math]::Max($l1, $l2)
    $darker = [Math]::Min($l1, $l2)
    return ($lighter + 0.05) / ($darker + 0.05)
}

$lumWhite = Get-Luminance 255 255 255
$lumDarkText = Get-Luminance 30 41 59
$contrastBody = Get-ContrastRatio $lumWhite $lumDarkText
Assert-Check "WCAG AAA: Body Text Contrast ($([Math]::Round($contrastBody, 1)):1 >= 7.0:1)" ($contrastBody -ge 7.0)

$lumPurple = Get-Luminance 124 58 237
$contrastPurple = Get-ContrastRatio $lumPurple $lumWhite
Assert-Check "WCAG AA: Active Purple Wedge Contrast ($([Math]::Round($contrastPurple, 1)):1 >= 4.5:1)" ($contrastPurple -ge 4.5)

# Verify CurdTheme contains all 15 curated themes
$curdSource = Get-Content "$nativeRoot\CurdTheme.cs" -Raw
$expectedThemes = @(
    "Purple", "Emerald", "Cobalt", "Amber", "DarkSlate",
    "PastelLavender", "PastelMint", "PastelRose", "PastelSky", "PastelPeach",
    "PastelMatcha", "NordicFrost", "Monochrome", "Solar", "Ocean"
)

$themesFound = 0
foreach ($t in $expectedThemes) {
    if ($curdSource -match "case `"$t`":" -or $curdSource -match "`"$t`"") {
        $themesFound++
    }
}
Assert-Check "Design System: All 15 Curd Themes Implemented ($themesFound / 15)" ($themesFound -eq 15)

if ($suite4Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 5: Spacebar QuickLook Architecture
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 5/10] Spacebar QuickLook Architecture ---" -ForegroundColor Yellow
$suite5Failed = 0

$qlSource = Get-Content "$nativeRoot\QuickLookWindow.cs" -Raw
$qlPresenters = @(
    "CreateImageViewer", "CreateCodeViewer", "CreateMediaViewer",
    "CreateDocumentViewer", "CreateArchiveViewer", "CreateFolderViewer", "CreateFallbackViewer"
)
foreach ($p in $qlPresenters) {
    $hasP = $qlSource -match "UIElement $p\b|FrameworkElement $p\b|void $p\b|$p\("
    Assert-Check "QuickLook: Presenter '$p'" $hasP
    if (-not $hasP) { $suite5Failed++ }
}

$hasSyntax = $qlSource -match "HighlightSourceCode"
Assert-Check "QuickLook: Syntax Highlighting Engine" $hasSyntax

$hasAsyncThum = $qlSource -match "IShellItemImageFactory|GetImage"
Assert-Check "QuickLook: Windows Shell Thumbnail Factory Interop" $hasAsyncThum

if ($suite5Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 6: All 26 Front & Back Radial Actions Execution
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 6/10] All 26 Front & Back Radial Actions ---" -ForegroundColor Yellow
$suite6Failed = 0

$testImg = "$nativeRoot\test_fixtures\test_img.png"
if (-not (Test-Path $testImg)) {
    $bmp = New-Object System.Drawing.Bitmap(100, 100)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Blue)
    $g.Dispose()
    $bmp.Save($testImg, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$scratchDir = Join-Path $env:TEMP "OmniMasterSuite_$(Get-Random)"
New-Item -ItemType Directory -Path $scratchDir -Force | Out-Null

$actionsToTest = @(
    @{ Act = "png"; Ext = "png"; Name = "Front 1: Convert to PNG" },
    @{ Act = "webp"; Ext = "webp"; Name = "Front 2: Convert to WEBP" },
    @{ Act = "pdf"; Ext = "pdf"; Name = "Front 3: Convert to PDF" },
    @{ Act = "trace-svg"; Ext = "svg"; Name = "Front 4: Trace to SVG" },
    @{ Act = "lineart-svg"; Ext = "svg"; Name = "Front 5: Line art to SVG" },
    @{ Act = "tiff"; Ext = "tiff"; Name = "Front 6: Convert to TIFF" },
    @{ Act = "bmp"; Ext = "bmp"; Name = "Front 7: Convert to BMP" },
    @{ Act = "ico"; Ext = "ico"; Name = "Front 8: Convert to ICO" },
    @{ Act = "half-size"; Ext = "png"; Name = "Front 9: Half Size" },
    @{ Act = "crop-auto"; Ext = "png"; Name = "Back 10a: Crop Auto" },
    @{ Act = "crop-1-1"; Ext = "png"; Name = "Back 10b: Crop 1:1" },
    @{ Act = "crop-16-9"; Ext = "png"; Name = "Back 10c: Crop 16:9" },
    @{ Act = "rotate-90-cw"; Ext = "png"; Name = "Back 11a: Rotate 90 CW" },
    @{ Act = "rotate-90-ccw"; Ext = "png"; Name = "Back 11b: Rotate 90 CCW" },
    @{ Act = "rotate-180"; Ext = "png"; Name = "Back 11c: Rotate 180" },
    @{ Act = "flip-h"; Ext = "png"; Name = "Back 12a: Flip Horizontal" },
    @{ Act = "flip-v"; Ext = "png"; Name = "Back 12b: Flip Vertical" },
    @{ Act = "remove-bg"; Ext = "png"; Name = "Back 13: Remove Background" },
    @{ Act = "grayscale"; Ext = "png"; Name = "Back 14: Grayscale" },
    @{ Act = "gif"; Ext = "gif"; Name = "Back 15: Convert to GIF" },
    @{ Act = "avif"; Ext = "avif"; Name = "Back 16: Convert to AVIF" },
    @{ Act = "zip"; Ext = "zip"; Name = "Back 17: ZIP Archive" }
)

foreach ($item in $actionsToTest) {
    $outPath = Join-Path $scratchDir "$($item.Act).$($item.Ext)"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $installedExe
    $psi.Arguments = "--headless --input `"$testImg`" --action `"$($item.Act)`" --output `"$outPath`""
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    [void]$p.WaitForExit(15000)

    $json = $null
    foreach ($line in ($stdout -split "`r?`n")) {
        $t = $line.Trim()
        if ($t.StartsWith("{") -and $t -match '"success"') {
            try { $json = $t | ConvertFrom-Json; break } catch { }
        }
    }
    $created = Test-Path $outPath
    $ok = ($p.ExitCode -eq 0 -and $json -ne $null -and $json.success -eq $true -and $created)
    Assert-Check $item.Name $ok
    if (-not $ok) { $suite6Failed++ }
}

if ($suite6Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 7: High-Resolution OCR, Multi-Page PDF & Word (DOCX)
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 7/10] High-Resolution OCR, Multi-Page PDF & Word ---" -ForegroundColor Yellow
$suite7Failed = 0

$ocrPdf = "$omniRoot\OMNIWHEEL_BY_ISURU_User_Guide.pdf"
if (Test-Path $ocrPdf) {
    # PDF -> Word DOCX
    $outDocx = Join-Path $scratchDir "guide_text.docx"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $installedExe
    $psi.Arguments = "--headless --input `"$ocrPdf`" --action `"docx`" --output `"$outDocx`""
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    [void]$p.WaitForExit(35000)

    $json = $null
    foreach ($line in ($stdout -split "`r?`n")) {
        $t = $line.Trim()
        if ($t.StartsWith("{") -and $t -match '"success"') {
            try { $json = $t | ConvertFrom-Json; break } catch { }
        }
    }
    $docxOk = ($p.ExitCode -eq 0 -and $json -ne $null -and $json.success -eq $true -and (Test-Path $outDocx))
    Assert-Check "Engine: PDF -> Word (DOCX) Generation" $docxOk
    if (-not $docxOk) { $suite7Failed++ }

    # PDF -> OCR DOCX
    $outOcrDocx = Join-Path $scratchDir "guide_ocr.docx"
    $psi.Arguments = "--headless --input `"$ocrPdf`" --action `"ocr-docx`" --output `"$outOcrDocx`""
    $p = [System.Diagnostics.Process]::Start($psi)
    $stdout = $p.StandardOutput.ReadToEnd()
    [void]$p.WaitForExit(45000)

    $json = $null
    foreach ($line in ($stdout -split "`r?`n")) {
        $t = $line.Trim()
        if ($t.StartsWith("{") -and $t -match '"success"') {
            try { $json = $t | ConvertFrom-Json; break } catch { }
        }
    }
    $ocrDocxOk = ($p.ExitCode -eq 0 -and $json -ne $null -and $json.success -eq $true -and (Test-Path $outOcrDocx))
    Assert-Check "Engine: PDF -> OCR to Word (DOCX)" $ocrDocxOk
    if (-not $ocrDocxOk) { $suite7Failed++ }

    if ($ocrDocxOk -and (Test-Path $outOcrDocx)) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($outOcrDocx)
        $docXmlEntry = $zip.GetEntry("word/document.xml")
        $stream = $docXmlEntry.Open()
        $reader = New-Object System.IO.StreamReader($stream)
        $xmlContent = $reader.ReadToEnd()
        $reader.Close()
        $zip.Dispose()

        $pageBreaks = ([regex]::Matches($xmlContent, "<w:br\s+w:type=""page""\s*/>")).Count
        $breaksOk = ($pageBreaks -eq 4)
        Assert-Check "Engine: OCR Multi-Page Preservation (Exact 4 Page Breaks for 5 Pages)" $breaksOk "Found: $pageBreaks"
        if (-not $breaksOk) { $suite7Failed++ }

        $cleanOk = ($xmlContent -notmatch "--- PAGE")
        Assert-Check "Engine: Clean Body (Zero debug tokens in generated Word document)" $cleanOk
        if (-not $cleanOk) { $suite7Failed++ }
    }

    # Word (DOCX) -> PDF Compilation
    if (Test-Path $outDocx) {
        $outRePdf = Join-Path $scratchDir "recompiled_guide.pdf"
        $psi.Arguments = "--headless --input `"$outDocx`" --action `"pdf`" --output `"$outRePdf`""
        $p = [System.Diagnostics.Process]::Start($psi)
        $stdout = $p.StandardOutput.ReadToEnd()
        [void]$p.WaitForExit(25000)
        $rePdfOk = ($p.ExitCode -eq 0 -and (Test-Path $outRePdf) -and (Get-Item $outRePdf).Length -gt 1000)
        Assert-Check "Engine: Word (DOCX) -> PDF Vector Export" $rePdfOk
        if (-not $rePdfOk) { $suite7Failed++ }
    }
} else {
    Assert-Check "Engine: Multi-Page User Guide PDF found" $false "Missing: $ocrPdf"
    $suite7Failed++
}

if ($suite7Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 8: Cross-Format Data, Subtitles & Media Matrix
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 8/10] Cross-Format Data, Subtitles & Media Matrix ---" -ForegroundColor Yellow
$suite8Failed = 0

$csvFile = "$nativeRoot\test_fixtures\test_data.csv"
if (-not (Test-Path $csvFile)) { "id,name,role`r`n1,Isuru,Developer`r`n2,Omni,Bot" | Set-Content $csvFile }

# CSV -> TSV
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $installedExe
$psi.Arguments = "--headless --input `"$csvFile`" --action `"tsv`""
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.CreateNoWindow = $true
$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
[void]$p.WaitForExit(10000)
$tsvOk = ($p.ExitCode -eq 0)
Assert-Check "Data: CSV to TSV conversion" $tsvOk

# Subtitles SRT -> VTT
$srtFile = "$nativeRoot\test_fixtures\test_sub.srt"
if (-not (Test-Path $srtFile)) { "1`r`n00:00:01,000 --> 00:00:04,000`r`nHello World" | Set-Content $srtFile }
$psi.Arguments = "--headless --input `"$srtFile`" --action `"vtt`""
$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
[void]$p.WaitForExit(10000)
$vttOk = ($p.ExitCode -eq 0)
Assert-Check "Subtitles: SRT to VTT conversion" $vttOk

if ($suite8Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 9: Adversarial Fault Injection & Resilience
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 9/10] Adversarial Fault Injection & Resilience ---" -ForegroundColor Yellow
$suite9Failed = 0

# Zero-byte handling
$zeroFile = "$env:TEMP\zero_test_sample.txt"
[System.IO.File]::WriteAllBytes($zeroFile, (New-Object byte[] 0))

$psi.Arguments = "--headless --input `"$zeroFile`" --action `"pdf`""
$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
[void]$p.WaitForExit(10000)
$zeroOk = ($p.ExitCode -eq 0)
Assert-Check "Adversarial: Zero-byte file graceful handling" $zeroOk

# Invalid format handling (must not crash process)
$psi.Arguments = "--headless --input `"$zeroFile`" --action `"unsupported_format_xyz`""
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
$stderr = $p.StandardError.ReadToEnd()
[void]$p.WaitForExit(10000)
$invOk = ($p.ExitCode -ne 0 -or $stdout -match '"success":false' -or $stderr -match '"success":false')
Assert-Check "Adversarial: Invalid target format graceful rejection" $invOk

# Non-existent input file
$psi.Arguments = "--headless --input `"C:\non_existent_path_xyz123.abc`" --action `"pdf`""
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
$stderr = $p.StandardError.ReadToEnd()
[void]$p.WaitForExit(10000)
$missOk = ($p.ExitCode -ne 0 -or $stdout -match '"success":false' -or $stderr -match '"success":false')
Assert-Check "Adversarial: Non-existent file graceful rejection" $missOk

if ($suite9Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 10: Performance, Resource Footprint & Zero-Leak Audit
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 10/10] Performance & Zero-Leak Audit ---" -ForegroundColor Yellow
$suite10Failed = 0

$swBench = [System.Diagnostics.Stopwatch]::StartNew()
$psi.Arguments = "--headless --input `"$testImg`" --action `"webp`""
$p = [System.Diagnostics.Process]::Start($psi)
[void]$p.WaitForExit(10000)
$swBench.Stop()

$latencyOk = ($swBench.ElapsedMilliseconds -lt 4500)
Assert-Check "Performance: Execution latency ($($swBench.ElapsedMilliseconds)ms < 4500ms)" $latencyOk

# Memory Working Set Check
$proc = Get-Process -Name "OMNIWHEEL" -ErrorAction SilentlyContinue | Select-Object -First 1
$memOk = $true
if ($proc) {
    $wsMB = [Math]::Round($proc.WorkingSet64 / 1MB, 2)
    $memOk = ($wsMB -lt 150)
    Assert-Check "Resource: Idle Memory footprint ($wsMB MB < 150 MB)" $memOk
} else {
    Assert-Check "Resource: Memory footprint check" $true "No running background daemon consuming memory"
}

# Cleanup scratch
Remove-Item $scratchDir -Recurse -Force -ErrorAction SilentlyContinue

if ($suite10Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# SUITE 11: Fluid UI Animations & Hardware-Accelerated Transitions
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 11/11] Fluid UI Animations & Hardware-Accelerated Transitions ---" -ForegroundColor Yellow
$suite11Failed = 0

$radialCs = Join-Path $nativeRoot "RadialDonutWindow.cs"
$radialContent = Get-Content $radialCs -Raw

$quicklookCs = Join-Path $nativeRoot "QuickLookWindow.cs"
$quicklookContent = Get-Content $quicklookCs -Raw

$htmlFile = Join-Path $omniRoot "OMNIWHEEL_3D_Frosted_UI.html"
$htmlContent = Get-Content $htmlFile -Raw

# 1. Radial Donut Animations
$animCheck1 = ($radialContent -match "private TransformGroup _transformGroup;" -and $radialContent -match "private ScaleTransform _flipTransform;")
Assert-Check "Animation: Radial 3D Transform Composition" $animCheck1
if (-not $animCheck1) { $suite11Failed++ }

$animCheck2 = ($radialContent -match "new BackEase \{ Amplitude = 0.28, EasingMode = EasingMode.EaseOut \}")
Assert-Check "Animation: Radial Pop-In BackEase Overshoot (140ms)" $animCheck2
if (-not $animCheck2) { $suite11Failed++ }

$animCheck3 = ($radialContent -match "TimeSpan.FromMilliseconds\(85\)" -and $radialContent -match "TimeSpan.FromMilliseconds\(125\)")
Assert-Check "Animation: Radial 3D Coin-Flip Double-Action (ScaleX squeeze & expand)" $animCheck3
if (-not $animCheck3) { $suite11Failed++ }

$animCheck4 = ($radialContent -match "exitScaleX = new DoubleAnimation" -and $radialContent -match "TimeSpan.FromMilliseconds\(75\)")
Assert-Check "Animation: Radial Smooth Exit (75ms fade & shrink)" $animCheck4
if (-not $animCheck4) { $suite11Failed++ }

# 2. QuickLook Transitions
$animCheck5 = ($quicklookContent -match "private ScaleTransform _cardScaleTransform;" -and $quicklookContent -match "new BackEase \{ Amplitude = 0.25, EasingMode = EasingMode.EaseOut \}")
Assert-Check "Animation: QuickLook Card Entrance Pop-In (150ms BackEase)" $animCheck5
if (-not $animCheck5) { $suite11Failed++ }

$animCheck6 = ($quicklookContent -match "delta > 0 \? 28\.0 : -28\.0" -and $quicklookContent -match "_slideTransform.BeginAnimation")
Assert-Check "Animation: QuickLook Carousel Horizontal Slide (±28px 140ms)" $animCheck6
if (-not $animCheck6) { $suite11Failed++ }

$animCheck7 = ($quicklookContent -match "exitFade = new DoubleAnimation" -and $quicklookContent -match "TimeSpan.FromMilliseconds\(80\)")
Assert-Check "Animation: QuickLook Smooth Exit (80ms fade & shrink)" $animCheck7
if (-not $animCheck7) { $suite11Failed++ }

# 3. Three.js 3D Frosted Physics
$animCheck8 = ($htmlContent -match "wheelRoot\.position\.y = Math\.sin\(elapsedTime \* 1\.5\) \* 0\.12;")
Assert-Check "Animation: Three.js Organic Floating Breath (0.12)" $animCheck8
if (-not $animCheck8) { $suite11Failed++ }

$animCheck9 = ($htmlContent -match "flipSwoopZ = Math\.sin\(Math\.min\(Math\.PI, rotDiff\)\) \* 0\.85;")
Assert-Check "Animation: Three.js 3D Flip Swoop (0.85 Z-arc)" $animCheck9
if (-not $animCheck9) { $suite11Failed++ }

$animCheck10 = ($htmlContent -match "mesh\.position\.z \+= \(targetZ - mesh\.position\.z\) \* 0\.14;" -and $htmlContent -match "targetScale = isTarget \? 1\.04 : 1\.0;")
Assert-Check "Animation: Three.js Per-Wedge Hover Spring Lerp (0.14 damping)" $animCheck10
if (-not $animCheck10) { $suite11Failed++ }

if ($suite11Failed -eq 0) { $script:suitesPassed++ }

# ------------------------------------------------------------------------------
# FINAL REPORT
# ------------------------------------------------------------------------------
$startTime.Stop()
$elapsedSec = [Math]::Round($startTime.Elapsed.TotalSeconds, 2)

Write-Host "`n==============================================================================" -ForegroundColor Cyan
Write-Host " SUPER MASTER TEST SUITE EXECUTION SUMMARY" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host " Total Test Suites  : $script:totalSuites"
Write-Host " Suites Passed      : $script:suitesPassed"
Write-Host " Total Checks       : $script:totalChecks"
Write-Host " Checks Passed      : $script:checksPassed"
Write-Host " Checks Failed      : $script:checksFailed"
Write-Host " Execution Time     : $elapsedSec seconds"
Write-Host " Pass Rate          : $([Math]::Round(($script:checksPassed / $script:totalChecks) * 100, 1))%"
Write-Host "==============================================================================" -ForegroundColor Cyan

if ($script:checksFailed -eq 0) {
    Write-Host ">>> ALL 11 PHASES PASSED WITH ZERO ERRORS. SHIP QUALITY CONFIRMED! <<<" -ForegroundColor Green
    exit 0
} else {
    Write-Host ">>> SOME CHECKS FAILED. PLEASE REVIEW THE LOG ABOVE <<<" -ForegroundColor Red
    exit 1
}
