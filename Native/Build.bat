@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo Building OMNIWHEEL Native Windows Application (.NET 8 CoreCLR)
echo ========================================================

set "DOTNET_CMD=dotnet"
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_CMD=%USERPROFILE%\.dotnet\dotnet.exe"
)

"%DOTNET_CMD%" publish "LocalConvertNative.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%~dp0bin"

if %ERRORLEVEL% EQU 0 (
    if exist "app.config" copy /y "app.config" "bin\OMNIWHEEL.exe.config" >nul
    if exist "app.config" copy /y "app.config" "bin\LocalConvertNative.exe.config" >nul
    if exist "app.ico" copy /y "app.ico" "bin\app.ico" >nul
    if exist "bin\OMNIWHEEL.exe" copy /y "bin\OMNIWHEEL.exe" "bin\LocalConvertNative.exe" >nul
    echo.
    echo ========================================================
    echo Build Succeeded!
    echo Output: %~dp0bin\OMNIWHEEL.exe
    echo ========================================================
) else (
    echo.
    echo Build Failed with error code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)
