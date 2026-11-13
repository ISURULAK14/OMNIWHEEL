@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo Installing OMNIWHEEL Native Windows Shell Utility
echo ========================================================

set "EXE_PATH=%~dp0bin\OMNIWHEEL.exe"
if not exist "%EXE_PATH%" (
    echo Binary not found. Building now...
    call Build.bat
)

if not exist "%EXE_PATH%" (
    echo Error: Failed to build %EXE_PATH%
    pause
    exit /b 1
)

:: 1. Create Desktop Shortcut with Royal Purple Wheel Icon
powershell -NoProfile -Command "$w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\OMNIWHEEL.lnk'); $s.TargetPath='%EXE_PATH%'; $s.WorkingDirectory='%~dp0bin'; $s.Description='OMNIWHEEL Native Radial Shell Utility'; $s.IconLocation='%EXE_PATH%,0'; $s.Save(); Write-Host ' [x] Desktop Shortcut Created (OMNIWHEEL.lnk)' -ForegroundColor Green"

:: 2. Register Windows Explorer Right-Click Context Menu (HKCU)
reg add "HKCU\Software\Classes\*\shell\OMNIWHEEL" /ve /d "OMNIWHEEL (Donut)" /f >nul
reg add "HKCU\Software\Classes\*\shell\OMNIWHEEL" /v "Icon" /d "%EXE_PATH%,0" /f >nul
reg add "HKCU\Software\Classes\*\shell\OMNIWHEEL\command" /ve /d "\"%EXE_PATH%\" \"%%1\"" /f >nul
reg delete "HKCU\Software\Classes\*\shell\LocalConvertNative" /f 2>nul
echo  [x] Windows File Explorer Context Menu Registered (HKCU)

:: 3. Launch the native background tray service
start "" "%EXE_PATH%"
echo  [x] OMNIWHEEL Started in System Tray!

echo.
echo ========================================================
echo Installation Complete!
echo  - Press and hold [`] (backtick key) when in File Explorer or Desktop to summon OMNIWHEEL.
echo  - Or Right-click ANY file in File Explorer and choose "OMNIWHEEL (Donut)".
echo  - Manage app via Royal Purple Wheel tray icon near the Windows clock.
echo  - Right-click tray icon to toggle "Run at Windows Startup".
echo ========================================================
