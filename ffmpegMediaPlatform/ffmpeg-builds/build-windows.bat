@echo off
echo FFmpeg Windows Build Script
echo ==========================
echo.

REM Check if PowerShell is available
powershell -Command "Get-Host" >nul 2>&1
if errorlevel 1 (
    echo Error: PowerShell is not available on this system.
    echo Please ensure PowerShell is installed and try again.
    pause
    exit /b 1
)

echo Building and copying Windows FFmpeg libraries...
echo.

REM Run the PowerShell script
powershell -ExecutionPolicy Bypass -File "build-and-copy-windows.ps1"

if errorlevel 1 (
    echo.
    echo Build failed! Check the error messages above.
    pause
    exit /b 1
)

echo.
echo Build completed successfully!
echo The Windows FFmpeg libraries are now available for replay video playback.
echo.
pause 