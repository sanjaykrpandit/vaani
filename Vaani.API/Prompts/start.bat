@echo off
echo ========================================
echo    Vaani API - Quick Start
echo ========================================
echo.

echo Checking .NET SDK...
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found!
    echo Please install .NET 8 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
echo OK: .NET SDK found
echo.

echo Restoring packages...
dotnet restore >nul 2>&1
if errorlevel 1 (
    echo ERROR: Failed to restore packages
    pause
    exit /b 1
)
echo OK: Packages restored
echo.

echo Building project...
dotnet build --verbosity quiet >nul 2>&1
if errorlevel 1 (
    echo ERROR: Build failed
    echo Run 'dotnet build' to see detailed errors
    pause
    exit /b 1
)
echo OK: Build successful
echo.

echo ========================================
echo    Starting Vaani API...
echo ========================================
echo.
echo API will be available at:
echo   - Swagger: https://localhost:7020
echo   - Health:  https://localhost:7020/health
echo.
echo Press Ctrl+C to stop the API
echo.

dotnet run
