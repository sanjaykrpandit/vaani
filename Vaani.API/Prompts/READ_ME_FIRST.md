# ? READ THIS FIRST - Quick Start Guide

## ?? Your Goal
Get Vaani API running at: **https://localhost:7020**

---

## ?? FASTEST WAY (30 Seconds)

### Method 1: Double-Click Batch File ? RECOMMENDED

1. Open File Explorer
2. Go to: `C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API`
3. **Double-click:** `START_API.bat`
4. Wait for: "Now listening on: https://localhost:7020"
5. Open browser: https://localhost:7020

**? Done! Swagger UI should appear.**

---

### Method 2: Visual Studio (If Method 1 Fails)

1. Open Visual Studio 2022
2. File ? Open ? Project/Solution
3. Open: `C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API\Vaani.API.csproj`
4. Press **F5** (or click green Play button)
5. Browser opens automatically

**? Done! Swagger UI should appear.**

---

### Method 3: PowerShell Commands

```powershell
cd C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API
dotnet run
```

Wait for "Now listening..." then open browser: https://localhost:7020

---

## ?? IMPORTANT NOTES

### 1. Keep Command Window Open
- The black window that opens is the API server
- **DO NOT CLOSE IT** while using the API
- To stop: Press Ctrl+C or close window

### 2. Certificate Warning in Browser
If browser shows "Your connection is not private":
- Click "Advanced"
- Click "Proceed to localhost (unsafe)"
- This is normal for local development

### 3. PostgreSQL Not Required for Testing
- API will start without database
- You'll see: "?? Cannot connect to database"
- **This is OK!** Swagger will still work
- Only meeting validation needs database

---

## ?? Success Checklist

You'll know it's working when you see:

? **In Command Window:**
```
Now listening on: https://localhost:7020
Now listening on: http://localhost:5114
```

? **In Browser:**
- Swagger UI interface appears
- You see sections: Health, Meetings, Sessions
- **NOT** a 404 error page

---

## ?? Troubleshooting

### If Nothing Happens When You Double-Click Batch File:

1. Right-click `START_API.bat`
2. Select "Run as administrator"

### If You See "Port 7020 already in use":

```powershell
# Open PowerShell as Administrator
Get-NetTCPConnection -LocalPort 7020
Stop-Process -Id <ProcessID> -Force
```

### If You See "Certificate not trusted":

```powershell
# Open PowerShell as Administrator
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```
Click "Yes" when prompted.

### If Build Fails:

```powershell
cd C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API
dotnet clean
dotnet restore
dotnet build
```

---

## ?? What to Do After API Starts

### 1. Test Health Endpoint
Browser: https://localhost:7020/health

Expected response:
```json
{
  "status": "healthy",
  "database": "connected"
}
```
(or "degraded" if no database - that's OK!)

### 2. Explore Swagger
Browser: https://localhost:7020

- Try the `/health` endpoint (GET)
- Expand each section to see endpoints
- Click "Try it out" to test

### 3. Test Validation (If Database Setup)
In Swagger:
1. Expand `POST /api/meetings/validate`
2. Click "Try it out"
3. Enter meeting ID: `VAANI-TEST-001`
4. Enter device ID: `test-123`
5. Click "Execute"

---

## ?? Additional Documentation

If you need more details, check these files:

| File | Purpose |
|------|---------|
| `FIX_404_GUIDE.md` | Detailed 404 fix steps |
| `TROUBLESHOOTING.md` | Common issues and solutions |
| `QUICK_REFERENCE.md` | Commands and tips |
| `README.md` | Complete documentation |
| `Database/README.md` | Database setup (optional) |

---

## ?? Key Concepts

### Why Do I Need to Keep the Window Open?
The command window is running the web server. When you close it, the server stops.

### What's the Difference Between HTTP and HTTPS?
- HTTPS (port 7020): Secure, requires certificate
- HTTP (port 5114): Not secure, no certificate needed
- Both work for local testing

### Do I Need PostgreSQL?
- **For Swagger testing:** No
- **For full API functionality:** Yes
- See `Database/README.md` to set up later

---

## ? You're Ready!

**Next Steps:**
1. ? Start API using Method 1, 2, or 3 above
2. ? Open https://localhost:7020 in browser
3. ? Explore Swagger UI
4. ? (Optional) Set up database later

---

## ?? Still Having Issues?

### Quick Diagnosis:

Run this in PowerShell:
```powershell
cd C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API
dotnet --version
dotnet build
```

If both succeed, the problem is likely:
- Port conflict
- Certificate issue
- Antivirus/Firewall blocking

See `FIX_404_GUIDE.md` for detailed solutions.

---

## ?? Contact

For issues:
- GitHub: https://github.com/sanjaykrpandit/vaani/issues
- Check troubleshooting docs in this folder

---

**Last Updated:** December 2025  
**Quick Start Version:** 1.0  

---

## ?? That's It!

**The API is simple to start:**
1. Double-click `START_API.bat`
2. Open https://localhost:7020

**Everything else is optional!**
