@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo Uninstalling OMNIWHEEL Native Shell Integration
echo ========================================================

:: 1. Terminate running process
taskkill /F /IM OMNIWHEEL.exe 2>nul
taskkill /F /IM LocalConvertNative.exe 2>nul
echo  [x] Stopped background process

:: 2. Remove Desktop shortcuts
powershell -NoProfile -Command "$paths = @([Environment]::GetFolderPath('Desktop') + '\OMNIWHEEL.lnk', [Environment]::GetFolderPath('Desktop') + '\LocalConvert Pro (Native).lnk'); foreach ($p in $paths) { if (Test-Path $p) { Remove-Item $p -Force } }; Write-Host ' [x] Removed Desktop Shortcuts' -ForegroundColor Green"

:: 3. Remove Startup entry
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "OMNIWHEEL" /f 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "LocalConvertPro" /f 2>nul
powershell -NoProfile -Command "$sp = [Environment]::GetFolderPath('Startup') + '\OMNIWHEEL.lnk'; if (Test-Path $sp) { Remove-Item $sp -Force }"

:: 4. Remove Registry Context Menu
reg delete "HKCU\Software\Classes\*\shell\OMNIWHEEL" /f 2>nul
reg delete "HKCU\Software\Classes\Directory\shell\OMNIWHEEL" /f 2>nul
reg delete "HKCU\Software\Classes\Directory\Background\shell\OMNIWHEEL" /f 2>nul
reg delete "HKCU\Software\Classes\*\shell\LocalConvertNative" /f 2>nul
echo  [x] Removed Windows File Explorer Context Menu (HKCU)

echo.
echo ========================================================
echo Uninstallation Complete!
echo ========================================================
