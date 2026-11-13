@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo Building OMNIWHEEL Standalone Installer (Setup.exe)
echo ========================================================

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
set "WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"

if not exist "%CSC%" (
    echo Error: C# Compiler not found at %CSC%
    exit /b 1
)

if not exist "bin\OMNIWHEEL.exe" (
    echo Error: Primary payload bin\OMNIWHEEL.exe not found. Running Build.bat first...
    call Build.bat
)

set "OUT=bin\OMNIWHEEL_Setup.exe"

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
set RES=%RES% /resource:bin\ffmpeg.zip,ffmpeg.zip
set RES=%RES% /resource:WindowsOcrEngine.ps1,WindowsOcrEngine.ps1
set RES=%RES% /resource:bin\D3DCompiler_47_cor3.dll,D3DCompiler_47_cor3.dll
set RES=%RES% /resource:bin\PenImc_cor3.dll,PenImc_cor3.dll
set RES=%RES% /resource:bin\PresentationNative_cor3.dll,PresentationNative_cor3.dll
set RES=%RES% /resource:bin\vcruntime140_cor3.dll,vcruntime140_cor3.dll
set RES=%RES% /resource:bin\wpfgfx_cor3.dll,wpfgfx_cor3.dll

echo Signing payload bin\OMNIWHEEL.exe prior to embedding...
powershell -NoProfile -ExecutionPolicy Bypass -File "sign_binaries.ps1"

echo Compiling standalone installer with embedded payloads...
"%CSC%" /target:winexe /optimize+ /platform:anycpu /highentropyva+ /win32manifest:app.manifest /win32icon:app.ico %REFS% %RES% /out:"%OUT%" Installer\OMNIWHEEL_Setup.cs

if %ERRORLEVEL% EQU 0 (
    copy /y bin\OMNIWHEEL_Setup.exe ..\OMNIWHEEL_Setup.exe >nul
    echo Signing standalone installer binaries...
    powershell -NoProfile -ExecutionPolicy Bypass -File "sign_binaries.ps1"
    echo.
    echo ========================================================
    echo Installer Build Succeeded!
    echo Output: %~dp0%OUT%
    echo Distribution copy: %~dp0..\OMNIWHEEL_Setup.exe
    echo ========================================================
) else (
    echo.
    echo Installer Build Failed with error code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)
