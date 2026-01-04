# ?? FIXING 404 ERROR - Quick Guide

## Problem
`https://localhost:7020/swagger` returns **HTTP ERROR 404**

---

## ? Solution (Choose One)

### **Option 1: Quick Start (Recommended)**

Open PowerShell in Vaani.API folder and run:

```powershell
cd C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API
.\start-api.ps1
```

This script will:
- Check all prerequisites
- Fix common issues
- Start the API automatically

---

### **Option 2: Manual Start**

#### Step 1: Check if API is running

```powershell
# Check if anything is running on port 7020
netstat -an | findstr "7020"
```

If something is there, kill it:
```powershell
# Find process
Get-NetTCPConnection -LocalPort 7020 | Select-Object OwningProcess

# Kill process (replace PID with actual process ID)
Stop-Process -Id <PID> -Force
```

#### Step 2: Start the API

```powershell
cd C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API
dotnet run
```

You should see:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7020
      Now listening on: http://localhost:5114
```

#### Step 3: Open Swagger

Open browser: `https://localhost:7020`

If you see certificate warning, click "Advanced" ? "Proceed to localhost (unsafe)"

---

### **Option 3: Use HTTP Instead (If HTTPS Certificate Issues)**

Edit `Properties/launchSettings.json`, change the profile to use `http`:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "launchUrl": "swagger",
      "applicationUrl": "http://localhost:5114",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Then run:
```powershell
dotnet run --launch-profile http
```

Open: `http://localhost:5114/swagger`

---

## ?? Common Issues & Fixes

### Issue 1: "Certificate is not trusted"

**Fix:** Trust the development certificate

```powershell
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

### Issue 2: "Connection to database failed"

The API will still start! Database is optional for testing Swagger.

**To fix database connection:**

1. Check if PostgreSQL is installed:
```powershell
Get-Service -Name postgresql*
```

2. If not installed, download: https://www.postgresql.org/download/windows/

3. Update password in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=vaani_db;Username=postgres;Password=YOUR_POSTGRES_PASSWORD"
  }
}
```

4. Create database:
```powershell
psql -U postgres -c "CREATE DATABASE vaani_db;"
psql -U postgres -d vaani_db -f Database/schema.sql
```

### Issue 3: "Port 7020 already in use"

**Fix:** Kill the process using the port

```powershell
$conn = Get-NetTCPConnection -LocalPort 7020
Stop-Process -Id $conn.OwningProcess -Force
dotnet run
```

### Issue 4: Build errors

**Fix:** Restore packages and rebuild

```powershell
dotnet clean
dotnet restore
dotnet build
dotnet run
```

---

## ? Verify It's Working

Once the API starts, you should be able to access:

1. **Swagger UI:** https://localhost:7020
2. **Health Check:** https://localhost:7020/health
3. **OpenAPI JSON:** https://localhost:7020/swagger/v1/swagger.json

---

## ?? Test Without Database

You can test the API even without PostgreSQL:

1. Start the API: `dotnet run`
2. Open Swagger: https://localhost:7020
3. Test `/health` endpoint - should return `degraded` status
4. Swagger UI will work
5. Meeting validation will fail (needs database)

---

## ?? Still Not Working?

### Check the console output for errors:

```powershell
dotnet run --verbosity detailed
```

### Common error messages and fixes:

| Error | Fix |
|-------|-----|
| "Failed to bind to address" | Port is in use, kill the process |
| "Unable to configure HTTPS endpoint" | Trust dev certificate: `dotnet dev-certs https --trust` |
| "Connection refused to PostgreSQL" | Start PostgreSQL or update connection string |
| "Assembly not found" | Run `dotnet restore` |

---

## ?? Quick Test Checklist

Run these commands in order:

```powershell
# 1. Navigate to project
cd C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API

# 2. Clean and restore
dotnet clean
dotnet restore

# 3. Build
dotnet build

# 4. Trust certificate (if HTTPS issues)
dotnet dev-certs https --trust

# 5. Run
dotnet run

# 6. Open browser
start https://localhost:7020
```

---

## ?? Pro Tip: Use Visual Studio

1. Open `Vaani.API.csproj` in Visual Studio 2022
2. Press **F5** or click **Run**
3. Browser will open automatically with Swagger

This handles all certificate and debugging automatically!

---

**Last Updated:** December 2025  
**Status:** Troubleshooting Guide
