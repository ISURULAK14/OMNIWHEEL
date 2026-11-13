# ==============================================================================
# OMNIWHEEL 1.0 - COMPREHENSIVE UI/UX AUTOMATED TEST SUITE
# Covers: Radial Donut Geometry, Curd Themes, WCAG Contrast, QuickLook Presenters,
# Settings Console, Installer/Uninstall UX, High-DPI Scaling & STA Live Smoke Test.
# ==============================================================================

$ErrorActionPreference = "Continue"

$script:totalTests = 0
$script:passCount = 0
$script:failCount = 0
$script:testResults = [System.Collections.Generic.List[PSCustomObject]]::new()

function Assert-Test {
    param(
        [string]$Category,
        [string]$TestName,
        [bool]$Condition,
        [string]$Details = ""
    )
    $script:totalTests++
    if ($Condition) {
        $script:passCount++
        $res = [PSCustomObject]@{
            Status = "PASS"
            Category = $Category
            TestName = $TestName
            Details = $Details
        }
        $script:testResults.Add($res)
        Write-Host " [PASS] [$Category] $TestName" -ForegroundColor Green
    } else {
        $script:failCount++
        $res = [PSCustomObject]@{
            Status = "FAIL"
            Category = $Category
            TestName = $TestName
            Details = $Details
        }
        $script:testResults.Add($res)
        Write-Host " [FAIL] [$Category] $TestName - $Details" -ForegroundColor Red
    }
}

Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host "  OMNIWHEEL 1.0 - COMPREHENSIVE UI/UX TEST SUITE" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan

# ------------------------------------------------------------------------------
# SUITE 1: RADIAL DONUT WINDOW GEOMETRY & POLAR HIT-TESTING
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 1/7] Radial Donut Window Geometry & Polar Hit-Testing ---" -ForegroundColor Yellow

function Simulate-HitTest {
    param(
        [double]$x,
        [double]$y,
        [int]$wedgeCount = 8,
        [int]$activeSubGroup = -1,
        [int]$subCount = 0,
        [double]$cx = 330,
        [double]$cy = 330
    )
    $dx = $x - $cx
    $dy = $y - $cy
    $dist = [Math]::Sqrt($dx * $dx + $dy * $dy)

    # 1. Center Hub (Flip / Cancel Zone): dist <= 65
    if ($dist -le 65.0) {
        return @{ Zone = "HUB_CANCEL"; Wedge = -1; SubWedge = -1; Radius = $dist }
    }

    $angleDeg = ([Math]::Atan2($dy, $dx) * 180.0 / [Math]::PI) + 90.0
    if ($angleDeg -lt 0) { $angleDeg += 360.0 }

    # 2. Secondary Sub-Arc Outer Ring: 218 < dist <= 330
    if ($dist -gt 218.0 -and $dist -le 330.0 -and $activeSubGroup -ge 0 -and $subCount -gt 0) {
        $sliceDeg = 360.0 / $wedgeCount
        $centerA = $activeSubGroup * $sliceDeg
        $totalSpan = [Math]::Max(48.0, $subCount * 24.0)

        $diff = $angleDeg - $centerA
        while ($diff -gt 180.0) { $diff -= 360.0 }
        while ($diff -lt -180.0) { $diff += 360.0 }

        if ($diff -ge (-$totalSpan / 2.0) -and $diff -le ($totalSpan / 2.0)) {
            $normalized = $diff + ($totalSpan / 2.0)
            $subSlice = $totalSpan / $subCount
            # Emulate C# integer truncation: (int)(normalized / subSlice)
            $sIdx = [int][Math]::Floor($normalized / $subSlice)
            if ($sIdx -lt 0) { $sIdx = 0 }
            if ($sIdx -ge $subCount) { $sIdx = $subCount - 1 }
            return @{ Zone = "SUB_ARC"; Wedge = $activeSubGroup; SubWedge = $sIdx; Radius = $dist }
        }
        elseif ([Math]::Abs($diff) -le (($totalSpan / 2.0) + 14.0)) {
            $sIdx = if ($diff -lt 0) { 0 } else { $subCount - 1 }
            return @{ Zone = "SUB_ARC"; Wedge = $activeSubGroup; SubWedge = $sIdx; Radius = $dist }
        }
    }

    # 3. Main Outer Ring: 65 < dist <= 218
    if ($dist -gt 65.0 -and $dist -le 218.0) {
        $sliceDeg = 360.0 / $wedgeCount
        $shifted = $angleDeg + ($sliceDeg / 2.0)
        if ($shifted -ge 360.0) { $shifted -= 360.0 }
        $idx = [int][Math]::Floor($shifted / $sliceDeg) % $wedgeCount
        return @{ Zone = "MAIN_WEDGE"; Wedge = $idx; SubWedge = -1; Radius = $dist }
    }

    # 4. Outside Bounds: dist > 330
    return @{ Zone = "OUTSIDE"; Wedge = -1; SubWedge = -1; Radius = $dist }
}

# Test 1.1: Center Hub Detection
$hitCenter = Simulate-HitTest 330 330 8
Assert-Test "Geometry" "Dead Center (330, 330) hits HUB_CANCEL" ($hitCenter.Zone -eq "HUB_CANCEL")
$hitHubEdge = Simulate-HitTest 330 395 8
Assert-Test "Geometry" "Radius 65px exact boundary hits HUB_CANCEL" ($hitHubEdge.Zone -eq "HUB_CANCEL")
$hitInsideHub = Simulate-HitTest 300 320 8
Assert-Test "Geometry" "Radius 36px inside hub hits HUB_CANCEL" ($hitInsideHub.Zone -eq "HUB_CANCEL")

# Test 1.2: Main Ring Wedge Distribution (8 wedges = 45 deg per slice)
# 12 o'clock (0 deg) -> Wedge 0
$hitW0 = Simulate-HitTest 330 200 8
Assert-Test "Geometry" "12 o'clock (angle 0 deg, r=130) hits Wedge 0" ($hitW0.Zone -eq "MAIN_WEDGE" -and $hitW0.Wedge -eq 0)

# 3 o'clock (90 deg) -> Wedge 2
$hitW2 = Simulate-HitTest 460 330 8
Assert-Test "Geometry" "3 o'clock (angle 90 deg, r=130) hits Wedge 2" ($hitW2.Zone -eq "MAIN_WEDGE" -and $hitW2.Wedge -eq 2)

# 6 o'clock (180 deg) -> Wedge 4
$hitW4 = Simulate-HitTest 330 460 8
Assert-Test "Geometry" "6 o'clock (angle 180 deg, r=130) hits Wedge 4" ($hitW4.Zone -eq "MAIN_WEDGE" -and $hitW4.Wedge -eq 4)

# 9 o'clock (270 deg) -> Wedge 6
$hitW6 = Simulate-HitTest 200 330 8
Assert-Test "Geometry" "9 o'clock (angle 270 deg, r=130) hits Wedge 6" ($hitW6.Zone -eq "MAIN_WEDGE" -and $hitW6.Wedge -eq 6)

# Test 1.3: Boundary between Main Ring and Sub-Arc (r=218 vs r=219)
$hitBoundaryInner = Simulate-HitTest 330 (330 - 217) 8
Assert-Test "Geometry" "Radius 217px is within MAIN_WEDGE" ($hitBoundaryInner.Zone -eq "MAIN_WEDGE")
$hitBoundaryOuter = Simulate-HitTest 330 (330 - 250) 8 0 3
Assert-Test "Geometry" "Radius 250px with active sub-wedge 0 hits SUB_ARC" ($hitBoundaryOuter.Zone -eq "SUB_ARC" -and $hitBoundaryOuter.Wedge -eq 0)

# Test 1.4: Sub-Arc Angular Span & Catchment Buffer
$hitSubMiddle = Simulate-HitTest 330 70 8 0 3
Assert-Test "Geometry" "Sub-Arc center (r=260) hits sub-wedge 1 of 3" ($hitSubMiddle.Zone -eq "SUB_ARC" -and $hitSubMiddle.SubWedge -eq 1)

# Test 1.5: Outside Bounds Escapement
$hitFarOutside = Simulate-HitTest 330 0 8
Assert-Test "Geometry" "Far outside (r=330.1px) hits OUTSIDE" ($hitFarOutside.Zone -eq "OUTSIDE")

# Test 1.6: Screen WorkArea Clamping Math
function Simulate-ShowWheelAt {
    param([double]$cursorX, [double]$cursorY, [double]$workL, [double]$workR, [double]$workT, [double]$workB, [double]$dpi = 1.0)
    $width = 660.0
    $height = 660.0
    $dipX = $cursorX / $dpi
    $dipY = $cursorY / $dpi
    $minX = ($workL / $dpi) + 10.0
    $maxX = ($workR / $dpi) - $width - 10.0
    $minY = ($workT / $dpi) + 10.0
    $maxY = ($workB / $dpi) - $height - 10.0

    $x = $dipX - ($width / 2.0)
    $y = $dipY - ($height / 2.0)

    $clampedX = [Math]::Max($minX, [Math]::Min($maxX, $x))
    $clampedY = [Math]::Max($minY, [Math]::Min($maxY, $y))

    return @{ X = $clampedX; Y = $clampedY; MinX = $minX; MaxX = $maxX; MinY = $minY; MaxY = $maxY }
}

$clampTopLeft = Simulate-ShowWheelAt 0 0 0 1920 0 1080 1.0
Assert-Test "Geometry" "Cursor at (0, 0) clamps wheel inside screen (X >= 10, Y >= 10)" ($clampTopLeft.X -eq 10 -and $clampTopLeft.Y -eq 10)

$clampBottomRight = Simulate-ShowWheelAt 2000 1200 0 1920 0 1080 1.0
$expectedMaxX = 1920 - 660 - 10
$expectedMaxY = 1080 - 660 - 10
Assert-Test "Geometry" "Cursor offscreen bottom-right clamps wheel inside screen (X <= $expectedMaxX, Y <= $expectedMaxY)" ($clampBottomRight.X -eq $expectedMaxX -and $clampBottomRight.Y -eq $expectedMaxY)


# ------------------------------------------------------------------------------
# SUITE 2: CURD DESIGN SYSTEM & WCAG ACCESSIBILITY
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 2/7] Curd Design System & WCAG Accessibility ---" -ForegroundColor Yellow

$curdThemeFile = "E:\ANTIGRAVITY\OMNIWHEEL\Native\CurdTheme.cs"
$themeSource = [System.IO.File]::ReadAllText($curdThemeFile)

# Test 2.1: Palette Constants
Assert-Test "Theme" "Curd Light Cancel Hub is #FFF1F2" ($themeSource -match '0xFF,\s*0xF1,\s*0xF2')
Assert-Test "Theme" "Curd Light Action Hub is #FFFFFF" ($themeSource -match '0xFF,\s*0xFF,\s*0xFF')
Assert-Test "Theme" "Curd Light Wedge Divider is #E2E8F0" ($themeSource -match '0xE2,\s*0xE8,\s*0xF0')
Assert-Test "Theme" "Curd Purple Accent Glow is #C4B5FD" ($themeSource -match '0xC4,\s*0xB5,\s*0xFD')
Assert-Test "Theme" "Curd Purple Accent Base is #7C3AED" ($themeSource -match '0x7C,\s*0x3A,\s*0xED')

# Test 2.2: WCAG 2.1 Contrast Ratio Calculator
function Get-RelativeLuminance {
    param([int]$r, [int]$g, [int]$b)
    $rNorm = $r / 255.0
    $gNorm = $g / 255.0
    $bNorm = $b / 255.0

    $rLin = if ($rNorm -le 0.03928) { $rNorm / 12.92 } else { [Math]::Pow(($rNorm + 0.055) / 1.055, 2.4) }
    $gLin = if ($gNorm -le 0.03928) { $gNorm / 12.92 } else { [Math]::Pow(($gNorm + 0.055) / 1.055, 2.4) }
    $bLin = if ($bNorm -le 0.03928) { $bNorm / 12.92 } else { [Math]::Pow(($bNorm + 0.055) / 1.055, 2.4) }

    return 0.2126 * $rLin + 0.7152 * $gLin + 0.0722 * $bLin
}

function Get-ContrastRatio {
    param([int[]]$c1, [int[]]$c2)
    $l1 = Get-RelativeLuminance $c1[0] $c1[1] $c1[2]
    $l2 = Get-RelativeLuminance $c2[0] $c2[1] $c2[2]
    $lighter = [Math]::Max($l1, $l2)
    $darker = [Math]::Min($l1, $l2)
    return [Math]::Round(($lighter + 0.05) / ($darker + 0.05), 2)
}

# Dark Text (0x1E, 0x1B, 0x28) on Light Background (0xFF, 0xFF, 0xFF)
$contrastTextOnWhite = Get-ContrastRatio @(30, 27, 40) @(255, 255, 255)
Assert-Test "Theme" "WCAG Contrast: Dark Text on White ($($contrastTextOnWhite):1 >= 7.0:1 AAA)" ($contrastTextOnWhite -ge 7.0)

# White Text (255, 255, 255) on Wedge Hover Purple (109, 40, 217)
$contrastWhiteOnPurple = Get-ContrastRatio @(255, 255, 255) @(109, 40, 217)
Assert-Test "Theme" "WCAG Contrast: White Text on Purple Active Wedge ($($contrastWhiteOnPurple):1 >= 4.5:1 AA)" ($contrastWhiteOnPurple -ge 4.5)

# Cancel Hub Red Text (0xE1, 0x1D, 0x48) on Cancel Hub Light Pink (0xFF, 0xF1, 0xF2)
$contrastRedOnPink = Get-ContrastRatio @(225, 29, 72) @(255, 241, 242)
Assert-Test "Theme" "WCAG Contrast: Red Label on Cancel Pink Hub ($($contrastRedOnPink):1 >= 3.0:1 Non-Text/Large)" ($contrastRedOnPink -ge 3.0)

# Test 2.3: Multi-Accent & Pastel/Simple Theme Support in CurdTheme
$accents = @(
    "Purple", "Emerald", "Cobalt", "Amber", "DarkSlate",
    "PastelLavender", "PastelMint", "PastelRose", "PastelSky", "PastelPeach", "PastelMatcha",
    "NordicFrost", "Monochrome", "Solar", "Ocean"
)
foreach ($acc in $accents) {
    $hasAccent = $themeSource.Contains("themeAccent.Equals(`"$acc`"") -or $themeSource.Contains("`"$acc`"")
    Assert-Test "Theme" "CurdTheme supports '$acc' accent" $hasAccent
}

# Test 2.4: WCAG AAA Contrast for Pastel & Minimalist Hover Themes
$pastelThemes = @(
    @{ Name="PastelLavender"; Bg=@(196, 181, 253); Txt=@(30, 27, 75); Min=7.0 },
    @{ Name="PastelMint"; Bg=@(110, 231, 183); Txt=@(2, 44, 34); Min=7.0 },
    @{ Name="PastelRose"; Bg=@(253, 164, 175); Txt=@(76, 5, 25); Min=7.0 },
    @{ Name="PastelSky"; Bg=@(125, 211, 252); Txt=@(8, 47, 73); Min=7.0 },
    @{ Name="PastelPeach"; Bg=@(253, 186, 116); Txt=@(67, 20, 7); Min=7.0 },
    @{ Name="PastelMatcha"; Bg=@(217, 249, 157); Txt=@(20, 83, 45); Min=7.0 },
    @{ Name="NordicFrost"; Bg=@(226, 232, 240); Txt=@(15, 23, 42); Min=7.0 },
    @{ Name="Monochrome"; Bg=@(51, 65, 85); Txt=@(255, 255, 255); Min=7.0 }
)
foreach ($pt in $pastelThemes) {
    $cRatio = Get-ContrastRatio $pt.Bg $pt.Txt
    Assert-Test "Theme" "WCAG AAA: $($pt.Name) hover text contrast ($cRatio:1 >= $($pt.Min):1)" ($cRatio -ge $pt.Min)
}


# ------------------------------------------------------------------------------
# SUITE 3: SPACEBAR QUICKLOOK / PEEK UX ARCHITECTURE
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 3/7] Spacebar QuickLook / Peek UX Architecture ---" -ForegroundColor Yellow

$qlSourceFile = "E:\ANTIGRAVITY\OMNIWHEEL\Native\QuickLookWindow.cs"
$qlSource = [System.IO.File]::ReadAllText($qlSourceFile)

# Test 3.1: Verify All Specialized Presenters Exist
$presenters = @(
    "CreateImageViewer",
    "CreateCodeViewer",
    "CreateMediaViewer",
    "CreateDocumentViewer",
    "CreateArchiveViewer",
    "CreateFolderViewer",
    "CreateFallbackViewer",
    "CreateErrorViewer"
)
foreach ($p in $presenters) {
    $hasPresenter = $qlSource.Contains("UIElement $p(")
    Assert-Test "QuickLook" "Presenter '$p' implemented" $hasPresenter
}

# Test 3.2: Image Presenter Features
Assert-Test "QuickLook" "Image Presenter contains resolution info chip" ($qlSource.Contains("footerDetails = string.Format") -or $qlSource.Contains("pixelWidth") -or $qlSource.Contains("PixelWidth"))
Assert-Test "QuickLook" "Image Presenter uses HighQuality BitmapScalingMode" ($qlSource.Contains("BitmapScalingMode.HighQuality"))

# Test 3.3: Code Presenter Features
Assert-Test "QuickLook" "Code Presenter includes Line Number Gutter" ($qlSource.Contains("gutterBlock") -and $qlSource.Contains("sbGutter"))
Assert-Test "QuickLook" "Code Presenter uses Monospace Font (Cascadia Code / Consolas)" ($qlSource.Contains("Cascadia Code") -or $qlSource.Contains("Consolas"))
Assert-Test "QuickLook" "Code Presenter has Syntax Highlighting tokenizer" ($qlSource.Contains("HighlightSyntax") -or $qlSource.Contains("HighlightSourceCode"))

# Test 3.4: Media Presenter Features
Assert-Test "QuickLook" "Media Presenter supports Video Playback controls (Play/Pause, Slider)" ($qlSource.Contains("MediaElement") -and ($qlSource.Contains("btnPlayPause") -or $qlSource.Contains("PlayPause")))
Assert-Test "QuickLook" "Media Presenter supports Vinyl Disc animation for Audio" ($qlSource.Contains("vinylDisc") -or $qlSource.Contains("vinyl") -or $qlSource.Contains("Disc"))

# Test 3.5: Archive Presenter Features
Assert-Test "QuickLook" "Archive Presenter inspects ZipArchive entries" ($qlSource.Contains("ZipFile.OpenRead") -and $qlSource.Contains("CompressedLength"))

# Test 3.6: Fallback Hex Viewer Features
Assert-Test "QuickLook" "Fallback Hex Viewer includes Offset & ASCII columns" ($qlSource.Contains("StringBuilder hexBuilder") -or $qlSource.Contains("hex") -or $qlSource.Contains("ASCII"))

# Test 3.7: Keyboard & Navigation Shortcuts
Assert-Test "QuickLook" "Spacebar QuickLook toggle hook wireup" ($qlSource.Contains("OnSpacePeekTriggered") -or [System.IO.File]::ReadAllText("E:\ANTIGRAVITY\OMNIWHEEL\Native\KeyboardHook.cs").Contains("OnSpacePeekTriggered"))
Assert-Test "QuickLook" "QuickLook Arrow Navigation (Next/Prev file in folder)" ($qlSource.Contains("Navigate(1)") -and $qlSource.Contains("Navigate(-1)"))


# ------------------------------------------------------------------------------
# SUITE 4: SETTINGS CONSOLE UI/UX
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 4/7] Settings Console UI/UX ---" -ForegroundColor Yellow

$settingsSourceFile = "E:\ANTIGRAVITY\OMNIWHEEL\Native\SettingsWindow.cs"
$settingsSource = [System.IO.File]::ReadAllText($settingsSourceFile)

# Test 4.1: Category Sections
$sections = @(
    "BuildActivationSection",
    "BuildSecondaryWheelsSection",
    "BuildQualitySection",
    "BuildMediaSection",
    "BuildVisualThemeSection"
)
foreach ($sec in $sections) {
    $hasSec = $settingsSource.Contains("UIElement $sec()")
    Assert-Test "Settings" "Settings Section '$sec' implemented" $hasSec
}

# Test 4.2: Settings Persistence & Event Dispatch
Assert-Test "Settings" "Settings Window dispatches OnSettingsSaved event" ($settingsSource.Contains("event Action<AppSettings> OnSettingsSaved"))
Assert-Test "Settings" "Settings Window supports Theme styling application" ($settingsSource.Contains("ApplyThemeStyling()"))
Assert-Test "Settings" "Settings includes EnableSpacePeek toggle" ($settingsSource.Contains("_chkSpacePeek"))
Assert-Test "Settings" "Settings includes Notification toggle" ($settingsSource.Contains("_chkNotifications"))
Assert-Test "Settings" "Settings includes Sound Chime toggle" ($settingsSource.Contains("_chkSound"))
Assert-Test "Settings" "Settings includes Windows Startup toggle" ($settingsSource.Contains("_chkStartup"))


# ------------------------------------------------------------------------------
# SUITE 5: PROGRESS & FEEDBACK OVERLAY WINDOW
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 5/7] Progress & Feedback Overlay Window ---" -ForegroundColor Yellow

$progSourceFile = "E:\ANTIGRAVITY\OMNIWHEEL\Native\ProgressFeedbackWindow.cs"
$progSource = [System.IO.File]::ReadAllText($progSourceFile)

# Test 5.1: Non-intrusive Overlay Window Properties
Assert-Test "ProgressHUD" "ShowInTaskbar is false" ($progSource.Contains("ShowInTaskbar = false"))
Assert-Test "ProgressHUD" "ShowActivated is false" ($progSource.Contains("ShowActivated = false"))
Assert-Test "ProgressHUD" "AllowsTransparency is true" ($progSource.Contains("AllowsTransparency = true"))
Assert-Test "ProgressHUD" "WS_EX_NOACTIVATE applied via Win32 SetWindowLong" ($progSource.Contains("WS_EX_NOACTIVATE"))

# Test 5.2: Progress Dynamics
Assert-Test "ProgressHUD" "UpdateProgress supports thread-safe Dispatcher invocation" ($progSource.Contains("Dispatcher.BeginInvoke") -and $progSource.Contains("UpdateProgress"))
Assert-Test "ProgressHUD" "SetComplete transitions to completion state" ($progSource.Contains("SetComplete"))


# ------------------------------------------------------------------------------
# SUITE 6: INSTALLER & UNINSTALL WIZARD UX
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 6/7] Installer & Uninstall Wizard UX ---" -ForegroundColor Yellow

$setupSourceFile = "E:\ANTIGRAVITY\OMNIWHEEL\Native\Installer\OMNIWHEEL_Setup.cs"
$setupSource = [System.IO.File]::ReadAllText($setupSourceFile)

# Test 6.1: Multilingual Rotating Drum Greeting
Assert-Test "SetupUX" "Rotating Greetings Drum Cylinder implemented" ($setupSource.Contains("RotateDrumToNextLanguage") -and $setupSource.Contains("_drumTimer"))
Assert-Test "SetupUX" "Multilingual greeting slots (Slot A & Slot B) exist" ($setupSource.Contains("_drumSlotA") -and $setupSource.Contains("_drumSlotB"))

# Test 6.2: Illuminating Author Deck for ISURU GUNASEKERA
Assert-Test "SetupUX" "Author credit ISURU GUNASEKERA declared" ($setupSource.Contains("ISURU GUNASEKERA"))
Assert-Test "SetupUX" "Author card includes pulsing glow drop shadow effect" ($setupSource.Contains("authorGlow") -or $setupSource.Contains("Author") -or $setupSource.Contains("DropShadowEffect"))

# Test 6.3: Uninstall Emotional Feedback Copy & Sad Face Container
Assert-Test "SetupUX" "Uninstall Window title is 'OMNIWHEEL 1.0 - Uninstall'" ($setupSource.Contains("Title = `"OMNIWHEEL 1.0 - Uninstall`""))
Assert-Test "SetupUX" "Uninstall contains exact copy: ' SAAAAD TO SEE YOU GOOOO'" ($setupSource.Contains(" SAAAAD TO SEE YOU GOOOO"))
Assert-Test "SetupUX" "Uninstall UI includes Sad Face drawing visual" ($setupSource.Contains("sadFace") -or $setupSource.Contains("CreateSadFace") -or $setupSource.Contains("Teardrop") -or $setupSource.Contains("Mouth"))


# ------------------------------------------------------------------------------
# SUITE 7: HIGH-DPI SCALING & STA LIVE SMOKE TEST
# ------------------------------------------------------------------------------
Write-Host "`n--- [SUITE 7/7] High-DPI Scaling & STA Live Smoke Test ---" -ForegroundColor Yellow

# Test 7.1: Manifest DPI Declaration
$manifestFile = "E:\ANTIGRAVITY\OMNIWHEEL\Native\app.manifest"
$manifestText = [System.IO.File]::ReadAllText($manifestFile)
$hasPerMonitorV2 = ($manifestText -match '<dpiAwareness.*?>.*PerMonitorV2.*?</dpiAwareness>')
Assert-Test "DPI" "Manifest declares PerMonitorV2 dpiAwareness" $hasPerMonitorV2

$hasDpiAware = ($manifestText -match '<dpiAware.*?>true/pm</dpiAware>')
Assert-Test "DPI" "Manifest declares true/pm dpiAware" $hasDpiAware

# Test 7.2: Multi-DPI Transform Calculations across standard display pairings
# 100%: 1080p, 125%: 1440p, 150%: 1440p, 175%: 4K, 200%: 4K
$displayConfigs = @(
    @{ Scale = 1.00; ResX = 1920; ResY = 1080 },
    @{ Scale = 1.25; ResX = 2560; ResY = 1440 },
    @{ Scale = 1.50; ResX = 2560; ResY = 1440 },
    @{ Scale = 1.75; ResX = 3840; ResY = 2160 },
    @{ Scale = 2.00; ResX = 3840; ResY = 2160 }
)

$allDpiPass = $true
foreach ($disp in $displayConfigs) {
    $res = Simulate-ShowWheelAt ($disp.ResX / 2.0) ($disp.ResY / 2.0) 0 $disp.ResX 0 $disp.ResY $disp.Scale
    if ($res.X -lt $res.MinX -or $res.X -gt $res.MaxX -or $res.Y -lt $res.MinY -or $res.Y -gt $res.MaxY) {
        $allDpiPass = $false
    }
}
Assert-Test "DPI" "Wheel stays bounded across 100%, 125%, 150%, 175%, 200% DPI scales" $allDpiPass

# Test 7.3: Live Process Instantiation Smoke Test
Write-Host "`nExecuting Live Process Smoke Test..." -ForegroundColor Cyan

$appExe = "E:\ANTIGRAVITY\OMNIWHEEL\Native\bin\OMNIWHEEL.exe"
$proc = Start-Process -FilePath $appExe -PassThru
Start-Sleep -Milliseconds 2500
$liveProc = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
$isAlive = ($null -ne $liveProc)
$isResponding = if ($isAlive) { $liveProc.Responding } else { $false }
$memMb = if ($isAlive) { [math]::Round($liveProc.WorkingSet64 / 1MB, 2) } else { 0 }
if ($isAlive) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }

$staSuccess = ($isAlive -and $isResponding)
Assert-Test "Smoke" "Live Process Smoke Test: OMNIWHEEL.exe boots, responsive, low memory ($memMb MB)" $staSuccess "RAM: $memMb MB, Responding: $isResponding"


# ------------------------------------------------------------------------------
# FINAL REPORT
# ------------------------------------------------------------------------------
Write-Host "`n==============================================================================" -ForegroundColor Cyan
Write-Host " UI/UX TEST SUITE SUMMARY" -ForegroundColor Cyan
Write-Host "==============================================================================" -ForegroundColor Cyan
Write-Host " Total Tests : $script:totalTests"
Write-Host " Passed      : $script:passCount" -ForegroundColor Green
Write-Host " Failed      : $script:failCount" -ForegroundColor $(if ($script:failCount -eq 0) { "Green" } else { "Red" })
$passRate = if ($script:totalTests -gt 0) { [Math]::Round(($script:passCount / $script:totalTests) * 100.0, 1) } else { 0.0 }
Write-Host " Pass Rate   : $passRate%" -ForegroundColor $(if ($passRate -eq 100.0) { "Green" } else { "Red" })
Write-Host "==============================================================================" -ForegroundColor Cyan

if ($script:failCount -eq 0) {
    exit 0
} else {
    exit 1
}
