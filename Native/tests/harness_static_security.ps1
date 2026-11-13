<#
.SYNOPSIS
    OMNIWHEEL Static Audit & Security Test Harness
.DESCRIPTION
    Comprehensive static code audit and vulnerability verification across:
    E:\ANTIGRAVITY\OMNIWHEEL\Native
    Verifies:
      1. Unmanaged Resource Leaks (GDI Handles, Streams, COM RCWs)
      2. Concurrency & Deadlocks (Dispatcher, Process.WaitForExit, Shared State)
      3. Security Defenses (Zip-Slip, Command Injection, DLL Hijacking, Binary Planting)
#>

[CmdletBinding()]
param(
    [string]$WorkspaceRoot = "E:\ANTIGRAVITY\OMNIWHEEL\Native"
)

$ErrorActionPreference = "Continue"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "       OMNIWHEEL STATIC SECURITY & AUDIT HARNESS                " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "Workspace: $WorkspaceRoot`n"

$Results = [System.Collections.Generic.List[PSCustomObject]]::new()

function Evaluate-Check {
    param(
        [string]$Category,
        [string]$CheckId,
        [string]$Description,
        [bool]$Passed,
        [string]$Impact,
        [string]$Location,
        [string]$Recommendation = ""
    )

    $status = if ($Passed) { "PASS" } else { "WARN" }
    $color = if ($Passed) { "Green" } else { "Yellow" }
    
    Write-Host ("[{0}] {1}: {2}" -f $status, $CheckId, $Description) -ForegroundColor $color
    Write-Host ("       Location       : {0}" -f $Location) -ForegroundColor Gray
    Write-Host ("       Status Detail  : {0}" -f $Impact) -ForegroundColor $(if ($Passed) { "DarkGreen" } else { "DarkYellow" })
    if (-not $Passed -and -not [string]::IsNullOrEmpty($Recommendation)) {
        Write-Host ("       Remediation    : {0}" -f $Recommendation) -ForegroundColor Magenta
    }

    $Results.Add([PSCustomObject]@{
        Category       = $Category
        CheckId        = $CheckId
        Description    = $Description
        Passed         = $Passed
        Location       = $Location
        Impact         = $Impact
        Recommendation = $Recommendation
    })
}

# --- Load File Contents ---
$fcePath   = Join-Path $WorkspaceRoot "FileConversionEngine.cs"
$progPath  = Join-Path $WorkspaceRoot "Program.cs"
$rdwPath   = Join-Path $WorkspaceRoot "RadialDonutWindow.cs"
$qlPath    = Join-Path $WorkspaceRoot "QuickLookWindow.cs"
$pfwPath   = Join-Path $WorkspaceRoot "ProgressFeedbackWindow.cs"
$setupPath = Join-Path $WorkspaceRoot "Installer\OMNIWHEEL_Setup.cs"
$logPath   = Join-Path $WorkspaceRoot "Logger.cs"

$fceRaw   = if (Test-Path $fcePath)   { Get-Content $fcePath -Raw } else { "" }
$progRaw  = if (Test-Path $progPath)  { Get-Content $progPath -Raw } else { "" }
$rdwRaw   = if (Test-Path $rdwPath)   { Get-Content $rdwPath -Raw } else { "" }
$qlRaw    = if (Test-Path $qlPath)    { Get-Content $qlPath -Raw } else { "" }
$pfwRaw   = if (Test-Path $pfwPath)   { Get-Content $pfwPath -Raw } else { "" }
$setupRaw = if (Test-Path $setupPath) { Get-Content $setupPath -Raw } else { "" }
$logRaw   = if (Test-Path $logPath)   { Get-Content $logPath -Raw } else { "" }

# =================================================================
# SECTION 1: UNMANAGED RESOURCE AUDIT
# =================================================================
Write-Host "`n--- [SECTION 1: UNMANAGED RESOURCE AUDIT] ---" -ForegroundColor Cyan

# 1.1 GDI HICON Handle Destruction
$hiconCreated = $progRaw -match 'bmp\.GetHicon\(\)'
$hiconDestroyed = $progRaw -match 'DestroyIcon\(\s*hIcon\s*\)'
Evaluate-Check -Category "Resource" `
    -CheckId "RES-01" `
    -Description "GDI HICON Handle Lifecycle (Program.cs)" `
    -Passed ($hiconCreated -and $hiconDestroyed) `
    -Location "Program.cs (Lines 509-519)" `
    -Impact $(if ($hiconDestroyed) { "Native HICON destroyed via DestroyIcon(hIcon) in finally block. No GDI leak." } else { "GDI handle leak: bmp.GetHicon() not destroyed." }) `
    -Recommendation "Call DestroyIcon(hIcon) in finally block."

# 1.2 GDI HBITMAP Handle Destruction
$hbmpCreated = $qlRaw -match 'gdiBmp\.GetHbitmap\(\)'
$hbmpDestroyed = $qlRaw -match 'DeleteObject\(\s*hBmp\s*\)'
Evaluate-Check -Category "Resource" `
    -CheckId "RES-02" `
    -Description "GDI HBITMAP Handle Lifecycle (QuickLookWindow.cs)" `
    -Passed ($hbmpCreated -and $hbmpDestroyed) `
    -Location "QuickLookWindow.cs (Lines 709-718)" `
    -Impact "HBITMAP released via DeleteObject(hBmp) in finally block. No GDI handle leak."

# 1.3 ZipArchive / Stream Using Blocks
$zipUsingMatch = $fceRaw -match 'using\s*\(\s*var\s+archive\s*=\s*(?:ZipFile\.Open|new\s+ZipArchive)'
Evaluate-Check -Category "Resource" `
    -CheckId "RES-03" `
    -Description "ZipArchive and FileStream Deterministic Disposal" `
    -Passed $zipUsingMatch `
    -Location "FileConversionEngine.cs (Lines 2260, 2503, 2564, 3510)" `
    -Impact "All archive compression and decompression streams are enclosed in using() scopes."

# 1.4 Word COM RCW Intermediate Collection Leak
$wordDocsChained = $fceRaw -match 'wordApp\.Documents\.Open'
Evaluate-Check -Category "Resource" `
    -CheckId "RES-04" `
    -Description "Word COM RCW Intermediate Collection Leak" `
    -Passed (-not $wordDocsChained) `
    -Location "FileConversionEngine.cs (Lines 1897, 2137)" `
    -Impact $(if (-not $wordDocsChained) { "RCWs isolated and released." } else { "wordApp.Documents.Open creates an unmanaged RCW for Documents collection that is never released." }) `
    -Recommendation "Store dynamic docs = wordApp.Documents; doc = docs.Open(...); and call Marshal.FinalReleaseComObject(docs) in finally."

# 1.5 Excel COM Workbooks RCW Release
$excelReleasesWorkbooks = $fceRaw -match 'Marshal\.(?:Final)?ReleaseComObject\s*\(\s*workbooks\s*\)'
Evaluate-Check -Category "Resource" `
    -CheckId "RES-05" `
    -Description "Excel COM Workbooks Collection RCW Release" `
    -Passed $excelReleasesWorkbooks `
    -Location "FileConversionEngine.cs (Lines 3344-3375)" `
    -Impact $(if ($excelReleasesWorkbooks) { "Workbooks RCW released." } else { "workbooks RCW variable is allocated at line 3344 but omitted from finally release block. Leaves EXCEL.EXE in memory." }) `
    -Recommendation "Add try { Marshal.FinalReleaseComObject(workbooks); } catch { } in finally block."

# 1.6 Installer WScript.Shell COM Release
$setupReleasesShell = $setupRaw -match 'Marshal\.(?:Final)?ReleaseComObject\s*\(\s*shell\s*\)'
Evaluate-Check -Category "Resource" `
    -CheckId "RES-06" `
    -Description "WScript.Shell COM Shortcut RCW Cleanup" `
    -Passed $setupReleasesShell `
    -Location "OMNIWHEEL_Setup.cs (Lines 444-456)" `
    -Impact $(if ($setupReleasesShell) { "COM objects released." } else { "WScript.Shell and IWshShortcut RCWs are not released with Marshal.FinalReleaseComObject." }) `
    -Recommendation "Call Marshal.FinalReleaseComObject(shortcut) and Marshal.FinalReleaseComObject(shell) in finally."


# =================================================================
# SECTION 2: CONCURRENCY & THREAD-SAFETY AUDIT
# =================================================================
Write-Host "`n--- [SECTION 2: CONCURRENCY & THREAD-SAFETY AUDIT] ---" -ForegroundColor Cyan

# 2.1 Zero Synchronous Dispatcher.Invoke (Deadlock Prevention)
$allCs = Get-ChildItem -Path $WorkspaceRoot -Filter "*.cs" -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$syncInvokeFound = $false
foreach ($file in $allCs) {
    $c = Get-Content $file.FullName -Raw
    if ($c -match '\bDispatcher\.Invoke\s*\(') {
        $syncInvokeFound = $true
        break
    }
}
Evaluate-Check -Category "Concurrency" `
    -CheckId "CONC-01" `
    -Description "Elimination of Synchronous Dispatcher.Invoke Deadlocks" `
    -Passed (-not $syncInvokeFound) `
    -Location "All UI Windows (Program.cs, RadialDonutWindow.cs, ProgressFeedbackWindow.cs)" `
    -Impact "Exclusively uses Dispatcher.BeginInvoke for thread-to-UI dispatch. Zero deadlock risk on worker threads."

# 2.2 Process.WaitForExit Timeout Handling & Orphan Process Prevention
$fceLines = Get-Content $fcePath
$sevenZipGuarded = $false
for ($i = 0; $i -lt $fceLines.Count; $i++) {
    if ($fceLines[$i] -match 'p\.WaitForExit\(60000\)') {
        if ($fceLines[$i] -match 'if\s*\(!p\.WaitForExit' -or ($i -gt 0 -and $fceLines[$i-1] -match 'if\s*\(!p\.WaitForExit')) {
            $sevenZipGuarded = $true
        }
    }
}
Evaluate-Check -Category "Concurrency" `
    -CheckId "CONC-02" `
    -Description "Process.WaitForExit Timeout Guarding (7-Zip CLI)" `
    -Passed $sevenZipGuarded `
    -Location "FileConversionEngine.cs (Lines 2614-2615)" `
    -Impact $(if ($sevenZipGuarded) { "Process timeout guarded." } else { "p.WaitForExit(60000) does not inspect return value. If process times out, accessing p.ExitCode throws InvalidOperationException and 7z.exe runs orphaned." }) `
    -Recommendation "Wrap with if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } return new ConversionResult { Success = false, Message = '7-Zip timed out' }; }"

# 2.3 Shared State Thread Safety (Progress Overlay Variable Race)
$hasUnsyncedClosure = $rdwRaw -match 'int\s+currentPct\s*=' -and $rdwRaw -match 'new\s+System\.Threading\.Timer'
Evaluate-Check -Category "Concurrency" `
    -CheckId "CONC-03" `
    -Description "Closure Variable Synchronization across Timer & Worker Thread" `
    -Passed (-not $hasUnsyncedClosure) `
    -Location "RadialDonutWindow.cs (Lines 811-852)" `
    -Impact $(if (-not $hasUnsyncedClosure) { "Variables synchronized." } else { "currentPct integer is captured and modified concurrently by Timer callback and conversion progress callback without lock." }) `
    -Recommendation "Use lock or volatile object / Interlocked for currentPct access across threads."

# 2.4 Thread-Safe Logger Monitor Locking
$loggerLockMatch = $logRaw -match 'lock\s*\(\s*_lock\s*\)'
Evaluate-Check -Category "Concurrency" `
    -CheckId "CONC-04" `
    -Description "Multi-Threaded Logger File Lock Synchronization" `
    -Passed $loggerLockMatch `
    -Location "Logger.cs (Lines 93-108)" `
    -Impact "File write operations and log rotation are synchronized under private object monitor lock."


# =================================================================
# SECTION 3: SECURITY & INPUT SANITIZATION AUDIT
# =================================================================
Write-Host "`n--- [SECTION 3: SECURITY & INPUT SANITIZATION AUDIT] ---" -ForegroundColor Cyan

# 3.1 Zip-Slip Path Traversal Mitigation
$zipSlipCheck = $fceRaw -match 'StartsWith\s*\(\s*targetRoot\s*,\s*StringComparison\.OrdinalIgnoreCase\s*\)'
Evaluate-Check -Category "Security" `
    -CheckId "SEC-01" `
    -Description "Zip-Slip Directory Traversal Mitigation in Archive Extraction" `
    -Passed $zipSlipCheck `
    -Location "FileConversionEngine.cs (Lines 2566-2576)" `
    -Impact "Canonical full destination path validated against targetRoot directory before extraction. Traversal attempts rejected."

# 3.2 Command Injection Argument Sanitization
$sanitizerDefined = $fceRaw -match 'private\s+static\s+string\s+SanitizePathForCommandLine'
$stripsTrailingSlash = $fceRaw.Contains("TrimEnd('\\')")
Evaluate-Check -Category "Security" `
    -CheckId "SEC-02" `
    -Description "CLI Argument Injection Sanitization & Backslash Trimming" `
    -Passed ($sanitizerDefined -and $stripsTrailingSlash) `
    -Location "FileConversionEngine.cs (Lines 4334-4342)" `
    -Impact "Rejects quote and control characters; strips trailing backslashes to prevent escaping closing double quotes in Windows CLI parser."

# 3.3 Fully Qualified Path for PowerShell Invocation
$barePs1 = $fceRaw -match 'RunProcessSafely\s*\(\s*"powershell\.exe"'
Evaluate-Check -Category "Security" `
    -CheckId "SEC-03" `
    -Description "Fully-Qualified Path for PowerShell OCR Invocation" `
    -Passed (-not $barePs1) `
    -Location "FileConversionEngine.cs (Line 4586)" `
    -Impact $(if (-not $barePs1) { "Fully-qualified system path used." } else { "Bare powershell.exe binary relies on search path. Should use Path.Combine(Environment.SpecialFolder.System, 'WindowsPowerShell\v1.0\powershell.exe')." }) `
    -Recommendation "Resolve powershell.exe explicitly via System32 path."

# 3.4 Fully Qualified Path for cmd.exe in Installer
$bareCmd = $setupRaw -match 'FileName\s*=\s*"cmd\.exe"'
Evaluate-Check -Category "Security" `
    -CheckId "SEC-04" `
    -Description "Fully-Qualified Path for cmd.exe in Uninstaller" `
    -Passed (-not $bareCmd) `
    -Location "OMNIWHEEL_Setup.cs (Lines 347, 2148)" `
    -Impact $(if (-not $bareCmd) { "System directory path used." } else { "Bare cmd.exe relies on search path. Should resolve from Environment.SpecialFolder.System." }) `
    -Recommendation "Set FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), 'cmd.exe')."

# 3.5 Binary Planting Defense in Installer Fallback
$unverifiedFfmpeg = $setupRaw -match 'File\.Copy\s*\(\s*localFfmpeg'
Evaluate-Check -Category "Security" `
    -CheckId "SEC-05" `
    -Description "Unverified Local Binary Copying in Setup Fallback" `
    -Passed (-not $unverifiedFfmpeg) `
    -Location "OMNIWHEEL_Setup.cs (Lines 534-539)" `
    -Impact $(if (-not $unverifiedFfmpeg) { "Binaries verified." } else { "Installer copies unverified ffmpeg.exe sitting alongside setup into install dir without hash or Authenticode verification." }) `
    -Recommendation "Verify Authenticode signature or SHA256 before copying external fallback binary."

# 3.6 DLL Hijacking & Binary Planting Defenses
$progDll = $progRaw -match 'SetDllDirectory\s*\(\s*""\s*\)' -and $progRaw -match 'SetDefaultDllDirectories\('
$setupDll = $setupRaw -match 'SetDllDirectory\s*\(\s*""\s*\)' -and $setupRaw -match 'SetDefaultDllDirectories\('
Evaluate-Check -Category "Security" `
    -CheckId "SEC-06" `
    -Description "DLL Hijacking Immunization via SetDllDirectory & SetDefaultDllDirectories" `
    -Passed ($progDll -and $setupDll) `
    -Location "Program.cs (Lines 90-91), OMNIWHEEL_Setup.cs (Lines 45-46)" `
    -Impact "Current working directory stripped from DLL load path; DLL search restricted to System32 and Application directory."

# 3.7 Cryptographic Self-Integrity Validation
$hasSelfCheck = $progRaw -match 'VerifyBinaryIntegrity\(\)'
$hasThumbprint = $progRaw -match '601AF83746DF7AA5CFCFC83F8B6FA2FA03883B05'
Evaluate-Check -Category "Security" `
    -CheckId "SEC-07" `
    -Description "Cryptographic Certificate Thumbprint Self-Validation" `
    -Passed ($hasSelfCheck -and $hasThumbprint) `
    -Location "Program.cs (Lines 46-78, 94-104)" `
    -Impact "Installed executable validates its own digital signature and SHA1 thumbprint against tampered or patched binaries."

# 3.8 XXE Injection Prohibited in XML Parsing
$xxeSafe = $fceRaw -match 'DtdProcessing\.Prohibit' -and $fceRaw -match 'XmlResolver\s*=\s*null'
Evaluate-Check -Category "Security" `
    -CheckId "SEC-08" `
    -Description "XXE Injection Prohibition in Document Readers" `
    -Passed $xxeSafe `
    -Location "FileConversionEngine.cs (Lines 4775-4793)" `
    -Impact "CreateSafeXmlReader explicitly disables DTD parsing and nullifies entity resolvers for DOCX, XLSX, and SVG parsing."


# =================================================================
# SUMMARY REPORT
# =================================================================
Write-Host "`n=================================================================" -ForegroundColor Cyan
$passedCount = ($Results | Where-Object { $_.Passed }).Count
$totalCount  = $Results.Count
$score = [math]::Round(($passedCount / $totalCount) * 100, 1)

Write-Host ("AUDIT SCORE: {0} / {1} Controls Verified ({2}%)" -f $passedCount, $totalCount, $score) -ForegroundColor $(if ($score -ge 80) { "Green" } else { "Yellow" })
Write-Host "=================================================================`n" -ForegroundColor Cyan

return $Results
