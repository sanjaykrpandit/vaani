# Test script to run Vaani with meetingId parameter

Write-Host "Building Vaani..." -ForegroundColor Cyan
dotnet build

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful!" -ForegroundColor Green
    
    $exePath = ".\bin\Debug\net8.0-windows\win-x64\Vaani.exe"
    
    if (Test-Path $exePath) {
        Write-Host "`nRunning Vaani with meetingId parameter..." -ForegroundColor Cyan
        Write-Host "Executable: $exePath" -ForegroundColor Gray
        
        # Test with URL containing meetingId
        & $exePath "https://vaani-rtt-api.tryzent.com/launcher/vaani.application?meetingId=VM-2025-1220-A7B3"
    } else {
        Write-Host "`nError: Executable not found at $exePath" -ForegroundColor Red
        Write-Host "Try running 'dotnet build' first." -ForegroundColor Yellow
    }
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
}
