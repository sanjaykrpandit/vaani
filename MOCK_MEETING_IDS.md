# Vaani - Mock Meeting IDs for Testing

## ?? Development Mode

The application is currently running in **MOCK MODE** (bypassing the backend API).

To switch to production mode when the backend API is ready:
1. Open `Authentication/Services/MeetingAuthenticationService.cs`
2. Change `USE_MOCK_MODE = true` to `USE_MOCK_MODE = false`
3. Uncomment the real API call sections

---

## ?? Valid Test Meeting IDs

Use any of these meeting IDs to test the application:

| Meeting ID | Meeting Name | Duration | Description |
|-----------|--------------|----------|-------------|
| **VAANI-TEST-001** | Test Meeting - English to Hindi | 2 hours | General testing |
| **VAANI-TEST-002** | Test Meeting - Sales Call | 2 hours | Sales scenario |
| **VAANI-DEMO-123** | Demo Meeting - Product Presentation | 2 hours | Demo/presentation |
| **VM-2025-1220-A7B3** | Vendor Discussion Meeting | 2 hours | Vendor meeting |

---

## ?? Testing Instructions

### Step 1: Launch Vaani
```sh
dotnet run
```

### Step 2: Login Window Appears
- The meeting login window will appear automatically
- Enter one of the valid meeting IDs listed above
- Example: `VAANI-TEST-001`

### Step 3: Validate
- Click **"? Validate"** or press **Enter**
- The app will simulate API validation (1 second delay)
- On success, the main translation window will open

### Step 4: Check Session Info
- Look at the bottom-left of the main window
- You should see:
  - Meeting name: "Test Meeting - English to Hindi"
  - Session expiry: "Expires in: 2 hours"

---

## ?? Testing Different Scenarios

### ? Valid Meeting ID
```
Enter: VAANI-TEST-001
Result: ? Success ? Main window opens
```

### ? Invalid Meeting ID
```
Enter: INVALID-123
Result: ? Error ? "Meeting ID not found. Try: VAANI-TEST-001"
```

### ?? Session Reconnection
1. Close the app after successful login
2. Reopen the app
3. The session should be automatically restored (if not expired)
4. Main window opens directly without login

---

## ?? Mock Configuration Details

When you enter a valid meeting ID in mock mode, the system uses:

### Azure Configuration
- **Subscription Key**: Your existing Azure key from `ClientService`
- **Region**: `eastus2`

### Translation Settings
- **Vendor Language**: Hindi (hi-IN)
- **Vendor Voice**: Madhur (Natural)
- **Organizer Language**: English (en-US)
- **Organizer Voice**: Guy (Natural)

### Session Settings
- **Duration**: 2 hours from login
- **Heartbeat**: Every 60 seconds
- **Reconnection**: Enabled
- **Local Cache**: Disabled

---

## ?? Switching to Production Mode

When your backend API is ready:

### 1. Update MeetingAuthenticationService.cs
```csharp
// Change this line:
private const bool USE_MOCK_MODE = false; // ? Set to false
```

### 2. Uncomment Real API Calls
Find and uncomment these sections:
- `ValidateMeetingAsync()` - Real API validation
- `SendHeartbeatAsync()` - Real heartbeat call
- `EndSessionAsync()` - Real session end call

### 3. Update API Base URL
In `appsettings.json`:
```json
{
  "Authentication": {
    "ApiBaseUrl": "https://your-actual-api.com",
    "ApiTimeout": 30
  }
}
```

---

## ?? Expected Behavior

### On App Launch
```
1. Check for saved session
   ?? Session exists & valid ? Open main window
   ?? No session or expired ? Show login window

2. User enters meeting ID
   ?? Validate with mock/API

3. On validation success:
   ?? Create session
   ?? Save to secure storage (DPAPI)
   ?? Start heartbeat timer
   ?? Start expiry monitor
   ?? Open main window

4. During session:
   ?? Heartbeat every 60 seconds
   ?? Check expiry every 30 seconds
   ?? Show warning at 5 minutes remaining

5. On session expiry:
   ?? Stop translation if running
   ?? Clear session
   ?? Show expiry message
   ?? (Optional) Navigate back to login
```

---

## ?? Troubleshooting

### Login window doesn't appear
- Check `App.axaml.cs` - session check logic
- Delete saved session: `%APPDATA%\Vaani\*.dat`

### "Meeting ID not found" error
- Use exact meeting IDs listed above
- IDs are case-insensitive

### Session not persisting
- Check Windows user permissions
- DPAPI requires Windows user account

### Main window opens but no session info
- Check `MainViewModel.ShowSessionInfo` property
- Verify `TranslationSettings.IsFromMeetingSession` is true

---

## ?? Tips

1. **Quick Testing**: Use `VAANI-TEST-001` - shortest and easiest to remember
2. **Clipboard**: Use the ?? Paste button to paste meeting IDs
3. **Session Storage**: Located at `%APPDATA%\Vaani\current_session.dat`
4. **Clear Session**: Delete the `.dat` file to force re-login
5. **Expiry Testing**: Change session duration in mock validation to test expiry warnings

---

## ?? Next Steps

1. ? Test with all mock meeting IDs
2. ? Verify session persistence (close/reopen app)
3. ? Test session expiry warnings
4. ? Build backend API
5. ? Implement real encryption/decryption
6. ? Switch to production mode

---

**Last Updated**: December 2025  
**Mock Mode**: Active  
**Backend API**: Not required for testing
