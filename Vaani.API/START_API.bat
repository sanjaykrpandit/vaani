@echo off
cls
echo ========================================
echo    Starting Vaani API
echo ========================================
echo.

cd /d "%~dp0"

echo Current directory: %CD%
echo.

echo [1/3] Checking .NET SDK...
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found!
    echo Please install from: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)
echo OK - .NET SDK found
echo.

echo [2/3] Building project...
dotnet build --nologo --verbosity quiet
if errorlevel 1 (
    echo ERROR: Build failed!
    echo.
    echo Running detailed build...
    dotnet build
    pause
    exit /b 1
)
echo OK - Build successful
echo.

echo [3/3] Starting API...
echo.
echo ========================================
echo  Vaani API is starting...
echo ========================================
echo.
echo  Once started, open your browser to:
echo.
echo  HTTPS: https://localhost:7020
echo  HTTP:  http://localhost:5114
echo.
echo  Press Ctrl+C to stop the API
echo ========================================
echo.

REM Trust the dev certificate if needed
dotnet dev-certs https --trust >nul 2>&1

REM Run the API
dotnet run --no-build

echo.
echo API stopped.
pause
