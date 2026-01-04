@echo off
echo ========================================
echo   Vaani API Diagnostic Tool
echo ========================================
echo.

echo [1/5] Checking if API is running on port 7020...
powershell -Command "try { $response = Invoke-WebRequest -Uri 'https://localhost:7020/health' -SkipCertificateCheck -TimeoutSec 2; Write-Host 'SUCCESS: API is running!' -ForegroundColor Green; Write-Host 'Response:' $response.Content } catch { Write-Host 'FAILED: API is NOT running on port 7020' -ForegroundColor Red; Write-Host 'Error:' $_.Exception.Message }"
echo.

echo [2/5] Checking if API is running on port 5114...
powershell -Command "try { $response = Invoke-WebRequest -Uri 'http://localhost:5114/health' -TimeoutSec 2; Write-Host 'SUCCESS: API is running!' -ForegroundColor Green; Write-Host 'Response:' $response.Content } catch { Write-Host 'FAILED: API is NOT running on port 5114' -ForegroundColor Red; Write-Host 'Error:' $_.Exception.Message }"
echo.

echo [3/5] Checking if port 7020 is in use...
powershell -Command "$port = Get-NetTCPConnection -LocalPort 7020 -ErrorAction SilentlyContinue; if ($port) { Write-Host 'Port 7020 is IN USE by process:' $port.OwningProcess -ForegroundColor Yellow } else { Write-Host 'Port 7020 is FREE' -ForegroundColor Red }"
echo.

echo [4/5] Checking if port 5114 is in use...
powershell -Command "$port = Get-NetTCPConnection -LocalPort 5114 -ErrorAction SilentlyContinue; if ($port) { Write-Host 'Port 5114 is IN USE by process:' $port.OwningProcess -ForegroundColor Yellow } else { Write-Host 'Port 5114 is FREE' -ForegroundColor Red }"
echo.

echo [5/5] Testing Swagger endpoints...
echo.
echo Testing: https://localhost:7020/swagger
powershell -Command "try { $null = Invoke-WebRequest -Uri 'https://localhost:7020/swagger' -SkipCertificateCheck -TimeoutSec 2; Write-Host 'SUCCESS: /swagger is accessible!' -ForegroundColor Green } catch { Write-Host 'FAILED: /swagger returned' $_.Exception.Response.StatusCode -ForegroundColor Red }"
echo.
echo Testing: https://localhost:7020/swagger/v1/swagger.json
powershell -Command "try { $null = Invoke-WebRequest -Uri 'https://localhost:7020/swagger/v1/swagger.json' -SkipCertificateCheck -TimeoutSec 2; Write-Host 'SUCCESS: swagger.json is accessible!' -ForegroundColor Green } catch { Write-Host 'FAILED: swagger.json returned' $_.Exception.Response.StatusCode -ForegroundColor Red }"
echo.

echo ========================================
echo   Diagnostic Complete
echo ========================================
echo.
echo INTERPRETATION:
echo - If health check FAILED: The API is NOT running. Run START_API.bat first!
echo - If health check SUCCEEDED but swagger FAILED: There's a routing issue
echo - If port is FREE: No API is running on that port
echo - If port is IN USE: An API (or another app) is using that port
echo.
pause
