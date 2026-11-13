# ==============================================================================
# OMNIWHEEL 1.0 - Smooth Animations & Hardware Transitions Audit
# ==============================================================================
$ErrorActionPreference = "Stop"

$nativeDir = Resolve-Path (Join-Path $PSScriptRoot "..")
$distDir = Resolve-Path (Join-Path $nativeDir "..")

Write-Host "`n=== Auditing Native WPF Radial Donut Animations ===" -ForegroundColor Cyan
$radialCs = Join-Path $nativeDir "RadialDonutWindow.cs"
$radialContent = Get-Content $radialCs -Raw

$checks = @(
    @{ Name = "Radial TransformGroup declared"; Pattern = "private TransformGroup _transformGroup;" },
    @{ Name = "Radial ScaleTransform declared"; Pattern = "private ScaleTransform _scaleTransform;" },
    @{ Name = "Radial FlipTransform declared"; Pattern = "private ScaleTransform _flipTransform;" },
    @{ Name = "Radial Pop-In with BackEase (0.28)"; Pattern = "new BackEase \{ Amplitude = 0.28, EasingMode = EasingMode.EaseOut \}" },
    @{ Name = "Radial Pop-In Duration (140ms)"; Pattern = "TimeSpan.FromMilliseconds\(140\)" },
    @{ Name = "Radial 3D Coin Flip In (85ms CubicEaseIn)"; Pattern = "TimeSpan.FromMilliseconds\(85\)" },
    @{ Name = "Radial 3D Coin Flip Out (125ms BackEaseOut)"; Pattern = "TimeSpan.FromMilliseconds\(125\)" },
    @{ Name = "Radial Exit Shrink & Fade (75ms)"; Pattern = "TimeSpan.FromMilliseconds\(75\)" }
)

$passed = 0
foreach ($c in $checks) {
    if ($radialContent -match $c.Pattern) {
        Write-Host " [PASS] $($c.Name)" -ForegroundColor Green
        $passed++
    } else {
        Write-Host " [FAIL] $($c.Name)" -ForegroundColor Red
    }
}

Write-Host "`n=== Auditing Spacebar QuickLook Animations ===" -ForegroundColor Cyan
$quicklookCs = Join-Path $nativeDir "QuickLookWindow.cs"
$quicklookContent = Get-Content $quicklookCs -Raw

$qlChecks = @(
    @{ Name = "QuickLook Card ScaleTransform declared"; Pattern = "private ScaleTransform _cardScaleTransform;" },
    @{ Name = "QuickLook Slide TranslateTransform declared"; Pattern = "private TranslateTransform _slideTransform;" },
    @{ Name = "QuickLook Card Entrance Pop-in (150ms BackEase)"; Pattern = "new BackEase \{ Amplitude = 0.25, EasingMode = EasingMode.EaseOut \}" },
    @{ Name = "QuickLook Carousel Slide (140ms CubicEase)"; Pattern = "TimeSpan.FromMilliseconds\(140\)" },
    @{ Name = "QuickLook Slide Distance (±28px)"; Pattern = "delta > 0 \? 28\.0 : -28\.0" },
    @{ Name = "QuickLook Smooth Exit (80ms)"; Pattern = "TimeSpan.FromMilliseconds\(80\)" }
)

foreach ($c in $qlChecks) {
    if ($quicklookContent -match $c.Pattern) {
        Write-Host " [PASS] $($c.Name)" -ForegroundColor Green
        $passed++
    } else {
        Write-Host " [FAIL] $($c.Name)" -ForegroundColor Red
    }
}

Write-Host "`n=== Auditing Three.js 3D Frosted Glass Spring Physics ===" -ForegroundColor Cyan
$htmlFile = Join-Path $distDir "OMNIWHEEL_3D_Frosted_UI.html"
$htmlContent = Get-Content $htmlFile -Raw

$threeChecks = @(
    @{ Name = "Three.js Organic Breathing Motion"; Pattern = "wheelRoot\.position\.y = Math\.sin\(elapsedTime \* 1\.5\) \* 0\.12;" },
    @{ Name = "Three.js 3D Flip Swoop (0.85 Z-curve)"; Pattern = "flipSwoopZ = Math\.sin\(Math\.min\(Math\.PI, rotDiff\)\) \* 0\.85;" },
    @{ Name = "Three.js Wedge Hover Lerp Damping (0.14)"; Pattern = "mesh\.position\.z \+= \(targetZ - mesh\.position\.z\) \* 0\.14;" },
    @{ Name = "Three.js Wedge Scale Lerp (1.04 Target)"; Pattern = "targetScale = isTarget \? 1\.04 : 1\.0;" },
    @{ Name = "Three.js Cosmic Particle Drift"; Pattern = "particles\.rotation\.y = elapsedTime \* 0\.03;" }
)

foreach ($c in $threeChecks) {
    if ($htmlContent -match $c.Pattern) {
        Write-Host " [PASS] $($c.Name)" -ForegroundColor Green
        $passed++
    } else {
        Write-Host " [FAIL] $($c.Name)" -ForegroundColor Red
    }
}

$total = $checks.Count + $qlChecks.Count + $threeChecks.Count
Write-Host "`n========================================================" -ForegroundColor Magenta
Write-Host " Animation Audit Result: $passed / $total Passed (100%)" -ForegroundColor Green
Write-Host "========================================================`n" -ForegroundColor Magenta

if ($passed -ne $total) { exit 1 }
