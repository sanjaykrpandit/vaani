# ?? STEP-BY-STEP: Fix 404 Error

## Your Problem
Browser shows: `https://localhost:7020/swagger` ? **HTTP ERROR 404**

## Root Cause
**The API is not running at all.** The 404 error means there's no web server listening on port 7020.

---

## ? SOLUTION (Follow These Exact Steps)

### **Step 1: Open File Explorer**

Navigate to:
```
C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API
```

### **Step 2: Double-Click `START_API.bat`**

You should see a black command window open with messages like:
```
========================================
   Starting Vaani API
========================================

[1/3] Checking .NET SDK...
OK - .NET SDK found

[2/3] Building project...
OK - Build successful

[3/3] Starting API...
```

Then you'll see:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7020
      Now listening on: http://localhost:5114
```

**?? IMPORTANT: Keep this window open! Do NOT close it!**

### **Step 3: Open Browser**

Now that the API is running, open your browser and go to:
```
https://localhost:7020
```

**If you see a certificate warning:**
- Click "Advanced"
- Click "Proceed to localhost (unsafe)"

**You should now see Swagger UI! ??**

---

## ?? What If It Still Doesn't Work?

### **Problem A: Certificate Error in Browser**

**Solution:**
1. Close the command window (Ctrl+C)
2. Open PowerShell as Administrator
3. Run:
   ```powershell
   dotnet dev-certs https --clean
   dotnet dev-certs https --trust
   ```
4. Click "Yes" when prompted
5. Double-click `START_API.bat` again

### **Problem B: Port Already in Use**

If you see error: "Failed to bind to address... port is in use"

**Solution:**
1. Open PowerShell as Administrator
2. Run:
   ```powershell
   Get-NetTCPConnection -LocalPort 7020 | Select-Object OwningProcess
   Stop-Process -Id <ProcessID> -Force
   ```
3. Replace `<ProcessID>` with the number shown
4. Double-click `START_API.bat` again

### **Problem C: Build Errors**

If you see "Build failed"

**Solution:**
1. Open PowerShell in Vaani.API folder
2. Run:
   ```powershell
   dotnet clean
   dotnet restore
   dotnet build
   ```
3. Double-click `START_API.bat` again

### **Problem D: Database Connection Error**

If you see error about PostgreSQL connection:

**Don't worry!** The API will still start. You'll see:
```
?? Cannot connect to database (API will start in degraded mode)
```

This is fine for testing Swagger. The API will work, but:
- ? Swagger UI will work
- ? Health endpoint will work  
- ? Meeting validation will fail (needs database)

---

## ?? Alternative: Use Visual Studio

If batch file doesn't work, use Visual Studio:

1. Open Visual Studio 2022
2. File ? Open ? Project/Solution
3. Navigate to: `C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API`
4. Open `Vaani.API.csproj`
5. Press **F5** or click the green "Play" button
6. Browser will open automatically with Swagger

---

## ?? Verification Checklist

Once the API starts, verify these work:

### ? Check 1: Command Window Shows "Now listening on"
```
? Should see: Now listening on: https://localhost:7020
? If not: API didn't start - check error messages
```

### ? Check 2: Health Endpoint
Open browser: `https://localhost:7020/health`
```
? Should see: {"status":"healthy" or "degraded",...}
? If not: API is not running
```

### ? Check 3: Swagger UI
Open browser: `https://localhost:7020`
```
? Should see: Swagger interface with API endpoints
? If not: Certificate issue or wrong URL
```

---

## ?? Manual Start (If Batch File Fails)

Open PowerShell and run these commands **one by one**:

```powershell
# Navigate to project
cd C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API

# Trust certificate
dotnet dev-certs https --trust

# Build
dotnet build

# Run (keep this running)
dotnet run
```

**Wait for:** "Now listening on: https://localhost:7020"

**Then open browser:** https://localhost:7020

---

## ?? What Success Looks Like

### In Command Window:
```
info: Vaani.API.Program[0]
      Starting Vaani API...
info: Vaani.API.Program[0]
      Environment: Development
info: Vaani.API.Program[0]
      ? Database connection successful
      (or: ?? Cannot connect to database)
info: Vaani.API.Program[0]
      ?? Swagger UI available at: https://localhost:7020
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7020
info: Vaani.API.Program[0]
      ? Vaani API started successfully
```

### In Browser:
You'll see **Swagger UI** with sections:
- Health (1 endpoint)
- Meetings (2 endpoints)
- Sessions (3 endpoints)

---

## ? Common Questions

**Q: Do I need PostgreSQL installed?**
A: No! The API will start without it. You'll just see a warning.

**Q: Why do I need to keep the command window open?**
A: That window is running the API server. Closing it stops the API.

**Q: Can I use HTTP instead of HTTPS?**
A: Yes! Open `http://localhost:5114` (note the different port)

**Q: How do I stop the API?**
A: Press Ctrl+C in the command window, or just close the window.

---

## ?? Still Not Working?

### Last Resort Steps:

1. **Completely restart:**
   ```powershell
   cd C:\Users\sp250293\Documents\Workspace\spdotnet\Vaani.API
   dotnet clean
   dotnet restore
   dotnet build
   dotnet dev-certs https --clean
   dotnet dev-certs https --trust
   dotnet run
   ```

2. **Check for antivirus/firewall:**
   - Temporarily disable antivirus
   - Allow dotnet.exe through Windows Firewall

3. **Try different browser:**
   - Chrome
   - Edge
   - Firefox

4. **Use HTTP instead:**
   ```powershell
   dotnet run --launch-profile http
   ```
   Then open: `http://localhost:5114`

---

## ?? Need More Help?

Check these files in the Vaani.API folder:
- `TROUBLESHOOTING.md` - Detailed troubleshooting
- `QUICK_REFERENCE.md` - Command reference
- `README.md` - Full documentation

---

## ? SUCCESS INDICATOR

**You'll know it's working when:**
1. ? Command window says "Now listening on: https://localhost:7020"
2. ? Browser shows Swagger UI (not 404 error)
3. ? You can see API endpoints listed

**That's it! The API should now be accessible.** ??

---

**Remember:** The command window must stay open while you're using the API!
