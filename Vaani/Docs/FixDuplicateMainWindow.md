# Fix: Duplicate MainWindow Issue After Test Success

## Problem
When audio tests passed after login, two MainWindow instances were being created, causing duplicate windows to appear.

## Root Cause
The issue occurred because both `MeetingLoginViewModel` and `TestAudioViewModel` were creating MainWindow instances:

1. **Login Flow**: User logs in ? Audio test launches ? Test passes ? `TestAudioViewModel` creates MainWindow
2. **Login Continuation**: After test window closes ? `MeetingLoginViewModel.LaunchAudioTestAsync()` returns ? `OpenMainWindowAsync()` creates **another** MainWindow

This resulted in 2 MainWindows being created.

## Solution
Added an `isFromLogin` flag to distinguish between two launch scenarios:

### Launch Scenarios:

#### 1. **From Login Flow** (`isFromLogin = true`)
- User logs in with valid credentials
- TestAudioWindow is launched with `isFromLogin = true`
- When tests pass, TestAudioViewModel **does NOT** create MainWindow
- Window just closes and returns control to MeetingLoginViewModel
- MeetingLoginViewModel then handles navigation (creates MainWindow once)

#### 2. **Direct Launch** (`isFromLogin = false`)
- User launches test from driver installation
- User launches test from app startup (no session)
- TestAudioWindow is launched with `isFromLogin = false`
- When tests pass, TestAudioViewModel creates appropriate window based on session state

## Files Modified

### 1. `Vaani\TestAudio\ViewModels\TestAudioViewModel.cs`
```csharp
// Added field
private readonly bool _isFromLogin;

// Updated constructor
public TestAudioViewModel(bool isFromLogin = false)
{
    // ...
    _isFromLogin = isFromLogin;
    // ...
}

// Updated navigation logic
if (!_isFromLogin)
{
    // Create MainWindow or LoginWindow based on session
}
else
{
    // Just close - parent will handle navigation
}
```

### 2. `Vaani\TestAudio\Views\TestAudioWindow.axaml.cs`
```csharp
public TestAudioWindow(bool isFromLogin = false)
{
    InitializeComponent();
    DataContext = new TestAudioViewModel(isFromLogin);
    // ...
}
```

### 3. `Vaani\Authentication\ViewModels\MeetingLoginViewModel.cs`
```csharp
private async Task<bool> LaunchAudioTestAsync()
{
    // ...
    var testWindow = new TestAudioWindow(isFromLogin: true);
    // ...
}
```

## Flow Diagram

### Before Fix (Duplicate Windows)
```
Login ? Audio Test ? Test Passes
   ?         ?
   ?         ??> Creates MainWindow #1
   ?
   ??> Test Closes ? Creates MainWindow #2 ?
```

### After Fix (Single Window)
```
Login (isFromLogin=true) ? Audio Test ? Test Passes
   ?                            ?
   ?                            ??> Just closes (no window creation)
   ?
   ??> Test Closed ? Creates MainWindow #1 ?
```

### Direct Launch (Still Works)
```
App Start (no session) ? Audio Test (isFromLogin=false) ? Test Passes
                              ?
                              ??> Creates LoginWindow ?

Driver Install ? Audio Test (isFromLogin=false) ? Test Passes
                      ?
                      ??> Creates MainWindow ? (if session exists)
```

## Testing Checklist

- [ ] Login flow ? Audio test passes ? Single MainWindow appears
- [ ] Login flow ? Audio test fails ? Returns to login window
- [ ] App start (no session) ? Driver install ? Test passes ? Login window appears
- [ ] App start (with session) ? Test passes ? MainWindow appears
- [ ] Driver reinstall ? Test passes ? MainWindow appears (if session)
- [ ] All scenarios only create one target window

## Benefits

1. ? No duplicate windows
2. ? Cleaner navigation flow
3. ? Proper separation of concerns
4. ? Backward compatible (defaults to false)
5. ? Works for all launch scenarios
