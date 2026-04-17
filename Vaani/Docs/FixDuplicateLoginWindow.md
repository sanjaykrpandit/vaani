# Fix: Duplicate Login Window After Driver Installation Test Success

## Problem
When the app flow goes: **App Start ? Login Window ? Driver Installation ? Test Window ? Test Success**, a **new** login window was being created instead of showing the existing (hidden) one.

## Root Cause Analysis

### App Startup Flow
```
1. App.axaml.cs creates MeetingLoginWindow
2. MeetingLoginWindow.OnWindowLoadedAsync() checks if driver is installed
3. If not installed, shows DriverInstallationWindow (hides login window)
4. After driver install, launches TestAudioWindow (driver window hidden)
5. Test passes ? TestAudioViewModel creates NEW MeetingLoginWindow ?
```

### The Problem
In `TestAudioViewModel.StartTestAsync()`, when `!_isFromLogin`:
```csharp
// WRONG: Always creates a new login window
var loginWindow = new MeetingLoginWindow();
```

This created a duplicate login window because the original one from App startup was just hidden, not closed.

## Solution

### Updated Code in `TestAudioViewModel.cs`

```csharp
// Only navigate if NOT launched from login flow
if (!_isFromLogin)
{
    await Dispatcher.UIThread.InvokeAsync(() =>
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var testWindow = desktop.Windows?.FirstOrDefault(w => w is TestAudioWindow);
            
            // ? Check if there's an existing login window (from driver installation flow)
            var existingLoginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);
            
            if (existingLoginWindow != null)
            {
                // ? Login window exists, just show it again
                testWindow?.Hide();
                existingLoginWindow.Show();
                testWindow?.Close();
            }
            else
            {
                // ? No login window exists, create a new one
                var loginWindow = new MeetingLoginWindow();
                testWindow?.Hide();
                desktop.MainWindow = loginWindow;
                loginWindow.Show();
                testWindow?.Close();
            }
        }
    });
}
```

## Flow Comparison

### Before Fix ?
```
App Start
  ??> Creates LoginWindow #1
       ??> Shows DriverInstallation (hides LoginWindow #1)
            ??> Shows TestAudio
                 ??> Test Success
                      ??> Creates LoginWindow #2 ? (duplicate!)
```

### After Fix ?
```
App Start
  ??> Creates LoginWindow #1
       ??> Shows DriverInstallation (hides LoginWindow #1)
            ??> Shows TestAudio
                 ??> Test Success
                      ??> Shows LoginWindow #1 again ? (reuses existing)
```

## Scenarios Covered

| Scenario | Existing Login Window? | Behavior |
|----------|------------------------|----------|
| **App Start ? Driver ? Test** | ? Yes (from App.axaml.cs) | Reuses existing window |
| **Test launched independently** | ? No | Creates new login window |
| **Direct test from elsewhere** | Depends | Checks and handles both cases |

## Files Modified

### `Vaani\TestAudio\ViewModels\TestAudioViewModel.cs`
- Added check for existing `MeetingLoginWindow` before creating a new one
- Reuses hidden login window if it exists
- Only creates new login window if none exists

## Benefits

1. ? **No Duplicate Windows**: Reuses existing login window
2. ? **Proper Window Management**: Maintains window state consistency
3. ? **Memory Efficient**: Doesn't create unnecessary window instances
4. ? **Better UX**: Maintains user's previous login state/context
5. ? **Backward Compatible**: Still creates new window when needed

## Testing Checklist

- [x] App start ? No driver ? Driver install ? Test pass ? Show original login window
- [x] App start ? Driver exists ? Login ? Test ? No duplicate windows
- [x] Test launched independently ? Creates new login window if none exists
- [x] All flows properly hide/show windows without duplicates

## Related Fixes

This fix complements the earlier fix for duplicate MainWindow issue:
- **Previous Fix**: Prevented duplicate MainWindow when coming from login flow
- **This Fix**: Prevents duplicate LoginWindow when coming from driver installation flow

Both work together to ensure clean window navigation throughout the app.
