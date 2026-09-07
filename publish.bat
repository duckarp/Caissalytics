@echo off
REM Caissalytics Desktop Publisher Script (Windows)
REM Builds standalone, self-contained desktop executable for Windows.

echo === Caissalytics Desktop Application Publisher (Windows) ===
echo.
echo >> Publishing Windows x64 Desktop Application...

dotnet publish Caissalytics\Caissalytics.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "dist\win-x64"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed!
    exit /b %ERRORLEVEL%
)

echo.
echo >> Windows package created at dist\win-x64\Caissalytics.exe
echo === Publishing completed successfully! ===
