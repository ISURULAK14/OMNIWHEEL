# Installation Verification Script
$ErrorActionPreference = "Continue"

Write-Host "=== CHECKING OMNIWHEEL INSTALLATION INTEGRITY ===" -ForegroundColor Cyan

$localAppDir = [System.IO.Path]::Combine($env:LOCALAPPDATA, "OMNIWHEEL")
$roamingAppDir = [System.IO.Path]::Combine($env:APPDATA, "OMNIWHEEL")

$errors = @()
$warnings = @()

# 1. Directory Checks
Write-Host "`n[1/6] Checking Installation Directories..."
if (Test-Path $localAppDir) {
    Write-Host " [PASS] Local AppData directory exists: $localAppDir" -ForegroundColor Green
} else {
    $errors += "Local AppData directory missing: $localAppDir"
    Write-Host " [FAIL] Local AppData directory missing: $localAppDir" -ForegroundColor Red
}

if (Test-Path $roamingAppDir) {
    Write-Host " [PASS] Roaming AppData directory exists: $roamingAppDir" -ForegroundColor Green
} else {
    $warnings += "Roaming AppData directory missing: $roamingAppDir"
    Write-Host " [WARN] Roaming AppData directory missing: $roamingAppDir" -ForegroundColor Yellow
}

# 2. File Integrity Checks
Write-Host "`n[2/6] Checking Critical Files..."
$criticalFiles = @(
    @{ Name = "OMNIWHEEL.exe"; MinSize = 100000; Required = $true },
    @{ Name = "OMNIWHEEL.exe.config"; MinSize = 100; Required = $true },
    @{ Name = "app.ico"; MinSize = 10000; Required = $true },
    @{ Name = "ffmpeg.exe"; MinSize = 100000000; Required = $true },
    @{ Name = "settings.ini"; MinSize = 50; Required = $true },
    @{ Name = "uninstall.exe"; MinSize = 1000000; Required = $true }
)

foreach ($item in $criticalFiles) {
    $path = [System.IO.Path]::Combine($localAppDir, $item.Name)
    if (Test-Path $path) {
        $fileInfo = Get-Item $path
        if ($fileInfo.Length -ge $item.MinSize) {
            Write-Host " [PASS] $($item.Name) present and healthy ($($fileInfo.Length) bytes)" -ForegroundColor Green
        } else {
            $errors += "$($item.Name) corrupted or undersized ($($fileInfo.Length) bytes < $($item.MinSize))"
            Write-Host " [FAIL] $($item.Name) undersized ($($fileInfo.Length) bytes)" -ForegroundColor Red
        }
    } else {
        if ($item.Required) {
            $errors += "$($item.Name) missing from $localAppDir"
            Write-Host " [FAIL] $($item.Name) missing" -ForegroundColor Red
        } else {
            $warnings += "$($item.Name) not found"
            Write-Host " [WARN] $($item.Name) not found" -ForegroundColor Yellow
        }
    }
}

# 3. Settings Integrity
Write-Host "`n[3/6] Checking Configuration Files..."
$iniPath = [System.IO.Path]::Combine($roamingAppDir, "settings.ini")
if (Test-Path $iniPath) {
    $iniContent = Get-Content $iniPath -Raw
    $keys = @("StartWithWindows", "PrimaryHotkey", "SummonMode", "ThemeMode", "ThemeAccent", "JpegQuality")
    $missingKeys = @()
    foreach ($k in $keys) {
        if ($iniContent -notmatch "(?m)^$k=") {
            $missingKeys += $k
        }
    }
    if ($missingKeys.Count -eq 0) {
        Write-Host " [PASS] settings.ini has all essential keys" -ForegroundColor Green
    } else {
        $warnings += "settings.ini missing keys: $($missingKeys -join ', ')"
        Write-Host " [WARN] settings.ini missing keys: $($missingKeys -join ', ')" -ForegroundColor Yellow
    }
} else {
    $warnings += "settings.ini not found in Roaming AppData"
    Write-Host " [WARN] settings.ini missing in Roaming AppData" -ForegroundColor Yellow
}

# 4. Shortcut Integrity
Write-Host "`n[4/6] Checking Shortcuts..."
$wsh = New-Object -ComObject WScript.Shell
$desktopDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$desktopLnk = [System.IO.Path]::Combine($desktopDir, "OMNIWHEEL.lnk")
if (Test-Path $desktopLnk) {
    $sc = $wsh.CreateShortcut($desktopLnk)
    if (Test-Path $sc.TargetPath) {
        Write-Host " [PASS] Desktop shortcut valid -> Target: $($sc.TargetPath)" -ForegroundColor Green
    } else {
        $errors += "Desktop shortcut target invalid: $($sc.TargetPath)"
        Write-Host " [FAIL] Desktop shortcut target invalid: $($sc.TargetPath)" -ForegroundColor Red
    }
} else {
    $userDesktop = [System.IO.Path]::Combine($env:USERPROFILE, "Desktop", "OMNIWHEEL.lnk")
    if (Test-Path $userDesktop) {
        Write-Host " [PASS] Desktop shortcut in user profile -> $userDesktop" -ForegroundColor Green
    } else {
        $warnings += "Desktop shortcut not found at $desktopLnk"
        Write-Host " [WARN] Desktop shortcut not found at $desktopLnk" -ForegroundColor Yellow
    }
}

$programsDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$menuLnk = [System.IO.Path]::Combine($programsDir, "OMNIWHEEL", "OMNIWHEEL.lnk")
if (Test-Path $menuLnk) {
    $sc = $wsh.CreateShortcut($menuLnk)
    if (Test-Path $sc.TargetPath) {
        Write-Host " [PASS] Start Menu shortcut valid -> Target: $($sc.TargetPath)" -ForegroundColor Green
    } else {
        $errors += "Start Menu shortcut target invalid: $($sc.TargetPath)"
        Write-Host " [FAIL] Start Menu shortcut target invalid: $($sc.TargetPath)" -ForegroundColor Red
    }
} else {
    $warnings += "Start Menu shortcut not found at $menuLnk"
    Write-Host " [WARN] Start Menu shortcut not found at $menuLnk" -ForegroundColor Yellow
}

# 5. Registry Context Menu & Startup Integration
Write-Host "`n[5/6] Checking Shell Context Menu & Startup Integration..."
$regKeys = @(
    "HKCU\Software\Classes\*\shell\OMNIWHEEL",
    "HKCU\Software\Classes\Directory\shell\OMNIWHEEL",
    "HKCU\Software\Classes\Directory\Background\shell\OMNIWHEEL"
)

foreach ($rk in $regKeys) {
    $cmdOut = cmd /c "reg query `"$rk`" /ve 2>&1"
    if ($cmdOut -match "REG_SZ") {
        Write-Host " [PASS] Context menu registered: $rk" -ForegroundColor Green
    } else {
        $warnings += "Context menu key not found: $rk"
        Write-Host " [WARN] Context menu key not found: $rk" -ForegroundColor Yellow
    }
}

# Check Run Key
$runOut = cmd /c 'reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v OMNIWHEEL 2>&1'
if ($runOut -match "OMNIWHEEL") {
    Write-Host " [PASS] Startup entry registered in HKCU Run" -ForegroundColor Green
} else {
    $warnings += "Startup entry missing in Run key"
    Write-Host " [WARN] Startup entry missing in Run key" -ForegroundColor Yellow
}

# Check Uninstall Key
$uninstOut = cmd /c 'reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\OMNIWHEEL" /v DisplayName 2>&1'
if ($uninstOut -match "OMNIWHEEL") {
    Write-Host " [PASS] Add/Remove Programs entry registered in Registry" -ForegroundColor Green
} else {
    $errors += "Add/Remove Programs uninstall key missing"
    Write-Host " [FAIL] Add/Remove Programs uninstall key missing" -ForegroundColor Red
}

# 6. Installed Binary Execution Checks
Write-Host "`n[6/6] Checking Installed Executable Functionality..."
$installedFfmpeg = [System.IO.Path]::Combine($localAppDir, "ffmpeg.exe")

try {
    $ffOut = & $installedFfmpeg -version 2>&1 | Select-Object -First 1
    if ($ffOut -match "ffmpeg version") {
        Write-Host " [PASS] Installed ffmpeg.exe execution verified: $ffOut" -ForegroundColor Green
    } else {
        $errors += "ffmpeg.exe failed to execute properly"
        Write-Host " [FAIL] ffmpeg.exe failed execution test" -ForegroundColor Red
    }
} catch {
    $errors += "ffmpeg.exe execution threw exception: $_"
    Write-Host " [FAIL] ffmpeg.exe threw exception: $_" -ForegroundColor Red
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " SUMMARY: $($errors.Count) Errors, $($warnings.Count) Warnings" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($errors.Count -eq 0) {
    Write-Host "RESULT: ALL INSTALLATION CHECKS PASSED WITH 0 ERRORS." -ForegroundColor Green
} else {
    Write-Host "RESULT: $($errors.Count) ERROR(S) FOUND:" -ForegroundColor Red
    foreach ($e in $errors) { Write-Host " - $e" -ForegroundColor Red }
}
