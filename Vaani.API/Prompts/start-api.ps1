# Vaani API - Startup Troubleshooting Script

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   Vaani API - Startup Diagnostic" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check 1: .NET SDK
Write-Host "1. Checking .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "   ? .NET SDK found: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "   ? .NET SDK not found! Install .NET 8 SDK" -ForegroundColor Red
    Write-Host "   Download: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}

# Check 2: PostgreSQL
Write-Host ""
Write-Host "2. Checking PostgreSQL..." -ForegroundColor Yellow
$pgService = Get-Service -Name "postgresql*" -ErrorAction SilentlyContinue
if ($pgService) {
    if ($pgService.Status -eq "Running") {
        Write-Host "   ? PostgreSQL is running" -ForegroundColor Green
    } else {
        Write-Host "   ?? PostgreSQL is installed but not running" -ForegroundColor Yellow
        Write-Host "   Starting PostgreSQL..." -ForegroundColor Yellow
        Start-Service $pgService.Name
        Write-Host "   ? PostgreSQL started" -ForegroundColor Green
    }
} else {
    Write-Host "   ?? PostgreSQL not found (API will start in degraded mode)" -ForegroundColor Yellow
    Write-Host "   To install: https://www.postgresql.org/download/windows/" -ForegroundColor Yellow
}

# Check 3: Port availability
Write-Host ""
Write-Host "3. Checking port availability..." -ForegroundColor Yellow
$port7020 = Get-NetTCPConnection -LocalPort 7020 -ErrorAction SilentlyContinue
if ($port7020) {
    Write-Host "   ?? Port 7020 is already in use" -ForegroundColor Yellow
    Write-Host "   Process using port:" -ForegroundColor Yellow
    Get-Process -Id $port7020.OwningProcess | Format-Table ProcessName, Id
    $kill = Read-Host "   Kill this process? (y/n)"
    if ($kill -eq "y") {
        Stop-Process -Id $port7020.OwningProcess -Force
        Write-Host "   ? Process killed" -ForegroundColor Green
    }
} else {
    Write-Host "   ? Port 7020 is available" -ForegroundColor Green
}

# Check 4: Database connection
Write-Host ""
Write-Host "4. Testing database connection..." -ForegroundColor Yellow
$appsettings = Get-Content "appsettings.json" | ConvertFrom-Json
$connString = $appsettings.ConnectionStrings.PostgreSQL

if ($connString -match "Host=([^;]+);.*Database=([^;]+);.*Username=([^;]+);.*Password=([^;]+)") {
    $host = $matches[1]
    $database = $matches[2]
    $username = $matches[3]
    $password = $matches[4]
    
    Write-Host "   Connection details:" -ForegroundColor Gray
    Write-Host "   - Host: $host" -ForegroundColor Gray
    Write-Host "   - Database: $database" -ForegroundColor Gray
    Write-Host "   - Username: $username" -ForegroundColor Gray
    
    # Try to connect
    $env:PGPASSWORD = $password
    $testResult = & psql -h $host -U $username -d postgres -c "SELECT 1;" 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ? Database connection successful" -ForegroundColor Green
        
        # Check if vaani_db exists
        $dbExists = & psql -h $host -U $username -d postgres -t -c "SELECT 1 FROM pg_database WHERE datname='$database';" 2>&1
        if ($dbExists -match "1") {
            Write-Host "   ? Database '$database' exists" -ForegroundColor Green
        } else {
            Write-Host "   ?? Database '$database' does not exist" -ForegroundColor Yellow
            Write-Host "   Creating database..." -ForegroundColor Yellow
            & psql -h $host -U $username -d postgres -c "CREATE DATABASE $database;" 2>&1 | Out-Null
            Write-Host "   ? Database created" -ForegroundColor Green
            Write-Host "   Run schema: psql -h $host -U $username -d $database -f Database/schema.sql" -ForegroundColor Yellow
        }
    } else {
        Write-Host "   ?? Cannot connect to database (API will start in degraded mode)" -ForegroundColor Yellow
        Write-Host "   Error: $testResult" -ForegroundColor Red
    }
    
    Remove-Item Env:\PGPASSWORD
}

# Check 5: Build project
Write-Host ""
Write-Host "5. Building project..." -ForegroundColor Yellow
$buildResult = dotnet build --verbosity quiet 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ? Build successful" -ForegroundColor Green
} else {
    Write-Host "   ? Build failed" -ForegroundColor Red
    Write-Host "   Error:" -ForegroundColor Red
    Write-Host $buildResult -ForegroundColor Red
    exit 1
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   Diagnostic Complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Starting API..." -ForegroundColor Green
Write-Host ""

# Start the API
dotnet run
