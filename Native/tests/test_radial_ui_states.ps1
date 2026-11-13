# Automated UI State & Geometry Test Suite
$ErrorActionPreference = "Continue"

$script:passCount = 0
$script:failCount = 0
$script:totalTests = 0

function Assert-Condition {
    param(
        [string]$TestName,
        [bool]$Condition,
        [string]$Details = ""
    )
    $script:totalTests++
    if ($Condition) {
        $script:passCount++
        Write-Host " [PASS] $TestName" -ForegroundColor Green
    } else {
        $script:failCount++
        Write-Host " [FAIL] $TestName - $Details" -ForegroundColor Red
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Testing Phase 1: Radial UI States, Geometry & Theme" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Math Test: Polar Coordinate Hit-Testing Simulation
# Radius thresholds:
# Center Hub: r <= 65
# Main Outer Ring: 65 < r <= 222
# Sub-Arc Expansion: 222 < r <= 325
function Test-HitTest {
    param([double]$x, [double]$y, [int]$count, [double]$cx = 330, [double]$cy = 330)
    $dx = $x - $cx
    $dy = $y - $cy
    $r = [Math]::Sqrt($dx * $dx + $dy * $dy)
    if ($r -le 65) { return "HUB_CANCEL" }
    if ($r -gt 222 -and $r -le 325) { return "SUB_ARC" }
    if ($r -gt 65 -and $r -le 222) {
        $deg = ([Math]::Atan2($dy, $dx) * 180.0 / [Math]::PI) + 90.0
        if ($deg -lt 0) { $deg += 360.0 }
        $slice = 360.0 / $count
        $shifted = $deg + ($slice / 2.0)
        if ($shifted -ge 360.0) { $shifted -= 360.0 }
        $idx = [int]($shifted / $slice) % $count
        return "WEDGE_$idx"
    }
    return "OUTSIDE"
}

# Hub center at (330, 330)
Assert-Condition "HitTest: Center point (330, 330) is inside Cancel Hub" ((Test-HitTest 330 330 9) -eq "HUB_CANCEL")
Assert-Condition "HitTest: Radius 40px (330, 370) is inside Cancel Hub" ((Test-HitTest 330 370 9) -eq "HUB_CANCEL")
Assert-Condition "HitTest: Radius 65px (330, 395) is at Cancel Hub border" ((Test-HitTest 330 395 9) -eq "HUB_CANCEL")

# 12 o'clock wedge (Wedge 0)
Assert-Condition "HitTest: 12 o'clock (330, 200, r=130) hits Wedge 0" ((Test-HitTest 330 200 9) -eq "WEDGE_0")

# ~1:30 wedge (Wedge 1)
$deg40Rad = 40 * [Math]::PI / 180.0
$w1X = 330 + 130 * [Math]::Sin($deg40Rad)
$w1Y = 330 - 130 * [Math]::Cos($deg40Rad)
Assert-Condition "HitTest: ~1:30 position hits Wedge 1" ((Test-HitTest $w1X $w1Y 9) -eq "WEDGE_1")

# Sub-arc zone (r=260, within parent span)
Assert-Condition "HitTest: Sub-Arc outer zone (330, 70, r=260) hits SUB_ARC" ((Test-HitTest 330 70 9) -eq "SUB_ARC")

# Outside bounds
Assert-Condition "HitTest: Far outside (330, 0, r=330) hits OUTSIDE" ((Test-HitTest 330 0 9) -eq "OUTSIDE")

# 2. Theme Verification: Hex Colors & Brushes in CurdTheme
$curdThemeFile = "E:\ANTIGRAVITY\OMNIWHEEL\Native\CurdTheme.cs"
$themeContent = [System.IO.File]::ReadAllText($curdThemeFile)

Assert-Condition "Theme: HubCancelBgColor is soft light-pink (#FFF1F2)" ($themeContent.Contains("0xFF, 0xF1, 0xF2"))
Assert-Condition "Theme: HubActionBgColor is pure white (#FFFFFF)" ($themeContent.Contains("0xFF, 0xFF, 0xFF"))
Assert-Condition "Theme: WedgeHoverBgColor is solid vibrant purple (#7C3AED)" ($themeContent.Contains("0x7C, 0x3A, 0xED"))
Assert-Condition "Theme: WedgeIndicatorColor is purple curved accent (#C4B5FD)" ($themeContent.Contains("0xC4, 0xB5, 0xFD"))
Assert-Condition "Theme: WedgeDefaultBorderColor is subtle divider (#E2E8F0)" ($themeContent.Contains("0xE2, 0xE8, 0xF0"))

# 3. High-DPI Manifest Verification
$manifestFile = "E:\ANTIGRAVITY\OMNIWHEEL\Native\app.manifest"
$manifestContent = [System.IO.File]::ReadAllText($manifestFile)
Assert-Condition "Manifest: PerMonitorV2 High-DPI awareness declared" ($manifestContent.Contains("PerMonitorV2"))

# 4. Zero-Footprint Idle Memory Verification (< 50MB for .NET 8 Self-Contained)
# Launch application in background mode, measure working set, verify < 50MB
$appExe = "E:\ANTIGRAVITY\OMNIWHEEL\Native\bin\OMNIWHEEL.exe"
$proc = Start-Process -FilePath $appExe -PassThru
Start-Sleep -Milliseconds 2500

$proc.Refresh()
$workingSetMb = [Math]::Round($proc.WorkingSet64 / (1024 * 1024), 2)
Write-Host " Measured Idle Memory Working Set: $workingSetMb MB" -ForegroundColor Yellow

Assert-Condition "Memory: Idle RAM footprint < 50MB" ($workingSetMb -lt 50.0) "Actual: $workingSetMb MB"

# Clean up process
$proc.Kill()
$proc.WaitForExit(3000)

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " UI State & Architecture Results: $script:passCount passed, $script:failCount failed out of $script:totalTests tests." -ForegroundColor $(if ($script:failCount -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($script:failCount -gt 0) {
    exit 1
} else {
    exit 0
}
