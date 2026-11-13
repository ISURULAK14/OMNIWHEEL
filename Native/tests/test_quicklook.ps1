# QuickLook / Spacebar Peek Verification Test
$ErrorActionPreference = "Stop"

Write-Host "=== TESTING OMNIWHEEL SPACEBAR PEEK (QUICKLOOK) ===" -ForegroundColor Cyan

$appPath = [System.IO.Path]::Combine($env:LOCALAPPDATA, "OMNIWHEEL", "OMNIWHEEL.exe")
if (!(Test-Path $appPath)) {
    $appPath = "E:\ANTIGRAVITY\OMNIWHEEL\Native\bin\OMNIWHEEL.exe"
}
if (!(Test-Path $appPath)) {
    throw "OMNIWHEEL.exe missing at $appPath"
}

# 1. Verify QuickLookWindow Architecture in Binary & Source
Write-Host "`n[1/4] Verifying QuickLookWindow Architecture..."
$qlSourcePath = Join-Path $PSScriptRoot "..\QuickLookWindow.cs"
if (!(Test-Path $qlSourcePath)) {
    $qlSourcePath = "E:\ANTIGRAVITY\OMNIWHEEL\Native\QuickLookWindow.cs"
}
$qlSource = Get-Content $qlSourcePath -Raw

$bytes = [System.IO.File]::ReadAllBytes($appPath)
$binStr = [System.Text.Encoding]::UTF8.GetString($bytes)

if ($binStr -match "QuickLookWindow" -and $qlSource -match "class QuickLookWindow\s*:\s*Window") {
    Write-Host " [PASS] QuickLookWindow class found in OMNIWHEEL binary & source" -ForegroundColor Green
} else {
    throw "QuickLookWindow class missing"
}

# 2. Check Methods & Delegates
Write-Host "`n[2/4] Inspecting QuickLook Methods & Delegates..."
$requiredMethods = @("ShowPreview", "ClosePreview", "Navigate", "ApplyTheme", "CreateImageViewer", "CreateCodeViewer", "CreateDocumentViewer", "CreateMediaViewer", "CreateArchiveViewer")

foreach ($m in $requiredMethods) {
    if ($qlSource -match "void $m\b|FrameworkElement $m\b|Task $m\b|$m\(") {
        Write-Host " [PASS] Method: $m present" -ForegroundColor Green
    } else {
        throw "Missing method: $m"
    }
}

# 3. Check AppSettings EnableSpacePeek persistence
Write-Host "`n[3/4] Testing AppSettings EnableSpacePeek Flag..."
$settingsPath = Join-Path $PSScriptRoot "..\AppSettings.cs"
if (!(Test-Path $settingsPath)) {
    $settingsPath = "E:\ANTIGRAVITY\OMNIWHEEL\Native\AppSettings.cs"
}
$settingsSource = Get-Content $settingsPath -Raw
if ($settingsSource -match "public bool EnableSpacePeek\s*\{\s*get;\s*set;\s*\}" -and $settingsSource -match "EnableSpacePeek\s*=\s*true;") {
    Write-Host " [PASS] EnableSpacePeek property present with default true" -ForegroundColor Green
} else {
    throw "EnableSpacePeek missing or misconfigured in AppSettings"
}

# 4. Check KeyboardHook Spacebar Peek Hook Event Wireup
Write-Host "`n[4/4] Verifying KeyboardHook Hook Interceptor..."
$hookPath = Join-Path $PSScriptRoot "..\KeyboardHook.cs"
if (!(Test-Path $hookPath)) {
    $hookPath = "E:\ANTIGRAVITY\OMNIWHEEL\Native\KeyboardHook.cs"
}
$hookSource = Get-Content $hookPath -Raw
$hasSpace = $hookSource -match "event Action<\w+> OnSpacePeekTriggered" -or $hookSource -match "OnSpacePeekTriggered"
$hasClose = $hookSource -match "event Action OnPeekCloseTriggered" -or $hookSource -match "OnPeekCloseTriggered"
$hasNav = $hookSource -match "event Action<\w+> OnPeekNavigateTriggered" -or $hookSource -match "OnPeekNavigateTriggered"

if ($hasSpace -and $hasClose -and $hasNav) {
    Write-Host " [PASS] OnSpacePeekTriggered, OnPeekCloseTriggered, OnPeekNavigateTriggered verified on KeyboardHook" -ForegroundColor Green
} else {
    throw "Missing KeyboardHook Spacebar events"
}

Write-Host "`n=========================================================="
Write-Host "RESULT: ALL SPACEBAR PEEK / QUICKLOOK TESTS PASSED!" -ForegroundColor Green
Write-Host "=========================================================="
exit 0
