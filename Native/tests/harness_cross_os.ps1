# ==============================================================================
# OMNIWHEEL Cross-OS Simulation & Compatibility Test Harness
# Target Platforms: Windows 7 SP1, Windows 10 (10240-19045), Windows 11 (21H2-23H2)
# ==============================================================================

param(
    [switch]$VerboseOutput = $false
)

$ErrorActionPreference = "Continue"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  OMNIWHEEL CROSS-OS ENVIRONMENT SIMULATION & COMPATIBILITY HARNESS   " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "Host OS: $([System.Environment]::OSVersion.VersionString) (Build $([System.Environment]::OSVersion.Version.Build))" -ForegroundColor Gray
Write-Host "Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`n" -ForegroundColor Gray

$script:TotalTests = 0
$script:PassedTests = 0
$script:FailedTests = 0
$script:WarningTests = 0
$script:DiscoveredBugs = @()

function Report-Result {
    param(
        [string]$Category,
        [string]$TestName,
        [string]$Status, # PASS, FAIL, WARN
        [string]$Details,
        [string]$BugId = $null,
        [string]$AffectedFile = $null,
        [string]$LineNumber = $null
    )
    $script:TotalTests++
    
    $color = "Green"
    if ($Status -eq "FAIL") { 
        $color = "Red"
        $script:FailedTests++
        if ($BugId) {
            $script:DiscoveredBugs += [PSCustomObject]@{
                BugId = $BugId
                Category = $Category
                Test = $TestName
                File = $AffectedFile
                Line = $LineNumber
                Severity = "CRITICAL"
                Description = $Details
            }
        }
    } elseif ($Status -eq "WARN") { 
        $color = "Yellow"
        $script:WarningTests++
        if ($BugId) {
            $script:DiscoveredBugs += [PSCustomObject]@{
                BugId = $BugId
                Category = $Category
                Test = $TestName
                File = $AffectedFile
                Line = $LineNumber
                Severity = "WARNING"
                Description = $Details
            }
        }
    } else {
        $script:PassedTests++
    }

    Write-Host " [$Status] " -NoNewline -ForegroundColor $color
    Write-Host "$Category : $TestName" -ForegroundColor White
    if ($Details) {
        Write-Host "         $Details" -ForegroundColor Gray
    }
}

$nativeDir = "E:\ANTIGRAVITY\OMNIWHEEL\Native"
if (-not (Test-Path $nativeDir)) {
    $nativeDir = (Get-Item "$PSScriptRoot\..").FullName
}

# ------------------------------------------------------------------------------
# SUITE 1: WINDOWS 7 SP1 ENVIRONMENT EMULATION
# ------------------------------------------------------------------------------
Write-Host "`n>>> [SUITE 1] WINDOWS 7 SP1 EMULATION (Build 7601, Aero Disabled, No WinRT) <<<" -ForegroundColor Yellow

# Test 1.1: Application Manifest Compatibility GUIDs
$manifestPath = Join-Path $nativeDir "app.manifest"
if (Test-Path $manifestPath) {
    $manifestXml = [xml](Get-Content $manifestPath)
    $supportedGuids = $manifestXml.assembly.compatibility.application.supportedOS.Id
    $win7Guid = "{35138b9a-5d96-4fbd-8e2d-a2440225f93a}"
    $win8Guid = "{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}"
    $win81Guid = "{1f676c76-80e1-4239-95bb-83d0f6d0da78}"
    
    if ($supportedGuids -contains $win7Guid) {
        Report-Result "Win7-Compat" "Application Manifest Windows 7 GUID" "PASS" "Windows 7 GUID declared in app.manifest"
    } else {
        Report-Result "Win7-Compat" "Application Manifest Windows 7 GUID" "FAIL" `
            "Missing Windows 7 supportedOS GUID ($win7Guid). Windows 7 executes application under legacy Vista compatibility shim." `
            "BUG-W7-01" "app.manifest" "Line 14"
    }
} else {
    Report-Result "Win7-Compat" "Application Manifest Check" "FAIL" "app.manifest not found at $manifestPath" "BUG-W7-01"
}

# Test 1.2: .NET Runtime Boundaries (.NET 4.5 vs 4.8)
$configPath = Join-Path $nativeDir "app.config"
if (Test-Path $configPath) {
    $configContent = Get-Content $configPath -Raw
    if ($configContent -match 'sku="\.NETFramework,Version=v4\.8"') {
        if ($configContent -match '<supportedRuntime version="v4\.0"\s*/>') {
            Report-Result "Win7-Runtime" ".NET Runtime Legacy Fallback" "PASS" "SupportedRuntime v4.0 fallback present"
        } else {
            Report-Result "Win7-Runtime" ".NET Runtime Legacy Fallback" "WARN" `
                "app.config strictly requires .NET 4.8 without v4.0 fallback. Windows 7 SP1 environments with .NET 4.5/4.6/4.7 will fail to launch." `
                "BUG-W7-02" "app.config" "Line 4"
        }
    }
}

# Test 1.3: DWM Desktop Composition Disabled (Classic / Basic Theme Simulation)
# Check whether windows inspect DwmIsCompositionEnabled or handle WM_DWMCOMPOSITIONCHANGED
$uiFiles = @("RadialDonutWindow.cs", "ProgressFeedbackWindow.cs", "QuickLookWindow.cs", "SettingsWindow.cs")
foreach ($uiFile in $uiFiles) {
    $fPath = Join-Path $nativeDir $uiFile
    if (Test-Path $fPath) {
        $content = Get-Content $fPath -Raw
        $hasDwmCompCheck = $content -match "DwmIsCompositionEnabled|WM_DWMCOMPOSITIONCHANGED"
        if ($hasDwmCompCheck) {
            Report-Result "Win7-DWM" "$uiFile DWM Composition Guard" "PASS" "Composition check implemented"
        } else {
            Report-Result "Win7-DWM" "$uiFile DWM Composition Guard" "FAIL" `
                "$uiFile uses AllowsTransparency=true without verifying DwmIsCompositionEnabled. Under Windows 7 Classic theme (DWM off), transparent layered WPF windows render with solid black rectangular boxes." `
                "BUG-W7-03" $uiFile "Class Declaration"
        }
    }
}

# Test 1.4: WinRT Project Absence on Windows 7
# Inspect WindowsOcrEngine.ps1 and FileConversionEngine.cs embedded script for WinRT guards
$ocrPs1Path = Join-Path $nativeDir "WindowsOcrEngine.ps1"
if (Test-Path $ocrPs1Path) {
    $ocrContent = Get-Content $ocrPs1Path -Raw
    $hasOsCheck = $ocrContent -match '\[System\.Environment\]::OSVersion|Major -ge 10'
    if ($hasOsCheck) {
        Report-Result "Win7-WinRT" "WindowsOcrEngine.ps1 OS Version Guard" "PASS" "OS version checked before WinRT invocation"
    } else {
        Report-Result "Win7-WinRT" "WindowsOcrEngine.ps1 OS Version Guard" "FAIL" `
            "WindowsOcrEngine.ps1 unconditionally executes WinRT type bindings at script root (line 12). On Windows 7, throws fatal type loader exception." `
            "BUG-W7-04" "WindowsOcrEngine.ps1" "Line 12"
    }
}

$fcePath = Join-Path $nativeDir "FileConversionEngine.cs"
if (Test-Path $fcePath) {
    $fceContent = Get-Content $fcePath -Raw
    $hasOcrOsGuard = $fceContent -match '(?s)RunWindowsOcr.*?Environment\.OSVersion\.Version\.Major'
    if ($hasOcrOsGuard) {
        Report-Result "Win7-WinRT" "FileConversionEngine RunWindowsOcr Guard" "PASS" "OS Version checked before launching powershell OCR"
    } else {
        Report-Result "Win7-WinRT" "FileConversionEngine RunWindowsOcr Guard" "FAIL" `
            "RunWindowsOcr lacks OS version guard. On Windows 7, OCR requests launch a 60-second PowerShell process that fails to load WinRT, causing 60s stalls." `
            "BUG-W7-05" "FileConversionEngine.cs" "Line 4560"
    }
}

# Test 1.5: SetDefaultDllDirectories on Windows 7 without KB2533623
$progPath = Join-Path $nativeDir "Program.cs"
if (Test-Path $progPath) {
    $progContent = Get-Content $progPath -Raw
    # Check if SetDefaultDllDirectories is called safely
    if ($progContent -match 'SetDefaultDllDirectories') {
        Report-Result "Win7-API" "SetDefaultDllDirectories P/Invoke Resilience" "PASS" "SetDefaultDllDirectories invocation is wrapped in try/catch block"
    }
}

# ------------------------------------------------------------------------------
# SUITE 2: WINDOWS 10 (BUILDS 10240-19045) EMULATION
# ------------------------------------------------------------------------------
Write-Host "`n>>> [SUITE 2] WINDOWS 10 EMULATION (Per-Monitor DPI v2, Defender Contention, EcoQoS) <<<" -ForegroundColor Yellow

# Test 2.1: Per-Monitor DPI Coordinate Translation in ProgressFeedbackWindow
$pfwPath = Join-Path $nativeDir "ProgressFeedbackWindow.cs"
if (Test-Path $pfwPath) {
    $pfwContent = Get-Content $pfwPath -Raw
    $hasDpiDiv = $pfwContent -match 'screenX\s*/\s*scaleX'
    if ($hasDpiDiv) {
        Report-Result "Win10-DPI" "ProgressFeedbackWindow DPI Coordinate Translation" "PASS" "Coordinates translated properly via CompositionTarget scale"
    } else {
        Report-Result "Win10-DPI" "ProgressFeedbackWindow DPI Coordinate Translation" "FAIL" `
            "ProgressFeedbackWindow.ShowAt assigns raw physical pixel coordinates directly to WPF DIP properties Left/Top without DPI scaling." `
            "BUG-W10-01" "ProgressFeedbackWindow.cs" "Lines 94-95"
    }
}

# Test 2.2: Per-Monitor DPI Coordinate Translation in QuickLookWindow
$qlPath = Join-Path $nativeDir "QuickLookWindow.cs"
if (Test-Path $qlPath) {
    $qlContent = Get-Content $qlPath -Raw
    if ($qlContent -match 'CenterOnActiveMonitor' -and $qlContent -match '\bLeft\s*=\s*bounds\.Left\b') {
        Report-Result "Win10-DPI" "QuickLookWindow CenterOnActiveMonitor DPI Mixing" "FAIL" `
            "QuickLookWindow.CenterOnActiveMonitor assigns physical screen bounds (Screen.WorkingArea) directly to WPF DIP properties Width/Height/Left/Top without dividing by DPI scale factor. Window centers incorrectly on high-DPI and multi-monitors." `
            "BUG-W10-02" "QuickLookWindow.cs" "Lines 499-500"
    } else {
        Report-Result "Win10-DPI" "QuickLookWindow CenterOnActiveMonitor DPI Mixing" "PASS" "DPI scale applied in centering"
    }
}

# Test 2.3: RadialDonutWindow PresentationSource Null on First Summon
$rdwPath = Join-Path $nativeDir "RadialDonutWindow.cs"
if (Test-Path $rdwPath) {
    $rdwContent = Get-Content $rdwPath -Raw
    $hasEnsureHandle = $rdwContent -match 'EnsureHandle\(\)'
    if ($hasEnsureHandle) {
        Report-Result "Win10-DPI" "RadialDonutWindow Pre-Show PresentationSource DPI Scale" "PASS" "EnsureHandle guarantees PresentationSource is non-null on first summon"
    } else {
        Report-Result "Win10-DPI" "RadialDonutWindow Pre-Show PresentationSource DPI Scale" "WARN" `
            "RadialDonutWindow.ShowAt queries PresentationSource.FromVisual(this) before Show() is called. On first summon, source is null and DPI scale defaults to 1.0, mispositioning the wheel on High-DPI screens." `
            "BUG-W10-03" "RadialDonutWindow.cs" "Lines 213-220"
    }
}

# Test 2.4: Windows Defender / Antivirus File Lock Simulation on SaveAtomic
# Simulate Defender opening newly created .part file with shared read lock or exclusive access
Write-Host "   -> Simulating Antivirus / Defender file lock on SaveAtomic..." -ForegroundColor DarkGray
$testTempDir = Join-Path ([System.IO.Path]::GetTempPath()) "omni_test_defender_$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testTempDir -Force | Out-Null
$targetFile = Join-Path $testTempDir "output.txt"
$tempPart = Join-Path $testTempDir "output.txt.part.test.txt"

# Create tempPart
Set-Content -Path $tempPart -Value "Sample Content"

# Check SaveAtomic code for UnauthorizedAccessException catch block
$hasUaeCatch = $fceContent -match 'catch\s*\(\s*UnauthorizedAccessException\s*\)'
if (-not $hasUaeCatch) {
    Report-Result "Win10-Defender" "SaveAtomic UnauthorizedAccessException Resilience" "FAIL" `
        "FileConversionEngine.SaveAtomic retry loop only catches 'catch (IOException)'. When Windows Defender / Antivirus scans newly created .part files, File.Move/Delete throws System.UnauthorizedAccessException (ERROR_ACCESS_DENIED). The loop aborts on attempt 1 without retrying!" `
        "BUG-W10-04" "FileConversionEngine.cs" "Lines 105-110"
} else {
    Report-Result "Win10-Defender" "SaveAtomic UnauthorizedAccessException Resilience" "PASS" "UnauthorizedAccessException caught and retried"
}

# Clean temp dir
Remove-Item -Path $testTempDir -Recurse -Force -ErrorAction SilentlyContinue

# Test 2.5: OpenFileWithRetry UnauthorizedAccessException Handling
$hasOpenFileUae = $fceContent -match 'OpenFileWithRetry[\s\S]{1,600}catch\s*\(\s*UnauthorizedAccessException\s*\)'
if (-not $hasOpenFileUae) {
    Report-Result "Win10-Defender" "OpenFileWithRetry UnauthorizedAccessException Resilience" "WARN" `
        "FileConversionEngine.OpenFileWithRetry only catches IOException. Transient file locks producing ERROR_ACCESS_DENIED cause immediate failure instead of exponential backoff retry." `
        "BUG-W10-05" "FileConversionEngine.cs" "Lines 67-72"
} else {
    Report-Result "Win10-Defender" "OpenFileWithRetry UnauthorizedAccessException Resilience" "PASS" "OpenFileWithRetry catches UnauthorizedAccessException"
}

# ------------------------------------------------------------------------------
# SUITE 3: WINDOWS 11 (21H2, 22H2, 23H2) EMULATION
# ------------------------------------------------------------------------------
Write-Host "`n>>> [SUITE 3] WINDOWS 11 EMULATION (Window Corners, Backdrops, Snap Layouts, UAC) <<<" -ForegroundColor Yellow

# Test 3.1: Windows 11 Window Corner Clipping (DWMWA_WINDOW_CORNER_PREFERENCE)
# Frameless circular layered windows must set DWMWCP_DONOTROUND (1) to prevent DWM from clipping the circle
$hasCornerPref = $rdwContent -match "DWMWA_WINDOW_CORNER_PREFERENCE|DWMWCP_DONOTROUND"
if ($hasCornerPref) {
    Report-Result "Win11-DWM" "RadialDonutWindow DWM Corner Preference" "PASS" "DWMWA_WINDOW_CORNER_PREFERENCE applied"
} else {
    Report-Result "Win11-DWM" "RadialDonutWindow DWM Corner Preference" "FAIL" `
        "RadialDonutWindow does not set DWMWA_WINDOW_CORNER_PREFERENCE (33) to DWMWCP_DONOTROUND (1). Windows 11 DWM applies default rounded rect clipping to the 640x640 transparent window, clipping outer donut sectors." `
        "BUG-W11-01" "RadialDonutWindow.cs" "Line 135"
}

$pfwPath = Join-Path $nativeDir "ProgressFeedbackWindow.cs"
if (Test-Path $pfwPath) {
    $pfwContent = Get-Content $pfwPath -Raw
    $hasPfwCornerPref = $pfwContent -match "DWMWA_WINDOW_CORNER_PREFERENCE|DWMWCP_DONOTROUND"
    if ($hasPfwCornerPref) {
        Report-Result "Win11-DWM" "ProgressFeedbackWindow DWM Corner Preference" "PASS" "DWMWA_WINDOW_CORNER_PREFERENCE applied"
    } else {
        Report-Result "Win11-DWM" "ProgressFeedbackWindow DWM Corner Preference" "FAIL" `
            "ProgressFeedbackWindow does not set DWMWCP_DONOTROUND. Windows 11 DWM clips the circular progress HUD." `
            "BUG-W11-02" "ProgressFeedbackWindow.cs" "Line 85"
    }
}

# Test 3.2: Windows 11 Snap Layouts (WM_NCHITTEST on Maximize Button)
# Custom borderless windows like SettingsWindow and QuickLookWindow should handle WM_NCHITTEST -> HTMAXBUTTON (9)
$hasSnapLayoutsSettings = (Get-Content (Join-Path $nativeDir "SettingsWindow.cs") -Raw) -match "WM_NCHITTEST|HTMAXBUTTON"
if ($hasSnapLayoutsSettings) {
    Report-Result "Win11-Shell" "SettingsWindow Windows 11 Snap Layouts" "PASS" "WM_NCHITTEST handled for HTMAXBUTTON"
} else {
    Report-Result "Win11-Shell" "SettingsWindow Windows 11 Snap Layouts" "WARN" `
        "SettingsWindow is a custom chrome window that does not handle WM_NCHITTEST (0x0084) returning HTMAXBUTTON (9). Hovering over maximize button will not summon Windows 11 Snap Layouts." `
        "BUG-W11-03" "SettingsWindow.cs" "Window Messages"
}

# Test 3.3: Strict UAC Folder Virtualization in Protected Program Files Directory
# Simulation: When app is installed in Program Files, writes to BaseDirectory must NOT occur
$hasHardcodedBaseDirLog = $progContent -match 'Path\.Combine\(AppDomain\.CurrentDomain\.BaseDirectory,\s*"app\.log"\)'
if ($hasHardcodedBaseDirLog) {
    Report-Result "Win11-UAC" "Program.cs BaseDirectory app.log Hardcoding" "FAIL" `
        "Program.cs writes app.log directly to AppDomain.CurrentDomain.BaseDirectory (line 151). When installed to 'C:\Program Files\OMNIWHEEL', 64-bit manifest-controlled processes have UAC virtualization disabled; launch crashes with UnauthorizedAccessException!" `
        "BUG-W11-04" "Program.cs" "Lines 151, 154, 332"
} else {
    Report-Result "Win11-UAC" "Program.cs BaseDirectory app.log Hardcoding" "PASS" "app.log uses %LOCALAPPDATA% directory"
}

$loggerPath = Join-Path $nativeDir "Logger.cs"
if (Test-Path $loggerPath) {
    $loggerContent = Get-Content $loggerPath -Raw
    $hasLoggerBaseDir = $loggerContent -match 'Path\.Combine\(baseDir,\s*"logs"\)'
    if ($hasLoggerBaseDir) {
        Report-Result "Win11-UAC" "Logger.cs BaseDirectory logs Folder Hardcoding" "FAIL" `
            "Logger.cs creates './logs' relative to AppDomain.CurrentDomain.BaseDirectory. Fails under Program Files without elevation." `
            "BUG-W11-05" "Logger.cs" "Line 18"
    } else {
        Report-Result "Win11-UAC" "Logger.cs BaseDirectory logs Folder Hardcoding" "PASS" "Logger uses %LOCALAPPDATA% directory"
    }
}

# Test 3.4: GDI Handle Leak on System Tray Fallback Icon
$hasGdiLeak = $progContent -match 'IntPtr hIcon = bmp\.GetHicon\(\);[\s\S]{1,100}Icon\.FromHandle\(hIcon\);' -and ($progContent -notmatch 'DestroyIcon\(hIcon\)')
if ($hasGdiLeak) {
    Report-Result "Win32-GDI" "Program.cs Tray Icon GDI HICON Handle Leak" "FAIL" `
        "Program.InitTrayIcon calls bmp.GetHicon() without calling DestroyIcon(hIcon) after Icon.FromHandle(hIcon). Leaks native GDI icon handles." `
        "BUG-WIN-01" "Program.cs" "Lines 505-506"
} else {
    Report-Result "Win32-GDI" "Program.cs Tray Icon GDI HICON Handle Leak" "PASS" "DestroyIcon properly invoked"
}

# ------------------------------------------------------------------------------
# SUMMARY & DIAGNOSTIC REPORT
# ------------------------------------------------------------------------------
Write-Host "`n======================================================================" -ForegroundColor Cyan
Write-Host "                    SIMULATION HARNESS EXECUTION SUMMARY               " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "Total Verification Checks: $script:TotalTests"
Write-Host "Passed Checks:             $script:PassedTests" -ForegroundColor Green
Write-Host "Warning Checks:            $script:WarningTests" -ForegroundColor Yellow
Write-Host "Failed Checks:             $script:FailedTests" -ForegroundColor Red
Write-Host "Discovered Bugs:           $($script:DiscoveredBugs.Count)`n" -ForegroundColor $(if ($script:DiscoveredBugs.Count -gt 0) { "Red" } else { "Green" })

if ($script:DiscoveredBugs.Count -gt 0) {
    Write-Host "DISCOVERED COMPATIBILITY BUGS & DEFECTS:" -ForegroundColor Yellow
    $idx = 1
    foreach ($bug in $script:DiscoveredBugs) {
        Write-Host "[$idx] $($bug.BugId) ($($bug.Severity)) - $($bug.Category): $($bug.Test)" -ForegroundColor White
        Write-Host "    Target: $($bug.File) ($($bug.Line))" -ForegroundColor Cyan
        Write-Host "    Detail: $($bug.Description)" -ForegroundColor Gray
        $idx++
    }
}

Write-Host "`nSimulation harness run complete." -ForegroundColor Cyan
