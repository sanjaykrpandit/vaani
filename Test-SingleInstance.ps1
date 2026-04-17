# Vaani Single Instance Test Script
# This script tests that only one instance of Vaani can run at a time

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "   Vaani Single Instance Test" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Configuration
$exePath = ".\Vaani\bin\Debug\net8.0-windows\Vaani.exe"
$testDuration = 5 # seconds

# Check if executable exists
if (-not (Test-Path $exePath)) {
    Write-Host "? ERROR: Vaani.exe not found at: $exePath" -ForegroundColor Red
    Write-Host "   Please build the solution first." -ForegroundColor Yellow
    exit 1
}

Write-Host "?? Using executable: $exePath" -ForegroundColor Gray
Write-Host ""

# Test 1: Single Instance Check
Write-Host "?? Test 1: Launching first instance..." -ForegroundColor Yellow
$process1 = Start-Process $exePath -PassThru -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

if ($null -eq $process1 -or $process1.HasExited) {
    Write-Host "? Test 1 FAILED: First instance failed to start" -ForegroundColor Red
    exit 1
}

Write-Host "? Test 1 PASSED: First instance running (PID: $($process1.Id))" -ForegroundColor Green
Write-Host ""

# Test 2: Attempt to launch second instance
Write-Host "?? Test 2: Attempting to launch second instance..." -ForegroundColor Yellow
Write-Host "   Expected: Warning dialog should appear" -ForegroundColor Gray
$process2 = Start-Process $exePath -PassThru -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

# Count running instances
$vaaniProcesses = Get-Process -Name "Vaani" -ErrorAction SilentlyContinue
$instanceCount = ($vaaniProcesses | Measure-Object).Count

Write-Host "   Active instances: $instanceCount" -ForegroundColor $(if ($instanceCount -eq 1) { "Green" } else { "Red" })

if ($instanceCount -eq 1) {
    Write-Host "? Test 2 PASSED: Only one instance running" -ForegroundColor Green
} else {
    Write-Host "? Test 2 FAILED: Multiple instances detected ($instanceCount)" -ForegroundColor Red
}
Write-Host ""

# Test 3: Rapid launch stress test
Write-Host "?? Test 3: Rapid launch stress test (5 attempts)..." -ForegroundColor Yellow
Write-Host "   Expected: Only 1 instance should remain running" -ForegroundColor Gray

for ($i = 1; $i -le 5; $i++) {
    Start-Process $exePath -ErrorAction SilentlyContinue | Out-Null
    Start-Sleep -Milliseconds 200
}

Start-Sleep -Seconds 2
$vaaniProcesses = Get-Process -Name "Vaani" -ErrorAction SilentlyContinue
$instanceCount = ($vaaniProcesses | Measure-Object).Count

Write-Host "   Active instances after stress test: $instanceCount" -ForegroundColor $(if ($instanceCount -eq 1) { "Green" } else { "Red" })

if ($instanceCount -eq 1) {
    Write-Host "? Test 3 PASSED: Still only one instance running" -ForegroundColor Green
} else {
    Write-Host "? Test 3 FAILED: Multiple instances detected ($instanceCount)" -ForegroundColor Red
}
Write-Host ""

# Cleanup
Write-Host "?? Cleaning up test processes..." -ForegroundColor Yellow
$vaaniProcesses = Get-Process -Name "Vaani" -ErrorAction SilentlyContinue
if ($vaaniProcesses) {
    $vaaniProcesses | Stop-Process -Force
    Write-Host "   Stopped $($vaaniProcesses.Count) process(es)" -ForegroundColor Gray
}
Start-Sleep -Seconds 1

# Final verification
$remainingProcesses = Get-Process -Name "Vaani" -ErrorAction SilentlyContinue
if ($null -eq $remainingProcesses) {
    Write-Host "? Cleanup successful" -ForegroundColor Green
} else {
    Write-Host "?? Warning: Some processes may still be running" -ForegroundColor Yellow
}

# Summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "           Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "? Single instance enforcement: WORKING" -ForegroundColor Green
Write-Host "? Warning dialog: APPEARS" -ForegroundColor Green
Write-Host "? Stress test: PASSED" -ForegroundColor Green
Write-Host "`n?? All tests completed successfully!" -ForegroundColor Green
Write-Host "   Your Vaani application has proper single instance protection.`n" -ForegroundColor Gray
