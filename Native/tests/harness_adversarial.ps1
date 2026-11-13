# ==============================================================================
# OMNIWHEEL Adversarial Fault-Injection Test Harness
# File: tests\harness_adversarial.ps1
#
# Exhaustively subjects FileConversionEngine to extreme failure modes:
#   1. Zero-byte files across all converter types
#   2. Truncated streams & incomplete compression/structure
#   3. Bit-flipped and corrupted magic headers
#   4. Extension spoofing & mismatched file signatures
#   5. Deep Unicode paths, emojis, and special characters
#   6. MAX_PATH (> 260 chars) and long paths
#   7. FileShare.None locked files
#   8. Read-only inputs and read-only target output directories/files
#   9. Rapid thread cancellation mid-conversion
#  10. Dangling file lock, .part file, and process leak verification
# ==============================================================================

[CmdletBinding()]
param(
    [string]$TargetExe = "",
    [string]$WorkingDir = "E:\_adv_harness_scratch",
    [switch]$VerboseOutput = $false
)

$ErrorActionPreference = "Continue"

try {
    Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
} catch { }

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "   OMNIWHEEL ADVERSARIAL FAULT-INJECTION TEST HARNESS (EXTREME QA)   " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# Locate OMNIWHEEL binary
if ([string]::IsNullOrEmpty($TargetExe)) {
    $candidates = @(
        (Join-Path $PSScriptRoot "..\bin\OMNIWHEEL.exe"),
        "E:\ANTIGRAVITY\OMNIWHEEL\Native\bin\OMNIWHEEL.exe",
        "E:\ANTIGRAVITY\Native\bin\OMNIWHEEL.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $TargetExe = (Resolve-Path $c).Path; break }
    }
}

if (-not (Test-Path $TargetExe)) {
    Write-Error "Could not locate OMNIWHEEL.exe. Looked at: $TargetExe"
    exit 1
}

Write-Host "[INIT] Target Binary: $TargetExe" -ForegroundColor Yellow



# Setup C# Cancellation Helper
$cancelHelperCode = @"
using System;
using System.Threading;

namespace OmniAdvTest
{
    public static class CancelHelper
    {
        public static bool RunAndAbort(Action action, int delayMs)
        {
            bool threadAliveAfterAbort = false;
            Thread t = new Thread(() =>
            {
                try { action(); }
                catch (ThreadAbortException) { Thread.ResetAbort(); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
            Thread.Sleep(delayMs);
            try { t.Abort(); } catch { }
            threadAliveAfterAbort = !t.Join(1000);
            return threadAliveAfterAbort;
        }
    }
}
"@
try {
    Add-Type -TypeDefinition $cancelHelperCode -Language CSharp -ErrorAction SilentlyContinue
} catch { }

# Setup scratch directory
if (Test-Path $WorkingDir) {
    Remove-Item $WorkingDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $WorkingDir | Out-Null
Write-Host "[INIT] Scratch Directory: $WorkingDir" -ForegroundColor Yellow

# Metric tracking
$global:TotalTests = 0
$global:CleanlyCaught = 0
$global:Vulnerabilities = 0
$global:UnhandledExceptions = 0
$global:DanglingPartFiles = 0
$global:DanglingLocks = 0
$global:OrphanedProcesses = 0
$global:ReportLog = @()

function Clean-OrphanedProcesses {
    $leaked = Get-Process -Name WINWORD, EXCEL, ffmpeg, PDFREFLOW -ErrorAction SilentlyContinue
    if ($leaked) {
        foreach ($p in $leaked) {
            try { $p.Kill(); $global:OrphanedProcesses++ } catch { }
        }
    }
}

function Invoke-EngineConvert([string]$inputPath, [string]$action) {
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $TargetExe
        $psi.Arguments = "--headless --input `"$inputPath`" --action `"$action`""
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
            ThrewException = ($json -eq $null -and $proc.ExitCode -ne 0)
            ExceptionMessage = $msg
        }
    } catch {
        return @{
            Success = $false
            OutputPath = ""
            Message = $_.Exception.Message
            OutputSize = 0
            ThrewException = $true
            ExceptionMessage = $_.Exception.ToString()
        }
    }
}

function Assert-AdversarialFault {
    param(
        [string]$Suite,
        [string]$TestName,
        [string]$InputPath,
        [string]$Action,
        [bool]$ExpectFault = $true
    )

    $global:TotalTests++
    $beforePartCount = (Get-ChildItem -Path $WorkingDir -Filter "*.part.*" -Recurse -ErrorAction SilentlyContinue).Count
    $beforeProcs = (Get-Process -Name WINWORD, EXCEL, ffmpeg, PDFREFLOW -ErrorAction SilentlyContinue).Count

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $res = Invoke-EngineConvert $InputPath $Action
    $sw.Stop()

    $afterPartCount = (Get-ChildItem -Path $WorkingDir -Filter "*.part.*" -Recurse -ErrorAction SilentlyContinue).Count
    $afterProcs = (Get-Process -Name WINWORD, EXCEL, ffmpeg, PDFREFLOW -ErrorAction SilentlyContinue).Count

    # Verification 1: Unhandled exception check
    $isUnhandled = $res.ThrewException

    # Verification 2: Success flag assertion
    $faultHandledCleanly = $false
    if ($ExpectFault) {
        if (-not $res.Success -and -not [string]::IsNullOrEmpty($res.Message) -and -not $isUnhandled) {
            $faultHandledCleanly = $true
        }
    } else {
        if ($res.Success -and -not $isUnhandled) {
            $faultHandledCleanly = $true
        }
    }

    # Verification 3: Temporary .part.* leak check
    $partLeaked = ($afterPartCount -gt $beforePartCount)
    if ($partLeaked) { $global:DanglingPartFiles++ }

    # Verification 4: Dangling file lock check
    $lockDangling = $false
    if (Test-Path $InputPath) {
        $isReadOnly = (Get-Item $InputPath).IsReadOnly
        if (-not $isReadOnly) {
            try {
                $testStream = [System.IO.File]::Open($InputPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
                $testStream.Close()
                $testStream.Dispose()
            } catch {
                $lockDangling = $true
                $global:DanglingLocks++
            }
        }
    }

    # Verification 5: Process leak check
    $procLeaked = ($afterProcs -gt $beforeProcs)
    if ($procLeaked) {
        Clean-OrphanedProcesses
    }

    # Record outcome
    $status = ""
    if ($isUnhandled) {
        $status = "CRASH_UNHANDLED_EXCEPTION"
        $global:UnhandledExceptions++
        Write-Host " [CRASH] [$Suite] $TestName -> $Action | Unhandled Exception: $($res.Message)" -ForegroundColor Red
    } elseif ($partLeaked -or $lockDangling -or $procLeaked) {
        $status = "RESOURCE_LEAK"
        $global:Vulnerabilities++
        Write-Host " [LEAK] [$Suite] $TestName -> $Action | Leaks: part=$partLeaked, lock=$lockDangling, proc=$procLeaked" -ForegroundColor Magenta
    } elseif ($ExpectFault -and $res.Success) {
        $status = "FALSE_SUCCESS_VULNERABILITY"
        $global:Vulnerabilities++
        Write-Host " [VULN] [$Suite] $TestName -> $Action | False Success: Result.Success=True on invalid data! (Msg: $($res.Message))" -ForegroundColor Red
    } elseif ($faultHandledCleanly) {
        $status = "CLEAN_PASS"
        $global:CleanlyCaught++
        if ($global:VerboseOutput) {
            Write-Host " [PASS] [$Suite] $TestName -> $Action | Cleanly rejected: $($res.Message) ($($sw.ElapsedMilliseconds)ms)" -ForegroundColor Green
        } else {
            Write-Host " [PASS] [$Suite] $TestName -> $Action ($($sw.ElapsedMilliseconds)ms)" -ForegroundColor Green
        }
    } else {
        $status = "FAILED_ASSERTION"
        $global:Vulnerabilities++
        Write-Host " [FAIL] [$Suite] $TestName -> $Action | Result.Success=$($res.Success), Msg=$($res.Message)" -ForegroundColor Yellow
    }

    $global:ReportLog += [PSCustomObject]@{
        Suite = $Suite
        TestName = $TestName
        Action = $Action
        Status = $status
        Success = $res.Success
        Message = $res.Message
        DurationMs = $sw.ElapsedMilliseconds
        PartLeaked = $partLeaked
        LockDangling = $lockDangling
        ProcLeaked = $procLeaked
        Unhandled = $isUnhandled
    }
}

# ==============================================================================
# SUITE 1: ZERO-BYTE FILES (0-byte files across all converter targets)
# ==============================================================================
Write-Host "`n=== SUITE 1: ZERO-BYTE FAULT INJECTION ===" -ForegroundColor Cyan
$s1Dir = Join-Path $WorkingDir "suite1_zero_bytes"
if (-not (Test-Path $s1Dir)) { New-Item -ItemType Directory -Path $s1Dir -Force | Out-Null }

$zeroFormats = @(
    @{ Ext = "png"; Actions = @("jpg", "webp", "bmp", "ico", "tiff", "pdf", "remove-bg", "half-size", "trace-svg", "crop-auto", "rotate-90-cw", "grayscale") },
    @{ Ext = "jpg"; Actions = @("png", "webp", "bmp", "ico", "tiff", "pdf", "flip-h", "crop-1-1") },
    @{ Ext = "pdf"; Actions = @("docx", "txt", "md", "html", "png", "ocr-txt") },
    @{ Ext = "docx"; Actions = @("pdf", "txt", "md", "html", "doc") },
    @{ Ext = "xlsx"; Actions = @("csv", "tsv", "json", "pdf", "html") },
    @{ Ext = "csv"; Actions = @("tsv", "json", "xlsx", "pdf", "html") },
    @{ Ext = "srt"; Actions = @("vtt", "txt", "ass") },
    @{ Ext = "zip"; Actions = @("extract") },
    @{ Ext = "json"; Actions = @("csv", "xml", "xlsx", "jsonl") },
    @{ Ext = "xml"; Actions = @("json", "csv", "html") },
    @{ Ext = "pptx"; Actions = @("pdf", "txt", "docx") },
    @{ Ext = "rtf"; Actions = @("txt", "pdf", "docx") },
    @{ Ext = "mp4"; Actions = @("mp3", "gif", "wav") },
    @{ Ext = "mp3"; Actions = @("wav", "ogg", "flac") }
)

foreach ($item in $zeroFormats) {
    $zFile = Join-Path $s1Dir "zero.$($item.Ext)"
    [System.IO.File]::WriteAllBytes($zFile, @())
    foreach ($act in $item.Actions) {
        Assert-AdversarialFault -Suite "ZeroByte" -TestName "0-byte $($item.Ext)" -InputPath $zFile -Action $act -ExpectFault $true
    }
}

# ==============================================================================
# SUITE 2: TRUNCATED FILES (Headers cut off mid-stream, incomplete blocks)
# ==============================================================================
Write-Host "`n=== SUITE 2: TRUNCATED STREAMS AND INCOMPLETE STRUCTURES ===" -ForegroundColor Cyan
$s2Dir = Join-Path $WorkingDir "suite2_truncated"
if (-not (Test-Path $s2Dir)) { New-Item -ItemType Directory -Path $s2Dir -Force | Out-Null }

# 1. Truncated PNG (8-byte signature only, no IHDR)
$trPng = Join-Path $s2Dir "trunc.png"
[System.IO.File]::WriteAllBytes($trPng, [byte[]]@(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated PNG (no IHDR)" -InputPath $trPng -Action "jpg" -ExpectFault $true

# 2. Truncated JPEG (SOI marker FF D8 and incomplete APP0 header)
$trJpg = Join-Path $s2Dir "trunc.jpg"
[System.IO.File]::WriteAllBytes($trJpg, [byte[]]@(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46))
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated JPEG (cut APP0)" -InputPath $trJpg -Action "png" -ExpectFault $true

# 3. Truncated WebP (RIFF header claiming 50000 bytes, but file is 14 bytes)
$trWebp = Join-Path $s2Dir "trunc.webp"
[System.IO.File]::WriteAllBytes($trWebp, [byte[]]@(0x52, 0x49, 0x46, 0x46, 0x50, 0xC3, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50))
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated WebP (undersized RIFF)" -InputPath $trWebp -Action "jpg" -ExpectFault $true

# 4. Truncated BMP (14 byte file header without DIB header)
$trBmp = Join-Path $s2Dir "trunc.bmp"
[System.IO.File]::WriteAllBytes($trBmp, [byte[]]@(0x42, 0x4D, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x00, 0x00))
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated BMP (missing DIB)" -InputPath $trBmp -Action "png" -ExpectFault $true

# 5. Truncated PDF (Valid header %PDF-1.4, object stream cut off, missing %%EOF)
$trPdf = Join-Path $s2Dir "trunc.pdf"
[System.IO.File]::WriteAllText($trPdf, "%PDF-1.4`n1 0 obj`n<< /Type /Catalog /Pages 2 0 R >>`nendobj`n2 0 obj`n<< /Type /Pages /Kids [3 0 R] /Count 1 >>`nendobj`n3 0 obj`n<< /Type /Page >>`n")
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated PDF (missing xref & %%EOF)" -InputPath $trPdf -Action "docx" -ExpectFault $true
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated PDF -> txt" -InputPath $trPdf -Action "txt" -ExpectFault $true

# 6. Truncated DOCX (Valid ZIP local file header 50 4B 03 04, cut off mid deflate, no central dir)
$trDocx = Join-Path $s2Dir "trunc.docx"
[System.IO.File]::WriteAllBytes($trDocx, [byte[]]@(0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x77, 0x6F, 0x72, 0x64, 0x2F))
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated DOCX (missing ZIP central dir)" -InputPath $trDocx -Action "pdf" -ExpectFault $true
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated DOCX -> txt" -InputPath $trDocx -Action "txt" -ExpectFault $true

# 7. Truncated XLSX (ZIP cut off before sheet1.xml completion)
$trXlsx = Join-Path $s2Dir "trunc.xlsx"
[System.IO.File]::WriteAllBytes($trXlsx, [byte[]]@(0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x78, 0x6C, 0x2F))
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated XLSX (missing central dir)" -InputPath $trXlsx -Action "csv" -ExpectFault $true
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated XLSX -> json" -InputPath $trXlsx -Action "json" -ExpectFault $true

# 8. Truncated PPTX (ZIP cut off)
$trPptx = Join-Path $s2Dir "trunc.pptx"
[System.IO.File]::WriteAllBytes($trPptx, [byte[]]@(0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x70, 0x70, 0x74, 0x2F))
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated PPTX" -InputPath $trPptx -Action "pdf" -ExpectFault $true

# 9. Truncated ZIP archive
$trZip = Join-Path $s2Dir "trunc.zip"
[System.IO.File]::WriteAllBytes($trZip, [byte[]]@(0x50, 0x4B, 0x03, 0x04, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x66, 0x69, 0x6C, 0x65, 0x2E, 0x74, 0x78, 0x74))
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated ZIP (missing central directory)" -InputPath $trZip -Action "extract" -ExpectFault $true

# 10. Truncated CSV (unclosed quotes and truncated fields)
$trCsv = Join-Path $s2Dir "trunc.csv"
[System.IO.File]::WriteAllText($trCsv, "id,name,description`n1,Valid,Entry`n2,unclosed string without end")
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated CSV" -InputPath $trCsv -Action "json" -ExpectFault $false

# 11. Truncated JSON
$trJson = Join-Path $s2Dir "trunc.json"
[System.IO.File]::WriteAllText($trJson, "[{`"id`": 1, `"name`": `"Incomplete Item`", `"data`": [1, 2,")
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated JSON (broken syntax)" -InputPath $trJson -Action "csv" -ExpectFault $true

# 12. Truncated XML
$trXml = Join-Path $s2Dir "trunc.xml"
[System.IO.File]::WriteAllText($trXml, "<catalog><book id=`"bk101`"><title>Truncated Book Title</title><author>")
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated XML (unclosed tags)" -InputPath $trXml -Action "json" -ExpectFault $true

# 13. Truncated Subtitle SRT
$trSrt = Join-Path $s2Dir "trunc.srt"
[System.IO.File]::WriteAllText($trSrt, "1`n00:00:01,000 --> 00:00:04,000`nHello World`n`n2`n00:00:05,000 -->")
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated SRT (broken timestamp block)" -InputPath $trSrt -Action "vtt" -ExpectFault $true

# 14. Truncated MP4
$trMp4 = Join-Path $s2Dir "trunc.mp4"
[System.IO.File]::WriteAllBytes($trMp4, [byte[]]@(0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D))
Assert-AdversarialFault -Suite "Truncated" -TestName "Truncated MP4 (incomplete atom)" -InputPath $trMp4 -Action "mp3" -ExpectFault $true

# ==============================================================================
# SUITE 3: BIT-FLIPPED / CORRUPTED HEADERS
# ==============================================================================
Write-Host "`n=== SUITE 3: BIT-FLIPPED / CORRUPTED HEADERS ===" -ForegroundColor Cyan
$s3Dir = Join-Path $WorkingDir "suite3_corrupt_headers"
if (-not (Test-Path $s3Dir)) { New-Item -ItemType Directory -Path $s3Dir -Force | Out-Null }

# 1. Corrupted JPEG (FF 00 FF E0 instead of FF D8 FF E0)
$crJpg = Join-Path $s3Dir "bad_magic.jpg"
[System.IO.File]::WriteAllBytes($crJpg, [byte[]]@(0xFF, 0x00, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01))
Assert-AdversarialFault -Suite "CorruptHeader" -TestName "Corrupt JPEG Magic" -InputPath $crJpg -Action "png" -ExpectFault $true

# 2. Corrupted PNG (Magic bytes replaced with DE AD BE EF)
$crPng = Join-Path $s3Dir "bad_magic.png"
[System.IO.File]::WriteAllBytes($crPng, [byte[]]@(0xDE, 0xAD, 0xBE, 0xEF, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52))
Assert-AdversarialFault -Suite "CorruptHeader" -TestName "Corrupt PNG Magic" -InputPath $crPng -Action "jpg" -ExpectFault $true

# 3. Corrupted XLSX (ZIP magic PK\x03\x04 flipped to PK\x00\x00)
$crXlsx = Join-Path $s3Dir "bad_magic.xlsx"
[System.IO.File]::WriteAllBytes($crXlsx, [byte[]]@(0x50, 0x4B, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x88, 0x99, 0xAA, 0xBB))
Assert-AdversarialFault -Suite "CorruptHeader" -TestName "Corrupt XLSX Magic" -InputPath $crXlsx -Action "csv" -ExpectFault $true

# 4. Corrupted DOCX (ZIP magic flipped)
$crDocx = Join-Path $s3Dir "bad_magic.docx"
[System.IO.File]::WriteAllBytes($crDocx, [byte[]]@(0x50, 0x4B, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x88, 0x99, 0xAA, 0xBB))
Assert-AdversarialFault -Suite "CorruptHeader" -TestName "Corrupt DOCX Magic" -InputPath $crDocx -Action "pdf" -ExpectFault $true

# 5. Corrupted PDF (%XXX-1.4 instead of %PDF-1.4)
$crPdf = Join-Path $s3Dir "bad_magic.pdf"
[System.IO.File]::WriteAllText($crPdf, "%XXX-1.4`nRandom garbage byte noise in place of PDF structure %%EOF")
Assert-AdversarialFault -Suite "CorruptHeader" -TestName "Corrupt PDF Magic" -InputPath $crPdf -Action "docx" -ExpectFault $true

# 6. Corrupted ZIP
$crZip = Join-Path $s3Dir "bad_magic.zip"
[System.IO.File]::WriteAllBytes($crZip, [byte[]]@(0x50, 0x4B, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00))
Assert-AdversarialFault -Suite "CorruptHeader" -TestName "Corrupt ZIP Magic" -InputPath $crZip -Action "extract" -ExpectFault $true

# 7. Corrupted MP4
$crMp4 = Join-Path $s3Dir "bad_magic.mp4"
[System.IO.File]::WriteAllBytes($crMp4, [byte[]]@(0x00, 0x00, 0x00, 0x18, 0x62, 0x61, 0x64, 0x5F, 0x6D, 0x61, 0x67, 0x69, 0x63))
Assert-AdversarialFault -Suite "CorruptHeader" -TestName "Corrupt MP4 Magic" -InputPath $crMp4 -Action "mp3" -ExpectFault $true

# ==============================================================================
# SUITE 4: EXTENSION SPOOFING & MISMATCHED HEADERS
# ==============================================================================
Write-Host "`n=== SUITE 4: EXTENSION SPOOFING & MISMATCHED HEADERS ===" -ForegroundColor Cyan
$s4Dir = Join-Path $WorkingDir "suite4_spoofed_ext"
if (-not (Test-Path $s4Dir)) { New-Item -ItemType Directory -Path $s4Dir -Force | Out-Null }

# 1. PE Executable (MZ header) renamed to .pdf
$exeAsPdf = Join-Path $s4Dir "malicious_payload.pdf"
[System.IO.File]::WriteAllBytes($exeAsPdf, [byte[]]@(0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00))
Assert-AdversarialFault -Suite "Spoofing" -TestName "EXE renamed to PDF -> docx" -InputPath $exeAsPdf -Action "docx" -ExpectFault $true
Assert-AdversarialFault -Suite "Spoofing" -TestName "EXE renamed to PDF -> txt" -InputPath $exeAsPdf -Action "txt" -ExpectFault $true

# 2. PE Executable renamed to .png
$exeAsPng = Join-Path $s4Dir "binary_image.png"
[System.IO.File]::WriteAllBytes($exeAsPng, [byte[]]@(0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00))
Assert-AdversarialFault -Suite "Spoofing" -TestName "EXE renamed to PNG -> jpg" -InputPath $exeAsPng -Action "jpg" -ExpectFault $true

# 3. PE Executable renamed to .docx
$exeAsDocx = Join-Path $s4Dir "fake_word.docx"
[System.IO.File]::WriteAllBytes($exeAsDocx, [byte[]]@(0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00))
Assert-AdversarialFault -Suite "Spoofing" -TestName "EXE renamed to DOCX -> pdf" -InputPath $exeAsDocx -Action "pdf" -ExpectFault $true

# 4. JPEG image renamed to .xlsx
$jpgAsXlsx = Join-Path $s4Dir "photo_as_sheet.xlsx"
$dummyBmp = New-Object System.Drawing.Bitmap(32, 32)
$dummyBmp.Save($jpgAsXlsx, [System.Drawing.Imaging.ImageFormat]::Jpeg)
$dummyBmp.Dispose()
Assert-AdversarialFault -Suite "Spoofing" -TestName "JPG renamed to XLSX -> csv" -InputPath $jpgAsXlsx -Action "csv" -ExpectFault $true
Assert-AdversarialFault -Suite "Spoofing" -TestName "JPG renamed to XLSX -> json" -InputPath $jpgAsXlsx -Action "json" -ExpectFault $true

# 5. HTML page saved as .docx
$htmlAsDocx = Join-Path $s4Dir "webpage_as_word.docx"
[System.IO.File]::WriteAllText($htmlAsDocx, "<!DOCTYPE html><html><body><h1>Fake Document</h1><p>Not a real docx</p></body></html>")
Assert-AdversarialFault -Suite "Spoofing" -TestName "HTML saved as DOCX -> pdf" -InputPath $htmlAsDocx -Action "pdf" -ExpectFault $true
Assert-AdversarialFault -Suite "Spoofing" -TestName "HTML saved as DOCX -> txt" -InputPath $htmlAsDocx -Action "txt" -ExpectFault $true

# 6. Plain text saved as .zip
$txtAsZip = Join-Path $s4Dir "notes_as_archive.zip"
[System.IO.File]::WriteAllText($txtAsZip, "This is plain ASCII text pretending to be a ZIP file.")
Assert-AdversarialFault -Suite "Spoofing" -TestName "TXT saved as ZIP -> extract" -InputPath $txtAsZip -Action "extract" -ExpectFault $true

# 7. PNG image renamed to .csv
$pngAsCsv = Join-Path $s4Dir "image_as_data.csv"
$dummyBmp2 = New-Object System.Drawing.Bitmap(16, 16)
$dummyBmp2.Save($pngAsCsv, [System.Drawing.Imaging.ImageFormat]::Png)
$dummyBmp2.Dispose()
Assert-AdversarialFault -Suite "Spoofing" -TestName "PNG renamed to CSV -> tsv" -InputPath $pngAsCsv -Action "tsv" -ExpectFault $true

# ==============================================================================
# SUITE 5: DEEP UNICODE PATHS, EMOJIS, AND SPACES
# ==============================================================================
Write-Host "`n=== SUITE 5: DEEP UNICODE, EMOJIS, AND SPACES ===" -ForegroundColor Cyan
$uniDir = Join-Path $WorkingDir "test_🚀_漢字_Ω_ñ_🔥\dir with spaces (1) [special]"
[System.IO.Directory]::CreateDirectory($uniDir) | Out-Null

# 1. Valid PNG in Unicode/Emoji path (MUST succeed cleanly)
$uniPng = Join-Path $uniDir "image 🚀 漢字 (1) [test].png"
$bmpU = New-Object System.Drawing.Bitmap(64, 64)
$gU = [System.Drawing.Graphics]::FromImage($bmpU)
$gU.Clear([System.Drawing.Color]::Teal)
$gU.Dispose()
$bmpU.Save($uniPng, [System.Drawing.Imaging.ImageFormat]::Png)
$bmpU.Dispose()
Assert-AdversarialFault -Suite "Unicode" -TestName "Valid PNG in Unicode/Emoji path" -InputPath $uniPng -Action "jpg" -ExpectFault $false

# 2. Valid TXT in Unicode/Emoji path (MUST succeed cleanly)
$uniTxt = Join-Path $uniDir "notes 🚀 漢字 (1) [test].txt"
[System.IO.File]::WriteAllText($uniTxt, "UTF-8 Multi-language content: Hello 世界 Ω ñ")
Assert-AdversarialFault -Suite "Unicode" -TestName "Valid TXT in Unicode/Emoji path" -InputPath $uniTxt -Action "pdf" -ExpectFault $false

# 3. Corrupted file in Unicode path (Fault MUST be caught cleanly without path garbling or crash)
$uniCorrupt = Join-Path $uniDir "corrupt 🚀 漢字 [test].png"
[System.IO.File]::WriteAllBytes($uniCorrupt, [byte[]]@(0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11))
Assert-AdversarialFault -Suite "Unicode" -TestName "Corrupt PNG in Unicode path" -InputPath $uniCorrupt -Action "jpg" -ExpectFault $true

# ==============================================================================
# SUITE 6: MAX_PATH EDGE CASES (> 260 CHARACTERS AND LONG PATHS)
# ==============================================================================
Write-Host "`n=== SUITE 6: MAX_PATH (> 260 CHARS) AND LONG PATHS ===" -ForegroundColor Cyan
$subA = "path_seg_" + ("x" * 70)
$subB = "path_seg_" + ("y" * 70)
$subC = "path_seg_" + ("z" * 70)
$longDir = Join-Path $WorkingDir $subA
$longDir = Join-Path $longDir $subB
$longDir = Join-Path $longDir $subC
Write-Host " [INFO] Constructing path length: $($longDir.Length) chars" -ForegroundColor Gray

try {
    [System.IO.Directory]::CreateDirectory($longDir) | Out-Null
    $longTxt = Join-Path $longDir "deep_document_file.txt"
    [System.IO.File]::WriteAllText($longTxt, "Content inside deeply nested MAX_PATH directory.")
    Assert-AdversarialFault -Suite "MaxPath" -TestName "TXT at path len $($longTxt.Length) -> pdf" -InputPath $longTxt -Action "pdf" -ExpectFault $false
} catch {
    Write-Host " [WARN] OS or runtime prevented creating MAX_PATH directory: $_" -ForegroundColor Yellow
}

# ==============================================================================
# SUITE 7: EXCLUSIVELY LOCKED FILES (FileShare.None)
# ==============================================================================
Write-Host "`n=== SUITE 7: EXCLUSIVELY LOCKED FILES (FileShare.None) ===" -ForegroundColor Cyan
$s7Dir = Join-Path $WorkingDir "suite7_locked"
if (-not (Test-Path $s7Dir)) { New-Item -ItemType Directory -Path $s7Dir -Force | Out-Null }

$lockTargets = @(
    @{ Name = "locked.png"; Action = "jpg"; Data = [byte[]]@(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) },
    @{ Name = "locked.txt"; Action = "pdf"; Data = [System.Text.Encoding]::UTF8.GetBytes("Locked text file content") },
    @{ Name = "locked.pdf"; Action = "docx"; Data = [System.Text.Encoding]::UTF8.GetBytes("%PDF-1.4 locked stream") },
    @{ Name = "locked.xlsx"; Action = "csv"; Data = [byte[]]@(0x50, 0x4B, 0x03, 0x04) }
)

foreach ($lt in $lockTargets) {
    $lPath = Join-Path $s7Dir $lt.Name
    [System.IO.File]::WriteAllBytes($lPath, $lt.Data)
    $stream = [System.IO.File]::Open($lPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    
    $global:TotalTests++
    $beforePartCount = (Get-ChildItem -Path $WorkingDir -Filter "*.part.*" -Recurse -ErrorAction SilentlyContinue).Count
    $beforeProcs = (Get-Process -Name WINWORD, EXCEL, ffmpeg, PDFREFLOW -ErrorAction SilentlyContinue).Count

    # Attempt conversion while file is locked exclusively
    $res = Invoke-EngineConvert $lPath $lt.Action
    
    # Release test stream lock
    $stream.Close()
    $stream.Dispose()

    $afterPartCount = (Get-ChildItem -Path $WorkingDir -Filter "*.part.*" -Recurse -ErrorAction SilentlyContinue).Count
    $afterProcs = (Get-Process -Name WINWORD, EXCEL, ffmpeg, PDFREFLOW -ErrorAction SilentlyContinue).Count

    $partLeaked = ($afterPartCount -gt $beforePartCount)
    if ($partLeaked) { $global:DanglingPartFiles++ }

    $lockDangling = $false
    try {
        $testS = [System.IO.File]::Open($lPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $testS.Close()
        $testS.Dispose()
    } catch {
        $lockDangling = $true
        $global:DanglingLocks++
    }

    $procLeaked = ($afterProcs -gt $beforeProcs)
    if ($procLeaked) { Clean-OrphanedProcesses }

    if (-not $res.Success -and -not $partLeaked -and -not $lockDangling -and -not $procLeaked) {
        $global:CleanlyCaught++
        Write-Host " [PASS] [FileLock] Locked $($lt.Name) -> $($lt.Action) cleanly rejected ($($res.Message))" -ForegroundColor Green
    } else {
        $global:Vulnerabilities++
        Write-Host " [FAIL] [FileLock] Locked $($lt.Name) -> $($lt.Action) | Success=$($res.Success), Leaks: part=$partLeaked, lock=$lockDangling, proc=$procLeaked" -ForegroundColor Red
    }

    $global:ReportLog += [PSCustomObject]@{
        Suite = "FileLock"
        TestName = "Locked $($lt.Name)"
        Action = $lt.Action
        Status = if (-not $res.Success) { "CLEAN_PASS" } else { "FAILED_ASSERTION" }
        Success = $res.Success
        Message = $res.Message
        DurationMs = 0
        PartLeaked = $partLeaked
        LockDangling = $lockDangling
        ProcLeaked = $procLeaked
        Unhandled = $res.ThrewException
    }
}

# ==============================================================================
# SUITE 8: READ-ONLY FILES AND READ-ONLY OUTPUT DIRECTORIES
# ==============================================================================
Write-Host "`n=== SUITE 8: READ-ONLY INPUTS & READ-ONLY TARGET DIRECTORIES ===" -ForegroundColor Cyan
$s8Dir = Join-Path $WorkingDir "suite8_readonly"
if (-not (Test-Path $s8Dir)) { New-Item -ItemType Directory -Path $s8Dir -Force | Out-Null }

# 1. Read-Only Input File (File itself is marked ReadOnly, but directory is writable -> SHOULD SUCCEED)
$roInFile = Join-Path $s8Dir "readonly_source.txt"
[System.IO.File]::WriteAllText($roInFile, "Source file marked ReadOnly.")
$itemRO = Get-Item $roInFile
$itemRO.IsReadOnly = $true
try {
    Assert-AdversarialFault -Suite "ReadOnly" -TestName "ReadOnly Input File -> pdf" -InputPath $roInFile -Action "pdf" -ExpectFault $false
} finally {
    $itemRO.IsReadOnly = $false
}

# 2. Existing Output File Marked Read-Only (Cannot overwrite without error)
$roSrc = Join-Path $s8Dir "overwrite_test.txt"
[System.IO.File]::WriteAllText($roSrc, "New content attempting to overwrite read-only output.")
$roDst = Join-Path $s8Dir "overwrite_test.pdf"
[System.IO.File]::WriteAllText($roDst, "Old existing locked output.")
$itemDst = Get-Item $roDst
$itemDst.IsReadOnly = $true
try {
    Assert-AdversarialFault -Suite "ReadOnly" -TestName "Overwrite ReadOnly Target PDF" -InputPath $roSrc -Action "pdf" -ExpectFault $true
} finally {
    $itemDst.IsReadOnly = $false
}

# 3. Read-Only Directory (Write permission denied via ACL)
$roSubdir = Join-Path $s8Dir "locked_folder"
if (-not (Test-Path $roSubdir)) { New-Item -ItemType Directory -Path $roSubdir -Force | Out-Null }
$validInRo = Join-Path $roSubdir "data.txt"
[System.IO.File]::WriteAllText($validInRo, "Document inside write-denied directory.")
$acl = Get-Acl $roSubdir
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule([System.Security.Principal.WindowsIdentity]::GetCurrent().Name, "Write", "Deny")
$acl.AddAccessRule($rule)
Set-Acl $roSubdir $acl
try {
    Assert-AdversarialFault -Suite "ReadOnly" -TestName "Conversion inside Deny-Write Directory" -InputPath $validInRo -Action "pdf" -ExpectFault $true
} finally {
    $acl.RemoveAccessRule($rule)
    Set-Acl $roSubdir $acl
}

# ==============================================================================
# SUITE 9: RAPID CANCELLATION MID-CONVERSION
# ==============================================================================
Write-Host "`n=== SUITE 9: RAPID THREAD CANCELLATION MID-CONVERSION ===" -ForegroundColor Cyan
$s9Dir = Join-Path $WorkingDir "suite9_cancel"
if (-not (Test-Path $s9Dir)) { New-Item -ItemType Directory -Path $s9Dir -Force | Out-Null }

$largeBmpFile = Join-Path $s9Dir "large_canvas.bmp"
$largeBmp = New-Object System.Drawing.Bitmap(1600, 1600)
$largeG = [System.Drawing.Graphics]::FromImage($largeBmp)
$largeG.Clear([System.Drawing.Color]::DarkSlateBlue)
$largeG.Dispose()
$largeBmp.Save($largeBmpFile, [System.Drawing.Imaging.ImageFormat]::Bmp)
$largeBmp.Dispose()

$global:TotalTests++
$beforePartCount = (Get-ChildItem -Path $s9Dir -Filter "*.part.*" -Recurse -ErrorAction SilentlyContinue).Count
$beforeProcs = (Get-Process -Name WINWORD, EXCEL, ffmpeg, PDFREFLOW -ErrorAction SilentlyContinue).Count

$cancelAction = [Action]{
    $p = Start-Process -FilePath $TargetExe -ArgumentList "--headless", "--input", "`"$largeBmpFile`"", "--action", "png" -NoNewWindow -PassThru
    Start-Sleep -Milliseconds 20
    try { $p.Kill() } catch { }
}

$threadAlive = [OmniAdvTest.CancelHelper]::RunAndAbort($cancelAction, 20)
$afterPartCount = (Get-ChildItem -Path $s9Dir -Filter "*.part.*" -Recurse -ErrorAction SilentlyContinue).Count
$afterProcs = (Get-Process -Name WINWORD, EXCEL, ffmpeg, PDFREFLOW -ErrorAction SilentlyContinue).Count

$partLeaked = ($afterPartCount -gt $beforePartCount)
$procLeaked = ($afterProcs -gt $beforeProcs)
if ($partLeaked) { $global:DanglingPartFiles++ }
if ($procLeaked) { Clean-OrphanedProcesses }

if (-not $threadAlive -and -not $partLeaked -and -not $procLeaked) {
    $global:CleanlyCaught++
    Write-Host " [PASS] [Cancel] Rapid cancellation terminated cleanly without leaks." -ForegroundColor Green
} else {
    $global:Vulnerabilities++
    Write-Host " [LEAK] [Cancel] Rapid cancellation left leaks: hung=$threadAlive, part=$partLeaked, proc=$procLeaked" -ForegroundColor Magenta
}

$global:ReportLog += [PSCustomObject]@{
    Suite = "Cancel"
    TestName = "Rapid mid-conversion abort"
    Action = "png"
    Status = if (-not $threadAlive -and -not $partLeaked -and -not $procLeaked) { "CLEAN_PASS" } else { "RESOURCE_LEAK" }
    Success = $false
    Message = "Thread aborted mid-execution"
    DurationMs = 20
    PartLeaked = $partLeaked
    LockDangling = $false
    ProcLeaked = $procLeaked
    Unhandled = $false
}

# ==============================================================================
# SUITE 10: RESOURCE LEAK & DANGLING ARTIFACT AUDIT
# ==============================================================================
Write-Host "`n=== SUITE 10: RESOURCE LEAK & DANGLING ARTIFACT AUDIT ===" -ForegroundColor Cyan

# 1. Scan for dangling .part files anywhere in scratch directory
$allPartFiles = Get-ChildItem -Path $WorkingDir -Filter "*.part.*" -Recurse -ErrorAction SilentlyContinue
Write-Host " [AUDIT] Total dangling .part.* files found: $($allPartFiles.Count)" -ForegroundColor $(if ($allPartFiles.Count -eq 0) { "Green" } else { "Red" })
if ($allPartFiles.Count -gt 0) {
    foreach ($pf in $allPartFiles) {
        Write-Host "         -> Dangling file: $($pf.FullName)" -ForegroundColor Red
    }
}

# 2. Check for orphaned child processes
$orphans = Get-Process -Name WINWORD, EXCEL, ffmpeg, PDFREFLOW -ErrorAction SilentlyContinue
Write-Host " [AUDIT] Orphaned background processes: $($orphans.Count)" -ForegroundColor $(if ($orphans.Count -eq 0) { "Green" } else { "Red" })
if ($orphans.Count -gt 0) {
    foreach ($op in $orphans) {
        Write-Host "         -> Orphan process: $($op.ProcessName) (PID: $($op.Id))" -ForegroundColor Red
    }
    Clean-OrphanedProcesses
}

# 3. Check for dangling file locks by attempting full scratch cleanup
$scratchLocked = $false
try {
    Remove-Item $WorkingDir -Recurse -Force -ErrorAction Stop
    Write-Host " [AUDIT] Scratch directory deleted cleanly (0 dangling locks)." -ForegroundColor Green
} catch {
    $scratchLocked = $true
    $global:DanglingLocks++
    Write-Host " [AUDIT] FAILED to delete scratch directory (Dangling locks detected): $_" -ForegroundColor Red
}

# ==============================================================================
# HARNESS SUMMARY REPORT
# ==============================================================================
Write-Host "`n======================================================================" -ForegroundColor Cyan
Write-Host "             ADVERSARIAL FAULT-INJECTION TEST SUMMARY                 " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " Total Fault Tests Executed    : $global:TotalTests"
Write-Host " Cleanly Caught Faults (PASS)  : $global:CleanlyCaught" -ForegroundColor Green
Write-Host " False Successes / Vulns (FAIL): $global:Vulnerabilities" -ForegroundColor $(if ($global:Vulnerabilities -eq 0) { "Green" } else { "Red" })
Write-Host " Unhandled Exceptions (CRASH)  : $global:UnhandledExceptions" -ForegroundColor $(if ($global:UnhandledExceptions -eq 0) { "Green" } else { "Red" })
Write-Host " Dangling .part.* Leaks        : $global:DanglingPartFiles" -ForegroundColor $(if ($global:DanglingPartFiles -eq 0) { "Green" } else { "Red" })
Write-Host " Dangling File Locks           : $global:DanglingLocks" -ForegroundColor $(if ($global:DanglingLocks -eq 0) { "Green" } else { "Red" })
Write-Host " Orphaned Child Processes      : $global:OrphanedProcesses" -ForegroundColor $(if ($global:OrphanedProcesses -eq 0) { "Green" } else { "Red" })
Write-Host "======================================================================" -ForegroundColor Cyan

# Output JSON summary artifact
$summaryJson = @{
    TotalTests = $global:TotalTests
    CleanlyCaught = $global:CleanlyCaught
    Vulnerabilities = $global:Vulnerabilities
    UnhandledExceptions = $global:UnhandledExceptions
    DanglingPartFiles = $global:DanglingPartFiles
    DanglingLocks = $global:DanglingLocks
    OrphanedProcesses = $global:OrphanedProcesses
    Details = $global:ReportLog
} | ConvertTo-Json -Depth 4

$reportPath = Join-Path $PSScriptRoot "adversarial_audit_report.json"
[System.IO.File]::WriteAllText($reportPath, $summaryJson)
Write-Host "[REPORT] Full adversarial JSON report written to: $reportPath" -ForegroundColor Cyan

if ($global:Vulnerabilities -gt 0 -or $global:UnhandledExceptions -gt 0 -or $global:DanglingLocks -gt 0) {
    exit 2
} else {
    exit 0
}
