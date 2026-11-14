@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

echo ========================================================
echo OMNIWHEEL - Full Automated Build, Sign, Test Pipeline
echo ========================================================

rem ------------------------------------------------
rem Step 1: Find or install .NET 8 SDK
rem ------------------------------------------------
set "DOTNET_EXE="
where dotnet >nul 2>&1 && (
    for /f "delims=" %%i in ('dotnet --list-sdks 2^>nul ^| findstr "8."') do set "HAS_SDK=1"
)
if defined HAS_SDK (
    set "DOTNET_EXE=dotnet"
    echo .NET 8 SDK found on PATH.
    goto :sdk_ready
)

rem Check user-local install
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    "%USERPROFILE%\.dotnet\dotnet.exe" --list-sdks 2>nul | findstr "8." >nul && (
        set "DOTNET_EXE=%USERPROFILE%\.dotnet\dotnet.exe"
        echo .NET 8 SDK found at %USERPROFILE%\.dotnet
        goto :sdk_ready
    )
)

echo .NET 8 SDK not found. Downloading...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '%TEMP%\dotnet-install.ps1' -UseBasicParsing; & '%TEMP%\dotnet-install.ps1' -Channel 8.0 -InstallDir '%USERPROFILE%\.dotnet'"
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_EXE=%USERPROFILE%\.dotnet\dotnet.exe"
) else (
    echo ERROR: Failed to install .NET 8 SDK.
    exit /b 1
)

:sdk_ready
echo Using: %DOTNET_EXE%
"%DOTNET_EXE%" --version

rem ------------------------------------------------
rem Step 2: Restore and add OpenXML package
rem ------------------------------------------------
set "PROJ=%~dp0LocalConvertNative.csproj"
"%DOTNET_EXE%" add "%PROJ%" package DocumentFormat.OpenXml --version 2.20.0 2>nul
"%DOTNET_EXE%" restore "%PROJ%"

rem ------------------------------------------------
rem Step 3: Publish self-contained single-file exe
rem ------------------------------------------------
echo Building OMNIWHEEL via dotnet publish...
if not exist "bin" mkdir bin
"%DOTNET_EXE%" publish "%PROJ%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%~dp0bin"

if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    exit /b 1
)

rem Copy config and assets
if exist "app.config" copy /y "app.config" "bin\OMNIWHEEL.exe.config" >nul
if exist "app.ico" copy /y "app.ico" "bin\app.ico" >nul
if exist "bin\OMNIWHEEL.exe" copy /y "bin\OMNIWHEEL.exe" "bin\LocalConvertNative.exe" >nul

echo Build succeeded: bin\OMNIWHEEL.exe

rem ------------------------------------------------
rem Step 4: Create settings.ini if missing
rem ------------------------------------------------
if not exist "bin\settings.ini" (
    echo [Settings]> "bin\settings.ini"
    echo Theme=RoyalPurple>> "bin\settings.ini"
    echo OutputFolder=Converted>> "bin\settings.ini"
)

rem ------------------------------------------------
rem Step 5: Sign binaries
rem ------------------------------------------------
echo Signing binaries...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign_binaries.ps1"

rem ------------------------------------------------
rem Step 6: Build standalone installer (csc.exe)
rem ------------------------------------------------
echo Building installer...
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
set "WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"

if not exist "%CSC%" (
    echo WARNING: csc.exe not found. Skipping installer build.
    goto :skip_installer
)

set REFS=/r:"%FW%\System.dll"
set REFS=%REFS% /r:"%FW%\System.Core.dll"
set REFS=%REFS% /r:"%FW%\System.Drawing.dll"
set REFS=%REFS% /r:"%FW%\System.Windows.Forms.dll"
set REFS=%REFS% /r:"%FW%\Microsoft.CSharp.dll"
set REFS=%REFS% /r:"%FW%\System.Xaml.dll"
set REFS=%REFS% /r:"%FW%\System.IO.Compression.dll"
set REFS=%REFS% /r:"%FW%\System.IO.Compression.FileSystem.dll"
set REFS=%REFS% /r:"%WPF%\PresentationCore.dll"
set REFS=%REFS% /r:"%WPF%\PresentationFramework.dll"
set REFS=%REFS% /r:"%WPF%\WindowsBase.dll"

set RES=/resource:bin\OMNIWHEEL.exe,OMNIWHEEL.exe
set RES=%RES% /resource:bin\OMNIWHEEL.exe.config,OMNIWHEEL.exe.config
set RES=%RES% /resource:app.ico,app.ico
set RES=%RES% /resource:bin\settings.ini,settings.ini
if exist "bin\ffmpeg.zip" set RES=%RES% /resource:bin\ffmpeg.zip,ffmpeg.zip
set RES=%RES% /resource:WindowsOcrEngine.ps1,WindowsOcrEngine.ps1

echo Compiling OMNIWHEEL_Setup.exe...
"%CSC%" /target:winexe /optimize+ /platform:anycpu /highentropyva+ /win32manifest:app.manifest /win32icon:app.ico %REFS% %RES% /out:"bin\OMNIWHEEL_Setup.exe" Installer\OMNIWHEEL_Setup.cs

if errorlevel 1 (
    echo WARNING: Installer compilation failed.
    goto :skip_installer
)

copy /y "bin\OMNIWHEEL_Setup.exe" "..\OMNIWHEEL_Setup.exe" >nul
echo Installer built: bin\OMNIWHEEL_Setup.exe

rem Re-sign installer
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign_binaries.ps1"

:skip_installer

rem ------------------------------------------------
rem Step 7: Static audit
rem ------------------------------------------------
echo Running static audit...
if exist "%~dp0audit_exe.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0audit_exe.ps1" 2>nul
)

rem ------------------------------------------------
rem Step 8: Run test suite
rem ------------------------------------------------
echo Running test suite...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0test_suite.ps1"

rem ------------------------------------------------
rem Step 9: UI smoke test
rem ------------------------------------------------
echo UI smoke test: launching OMNIWHEEL for 5 seconds...
start "" "bin\OMNIWHEEL.exe"
powershell -NoProfile -Command "Start-Sleep -Seconds 5"
taskkill /im OMNIWHEEL.exe /f >nul 2>&1

echo.
echo ========================================================
echo ALL STEPS COMPLETE
echo   EXE:       %~dp0bin\OMNIWHEEL.exe
echo   Installer: %~dp0bin\OMNIWHEEL_Setup.exe
echo ========================================================
endlocal
