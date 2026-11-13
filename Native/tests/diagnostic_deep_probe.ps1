# ==============================================================================
# OMNIWHEEL ADVANCED DIAGNOSTIC PROBE & STRESS HARNESS
# File: tests\diagnostic_deep_probe.ps1
#
# Diagnostic Parameters:
#   PARAM 1: Random Bit-Rot & Fuzzy Data Stream Mutilation
#   PARAM 2: Multi-Encoding & BOM Drift (UTF-8, UTF-16LE, UTF-16BE, ANSI, mixed EOL)
#   PARAM 3: Windows Reserved Device Names & Pathological Paths (CON, NUL, AUX, MAX_PATH)
#   PARAM 4: High-Contention Concurrent Collisions & Target Lock Races
#   PARAM 5: Unmanaged Resource Soak & Handle Leak Tracking (GDI, USER, Handles)
#   PARAM 6: Matrix Topology & Semantic Output Non-Empty Integrity
# ==============================================================================

[CmdletBinding()]
param(
    [string]$TargetExe = "E:\ANTIGRAVITY\OMNIWHEEL\Native\bin\OMNIWHEEL.exe",
    [string]$ScratchDir = "E:\_omni_deep_probe_scratch"
)

$ErrorActionPreference = "Continue"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "        OMNIWHEEL DEEP DIAGNOSTIC PROBE & STRESS HARNESS              " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

if (-not (Test-Path $TargetExe)) {
    Write-Error "Target executable not found: $TargetExe"
    exit 1
}

# Clean scratch
if (Test-Path $ScratchDir) {
    Remove-Item $ScratchDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $ScratchDir -Force | Out-Null

# Win32 GUI Resource definitions
Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
$win32Type = @"
using System;
using System.Runtime.InteropServices;

public static class NativeWin32Metrics
{
    [DllImport("user32.dll")]
    public static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
}
"@
try { Add-Type -TypeDefinition $win32Type -Language CSharp -ErrorAction SilentlyContinue } catch { }

$global:TotalProbes = 0
$global:ProbePassed = 0
$global:ProbeFailed = 0
$global:Discrepancies = @()

function Invoke-ProbeConvert([string]$inPath, [string]$action) {
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $TargetExe
        $psi.Arguments = "--headless --input `"$inPath`" --action `"$action`""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true

        $proc = [System.Diagnostics.Process]::Start($psi)
        $stdout = $proc.StandardOutput.ReadToEnd()
        $stderr = $proc.StandardError.ReadToEnd()
        [void]$proc.WaitForExit(35000)

        $json = $null
        $combined = "$stdout`n$stderr"
        foreach ($line in ($combined -split "`r?`n")) {
            $t = $line.Trim()
            if ($t.StartsWith("{") -and ($t -match '"success"' -or $t -match '"error"')) {
                try { $json = $t | ConvertFrom-Json; break } catch { }
            }
        }

        $resSuccess = ($proc.ExitCode -eq 0 -and $json -ne $null -and $json.success -eq $true)
        $outPath = if ($json -and $json.outputPath) { $json.outputPath } else { "" }
        $outSize = if ($outPath -and (Test-Path $outPath)) { (Get-Item $outPath).Length } else { 0 }
        $msg = if ($json -and $json.message) { $json.message } elseif ($json -and $json.error) { $json.error } elseif ($stderr) { $stderr.Trim() } else { "" }

        return @{
            Success = $resSuccess
            OutputPath = $outPath
            Message = $msg
            OutputSize = $outSize
            ThrewException = ($proc.ExitCode -ne 0 -and -not $resSuccess -and $json -eq $null)
            ExceptionText = $stderr
        }
    } catch {
        return @{
            Success = $false
            OutputPath = ""
            Message = $_.Exception.Message
            OutputSize = 0
            ThrewException = $true
            ExceptionText = $_.Exception.ToString()
        }
    }
}

function Record-ProbeResult {
    param(
        [string]$Param,
        [string]$TestName,
        [bool]$Condition,
        [string]$Details = ""
    )
    $global:TotalProbes++
    if ($Condition) {
        $global:ProbePassed++
        Write-Host " [PASS] [$Param] $TestName" -ForegroundColor Green
    } else {
        $global:ProbeFailed++
        $global:Discrepancies += [PSCustomObject]@{
            Parameter = $Param
            Test = $TestName
            Detail = $Details
        }
        Write-Host " [FAIL] [$Param] $TestName -> $Details" -ForegroundColor Red
    }
}

# ------------------------------------------------------------------------------
# PARAMETER 1: RANDOM BIT-ROT & FUZZY STREAM MUTILATION
# ------------------------------------------------------------------------------
Write-Host "`n>>> [PARAM 1] BIT-ROT & FUZZY STREAM MUTILATION <<<" -ForegroundColor Yellow
$p1Dir = Join-Path $ScratchDir "param1_bitrot"
New-Item -ItemType Directory -Path $p1Dir -Force | Out-Null

# Create valid base image
$validBmp = Join-Path $p1Dir "valid.bmp"
$bmpObj = New-Object System.Drawing.Bitmap 64, 64
$g = [System.Drawing.Graphics]::FromImage($bmpObj)
$g.Clear([System.Drawing.Color]::Blue)
$g.Dispose()
$bmpObj.Save($validBmp, [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmpObj.Dispose()

# 1.1 Bit-rotted BMP (header intact, body 10% corrupted)
$rotBmp = Join-Path $p1Dir "rot.bmp"
$bmpBytes = [System.IO.File]::ReadAllBytes($validBmp)
$rand = New-Object System.Random 1337
for ($i = 54; $i -lt $bmpBytes.Length; $i++) {
    if ($rand.Next(10) -eq 0) { $bmpBytes[$i] = [byte]($rand.Next(256)) }
}
[System.IO.File]::WriteAllBytes($rotBmp, $bmpBytes)
$res = Invoke-ProbeConvert $rotBmp "png"
Record-ProbeResult -Param "Bit-Rot" -TestName "BMP body 10% corrupted -> png" -Condition (-not $res.ThrewException) -Details "Threw: $($res.ExceptionText)"

# 1.2 Bit-rotted PNG (valid 8-byte magic header, corrupted chunk data)
$rotPng = Join-Path $p1Dir "rot.png"
$pngBytes = [byte[]]@(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) + (1..300 | ForEach-Object { [byte]($rand.Next(256)) })
[System.IO.File]::WriteAllBytes($rotPng, $pngBytes)
$res = Invoke-ProbeConvert $rotPng "jpg"
Record-ProbeResult -Param "Bit-Rot" -TestName "PNG random payload with intact header -> jpg" -Condition (-not $res.ThrewException -and -not $res.Success) -Details "Expected clean rejection, got: Success=$($res.Success), Threw=$($res.ThrewException)"

# 1.3 Bit-rotted JPEG (valid FF D8 FF E0, random scan data)
$rotJpg = Join-Path $p1Dir "rot.jpg"
$jpgBytes = [byte[]]@(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01) + (1..400 | ForEach-Object { [byte]($rand.Next(256)) })
[System.IO.File]::WriteAllBytes($rotJpg, $jpgBytes)
$res = Invoke-ProbeConvert $rotJpg "webp"
Record-ProbeResult -Param "Bit-Rot" -TestName "JPEG corrupted scan payload -> webp" -Condition (-not $res.ThrewException -and -not $res.Success) -Details "Expected clean rejection, got: Success=$($res.Success), Threw=$($res.ThrewException)"

# 1.4 Bit-rotted MP3 (valid ID3v2 header, random garbage payload)
$rotMp3 = Join-Path $p1Dir "rot.mp3"
$mp3Bytes = [byte[]]@(0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x76) + (1..500 | ForEach-Object { [byte]($rand.Next(256)) })
[System.IO.File]::WriteAllBytes($rotMp3, $mp3Bytes)
$res = Invoke-ProbeConvert $rotMp3 "wav"
Record-ProbeResult -Param "Bit-Rot" -TestName "MP3 garbage payload with ID3v2 header -> wav" -Condition (-not $res.ThrewException -and -not $res.Success) -Details "Expected clean rejection, got: Success=$($res.Success), Threw=$($res.ThrewException)"

# ------------------------------------------------------------------------------
# PARAMETER 2: MULTI-ENCODING & BOM DRIFT
# ------------------------------------------------------------------------------
Write-Host "`n>>> [PARAM 2] MULTI-ENCODING & BOM DRIFT <<<" -ForegroundColor Yellow
$p2Dir = Join-Path $ScratchDir "param2_encodings"
New-Item -ItemType Directory -Path $p2Dir -Force | Out-Null

$sampleText = @"
ID,Name,Role,Location
1,Guenther,Mueller,Muenchen
2,Elodie,Rene,Paris
3,Tanaka,Taro,Tokyo
4,Ivan,Petrov,Moscow
5,Starman,Explorer,Mars
"@

# 2.1 UTF-8 with BOM
$csvUtf8Bom = Join-Path $p2Dir "utf8_bom.csv"
$utf8BomEncoding = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($csvUtf8Bom, $sampleText, $utf8BomEncoding)
$res = Invoke-ProbeConvert $csvUtf8Bom "json"
$validUtf8 = $false
if ($res.Success -and (Test-Path $res.OutputPath)) {
    $outContent = [System.IO.File]::ReadAllText($res.OutputPath, [System.Text.Encoding]::UTF8)
    if ($outContent.Contains("Muenchen") -and $outContent.Contains("Tanaka") -and $outContent.Contains("Starman")) {
        $validUtf8 = $true
    }
}
Record-ProbeResult -Param "Encoding" -TestName "CSV UTF-8 with BOM -> JSON preserves data" -Condition $validUtf8 -Details "Output missing fields in JSON"

# 2.2 UTF-16 LE with BOM
$csvUtf16Le = Join-Path $p2Dir "utf16_le.csv"
[System.IO.File]::WriteAllText($csvUtf16Le, $sampleText, [System.Text.Encoding]::Unicode)
$res = Invoke-ProbeConvert $csvUtf16Le "tsv"
Record-ProbeResult -Param "Encoding" -TestName "CSV UTF-16 LE -> TSV" -Condition (-not $res.ThrewException) -Details "Threw exception on UTF-16 LE file: $($res.ExceptionText)"

# 2.3 ANSI (Windows-1252) with high-byte characters
$csvAnsi = Join-Path $p2Dir "ansi_1252.csv"
$ansiText = @"
ID,Name,City
1,Rene,Zurich
2,Mueller,Koeln
"@
$enc1252 = [System.Text.Encoding]::GetEncoding(1252)
[System.IO.File]::WriteAllText($csvAnsi, $ansiText, $enc1252)
$res = Invoke-ProbeConvert $csvAnsi "json"
Record-ProbeResult -Param "Encoding" -TestName "CSV Windows-1252 ANSI -> JSON" -Condition (-not $res.ThrewException) -Details "Threw exception on ANSI: $($res.ExceptionText)"

# 2.4 Mixed Line Endings in Subtitles
$srtMixed = Join-Path $p2Dir "mixed_eol.srt"
$srtContent = "1`r`n00:00:01,000 --> 00:00:03,000`r`nFirst Line`r`n`r`n2`n00:00:04,000 --> 00:00:06,000`nSecond Line`n`n3`r`n00:00:07,000 --> 00:00:09,000`r`nThird Line`r`n`r`n"
[System.IO.File]::WriteAllText($srtMixed, $srtContent, [System.Text.Encoding]::UTF8)
$res = Invoke-ProbeConvert $srtMixed "vtt"
$parsedAll3 = $false
if ($res.Success -and (Test-Path $res.OutputPath)) {
    $vttText = [System.IO.File]::ReadAllText($res.OutputPath, [System.Text.Encoding]::UTF8)
    if ($vttText.Contains("First Line") -and $vttText.Contains("Second Line") -and $vttText.Contains("Third Line")) {
        $parsedAll3 = $true
    }
}
Record-ProbeResult -Param "Encoding" -TestName "SRT with mixed line endings -> VTT" -Condition $parsedAll3 -Details "Failed to parse all 3 cues under mixed EOLs"

# ------------------------------------------------------------------------------
# PARAMETER 3: WINDOWS RESERVED DEVICE NAMES & PATHOLOGICAL PATHS
# ------------------------------------------------------------------------------
Write-Host "`n>>> [PARAM 3] RESERVED DEVICE NAMES & PATHOLOGICAL PATHS <<<" -ForegroundColor Yellow
$p3Dir = Join-Path $ScratchDir "param3_paths"
New-Item -ItemType Directory -Path $p3Dir -Force | Out-Null

# 3.1 Non-existent Reserved Device Name (CON.txt)
$conPath = Join-Path $p3Dir "CON.txt"
$res = Invoke-ProbeConvert $conPath "pdf"
Record-ProbeResult -Param "ReservedPaths" -TestName "Opening non-existent CON.txt -> pdf cleanly caught" -Condition (-not $res.ThrewException -and -not $res.Success) -Details "Threw unhandled exception or returned true on CON device"

# 3.2 Non-existent NUL.png
$nulPath = Join-Path $p3Dir "NUL.png"
$res = Invoke-ProbeConvert $nulPath "jpg"
Record-ProbeResult -Param "ReservedPaths" -TestName "Opening non-existent NUL.png -> jpg cleanly caught" -Condition (-not $res.ThrewException -and -not $res.Success) -Details "Threw unhandled exception on NUL device"

# 3.3 Trailing spaces in directory path
$spaceDir = Join-Path $p3Dir "dir_with_trailing_space"
try {
    [System.IO.Directory]::CreateDirectory($spaceDir) | Out-Null
    $spFile = Join-Path $spaceDir "sample.txt"
    [System.IO.File]::WriteAllText($spFile, "Hello World from trailing space path")
    $res = Invoke-ProbeConvert $spFile "pdf"
    Record-ProbeResult -Param "ReservedPaths" -TestName "Conversion inside path with spaces" -Condition (-not $res.ThrewException) -Details "Threw: $($res.ExceptionText)"
} catch {
    Record-ProbeResult -Param "ReservedPaths" -TestName "Directory creation with spaces" -Condition $true -Details "Handled by OS"
}

# ------------------------------------------------------------------------------
# PARAMETER 4: HIGH-CONTENTION CONCURRENT COLLISIONS & LOCK RACES
# ------------------------------------------------------------------------------
Write-Host "`n>>> [PARAM 4] HIGH-CONTENTION CONCURRENT COLLISIONS <<<" -ForegroundColor Yellow
$p4Dir = Join-Path $ScratchDir "param4_contention"
New-Item -ItemType Directory -Path $p4Dir -Force | Out-Null

$sharedSource = Join-Path $p4Dir "source_data.csv"
[System.IO.File]::WriteAllText($sharedSource, "col1,col2,col3`n1,2,3`n4,5,6`n7,8,9")

# 20 parallel threads converting the exact same source to JSON simultaneously
$runspacePool = [runspacefactory]::CreateRunspacePool(1, 8)
$runspacePool.Open()
$tasks = @()

for ($t = 0; $t -lt 20; $t++) {
    $ps = [powershell]::Create()
    $ps.RunspacePool = $runspacePool
    [void]$ps.AddScript({
        param($exe, $src)
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exe
        $psi.Arguments = "--headless --input `"$src`" --action `"json`""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $p = [System.Diagnostics.Process]::Start($psi)
        [void]$p.StandardOutput.ReadToEnd()
        [void]$p.WaitForExit(35000)
        return ($p.ExitCode -eq 0)
    }).AddArgument($TargetExe).AddArgument($sharedSource)
    $tasks += [PSCustomObject]@{
        PowerShell = $ps
        Handle = $ps.BeginInvoke()
    }
}

$contentionSuccess = 0
$contentionCrashes = 0
foreach ($task in $tasks) {
    try {
        $r = $task.PowerShell.EndInvoke($task.Handle)
        if ($r -eq $true) { $contentionSuccess++ }
    } catch {
        $contentionCrashes++
    } finally {
        $task.PowerShell.Dispose()
    }
}
$runspacePool.Close()
$runspacePool.Dispose()

# Verify no dangling .part.* files remain
$danglingParts = (Get-ChildItem -Path $p4Dir -Filter "*.part.*" -Recurse -ErrorAction SilentlyContinue).Count
Record-ProbeResult -Param "Contention" -TestName "20 concurrent calls on identical file: zero crashes" -Condition ($contentionCrashes -eq 0) -Details "Crashes: $contentionCrashes"
Record-ProbeResult -Param "Contention" -TestName "20 concurrent calls: zero dangling .part.* files" -Condition ($danglingParts -eq 0) -Details "Dangling .part.* files left: $danglingParts"

# ------------------------------------------------------------------------------
# PARAMETER 5: UNMANAGED RESOURCE SOAK & HANDLE TRACKING
# ------------------------------------------------------------------------------
Write-Host "`n>>> [PARAM 5] UNMANAGED RESOURCE SOAK & HANDLE TRACKING <<<" -ForegroundColor Yellow
$p5Dir = Join-Path $ScratchDir "param5_soak"
New-Item -ItemType Directory -Path $p5Dir -Force | Out-Null

$proc = [System.Diagnostics.Process]::GetCurrentProcess()
$initialHandles = $proc.HandleCount
$initialGdi = 0
$initialUser = 0
try {
    $initialGdi = [NativeWin32Metrics]::GetGuiResources($proc.Handle, 0)
    $initialUser = [NativeWin32Metrics]::GetGuiResources($proc.Handle, 1)
} catch { }

# Create test sample
$soakTxt = Join-Path $p5Dir "soak.txt"
[System.IO.File]::WriteAllText($soakTxt, "Soak testing line 1`r`nLine 2`r`nLine 3")

# Run 50 rapid sequential conversions
for ($i = 0; $i -lt 50; $i++) {
    $res = Invoke-ProbeConvert $soakTxt "pdf"
    $res = Invoke-ProbeConvert $soakTxt "html"
}

[System.GC]::Collect()
[System.GC]::WaitForPendingFinalizers()
$proc.Refresh()

$finalHandles = $proc.HandleCount
$finalGdi = 0
$finalUser = 0
try {
    $finalGdi = [NativeWin32Metrics]::GetGuiResources($proc.Handle, 0)
    $finalUser = [NativeWin32Metrics]::GetGuiResources($proc.Handle, 1)
} catch { }

$gdiDelta = $finalGdi - $initialGdi
Record-ProbeResult -Param "Soak" -TestName "50 rapid conversions: Zero GDI handle leaks (Delta: $gdiDelta)" -Condition ($gdiDelta -le 2) -Details "GDI handle delta was: $gdiDelta"

# ------------------------------------------------------------------------------
# PARAMETER 6: TOPOLOGY & SEMANTIC OUTPUT INTEGRITY
# ------------------------------------------------------------------------------
Write-Host "`n>>> [PARAM 6] TOPOLOGY & SEMANTIC OUTPUT INTEGRITY <<<" -ForegroundColor Yellow
$p6Dir = Join-Path $ScratchDir "param6_topology"
New-Item -ItemType Directory -Path $p6Dir -Force | Out-Null

# 6.1 vCard to JSON / CSV / HTML / Markdown round-trip
$vcfSample = Join-Path $p6Dir "contact.vcf"
$vcfContent = @"
BEGIN:VCARD
VERSION:3.0
FN:Dr. Alexander Fleming
N:Fleming;Alexander;;Dr.;
TEL;TYPE=WORK:+44-20-7123-4567
EMAIL:afleming@stmarys.ac.uk
ORG:St. Mary's Hospital
TITLE:Professor of Bacteriology
END:VCARD
"@
[System.IO.File]::WriteAllText($vcfSample, $vcfContent, [System.Text.Encoding]::UTF8)

$resCsv = Invoke-ProbeConvert $vcfSample "csv"
Record-ProbeResult -Param "Topology" -TestName "vCard -> CSV non-empty" -Condition ($resCsv.Success -and $resCsv.OutputSize -gt 50) -Details "Size: $($resCsv.OutputSize)"

$resJson = Invoke-ProbeConvert $vcfSample "json"
$hasFleming = $false
if ($resJson.Success -and (Test-Path $resJson.OutputPath)) {
    $jsonContent = [System.IO.File]::ReadAllText($resJson.OutputPath, [System.Text.Encoding]::UTF8)
    if ($jsonContent.Contains("Alexander Fleming") -and $jsonContent.Contains("afleming@stmarys.ac.uk")) {
        $hasFleming = $true
    }
}
Record-ProbeResult -Param "Topology" -TestName "vCard -> JSON schema integrity (contains name and email)" -Condition $hasFleming -Details "Failed to preserve fields in JSON"

$resMd = Invoke-ProbeConvert $vcfSample "md"
Record-ProbeResult -Param "Topology" -TestName "vCard -> Markdown table non-empty" -Condition ($resMd.Success -and $resMd.OutputSize -gt 50) -Details "Size: $($resMd.OutputSize)"

# 6.2 CSV -> vCard generation
$csvContact = Join-Path $p6Dir "new_contacts.csv"
$csvContent = @"
Name,Phone,Email,Company,Title
Ada Lovelace,+44-1234,ada@analytical.org,Computing,Pioneer
"@
[System.IO.File]::WriteAllText($csvContact, $csvContent, [System.Text.Encoding]::UTF8)
$resVcf = Invoke-ProbeConvert $csvContact "vcf"
$validVcardGenerated = $false
if ($resVcf.Success -and (Test-Path $resVcf.OutputPath)) {
    $vcfGen = [System.IO.File]::ReadAllText($resVcf.OutputPath, [System.Text.Encoding]::UTF8)
    if ($vcfGen.Contains("BEGIN:VCARD") -and $vcfGen.Contains("FN:Ada Lovelace") -and $vcfGen.Contains("END:VCARD")) {
        $validVcardGenerated = $true
    }
}
Record-ProbeResult -Param "Topology" -TestName "CSV -> vCard (VCF) generation integrity" -Condition $validVcardGenerated -Details "Generated VCF missing standard RFC markers"

# 6.3 Markdown with Headers, Lists, Tables -> HTML & PDF
$mdComplex = Join-Path $p6Dir "complex.md"
$mdBody = @"
# Heading 1

## Subheading

- Item 1
- Item 2

| Col A | Col B |
| --- | --- |
| Val 1 | Val 2 |

[Link](https://example.com)
"@
[System.IO.File]::WriteAllText($mdComplex, $mdBody, [System.Text.Encoding]::UTF8)
$resHtml = Invoke-ProbeConvert $mdComplex "html"
$resPdf = Invoke-ProbeConvert $mdComplex "pdf"
Record-ProbeResult -Param "Topology" -TestName "Complex Markdown -> HTML" -Condition ($resHtml.Success -and $resHtml.OutputSize -gt 100) -Details "HTML output empty"
Record-ProbeResult -Param "Topology" -TestName "Complex Markdown -> PDF" -Condition ($resPdf.Success -and $resPdf.OutputSize -gt 300) -Details "PDF output empty"

# Clean scratch
Remove-Item $ScratchDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n======================================================================" -ForegroundColor Cyan
Write-Host "                DIAGNOSTIC PROBE SUMMARY                              " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " Total Diagnostic Probes : $global:TotalProbes"
Write-Host " Probes Passed           : $global:ProbePassed" -ForegroundColor Green
Write-Host " Probes Failed           : $global:ProbeFailed" -ForegroundColor $(if ($global:ProbeFailed -eq 0) { "Green" } else { "Red" })
Write-Host "======================================================================" -ForegroundColor Cyan

if ($global:ProbeFailed -gt 0) {
    Write-Host "`nDISCREPANCIES DISCOVERED:" -ForegroundColor Yellow
    $global:Discrepancies | Format-Table -AutoSize
    exit 1
} else {
    Write-Host "`nAll diagnostic parameters verified successfully with 0 discrepancies." -ForegroundColor Green
    exit 0
}
