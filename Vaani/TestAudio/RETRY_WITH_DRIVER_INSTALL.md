# Audio Test - 3 Attempt Retry with Auto Driver Installation

## ?? Feature Overview

Implemented automatic retry logic with **3 attempts** for audio testing. If all 3 attempts fail, automatically opens the **Driver Installation window** to help users fix the issue.

---

## ?? How It Works

### **Retry Flow:**

```
Attempt 1: Test runs automatically
    ?
    ?? Success ? ? Main window opens
    ?? Failure ? ? Show retry button
         ?
Attempt 2: User clicks "Retry"
    ?
    ?? Success ? ? Main window opens
    ?? Failure ? ? Show retry button
         ?
Attempt 3: User clicks "Retry" (last chance)
    ?
    ?? Success ? ? Main window opens
    ?? Failure ? ? Auto-open Driver Installation window ??
```

---

## ?? Implementation Details

### **1. Added Retry Tracking**

```csharp
private int _testAttemptCount = 0;
private const int MaxTestAttempts = 3;
```

### **2. Updated StartTestAsync()**

```csharp
private async Task StartTestAsync()
{
    // Increment attempt count
    _testAttemptCount++;
    
    AppendLog($"\n?? Test Attempt {_testAttemptCount} of {MaxTestAttempts}");
    
    // ... run tests ...
    
    if (test fails)
    {
        ErrorMessage = $"Test failed. (Attempt {_testAttemptCount}/{MaxTestAttempts})";
        await HandleTestFailure();
        return;
    }
    
    // On success
    _testAttemptCount = 0; // Reset counter
    AllTestsPassed = true;
}
```

### **3. New HandleTestFailure() Method**

```csharp
private async Task HandleTestFailure()
{
    if (_testAttemptCount >= MaxTestAttempts)
    {
        // All 3 attempts failed
        AppendLog($"\n? All {MaxTestAttempts} test attempts failed.");
        AppendLog("?? Opening Driver Installation window...");
        
        await Task.Delay(1500);
        
        // Close test window
        testWindow.Close();
        
        // Open driver installation window
        var driverWindow = new DriverInstallationWindow();
        await driverWindow.ShowDialog(loginWindow);
    }
    else
    {
        // Still have attempts left
        int remaining = MaxTestAttempts - _testAttemptCount;
        AppendLog($"\n?? Test failed. {remaining} attempt(s) remaining.");
        AppendLog("Click 'Retry' to try again.");
    }
}
```

---

## ?? User Experience

### **Attempt 1 (Auto-start):**
```
?? Audio System Check
???????????????????????
?? Test Attempt 1 of 3

1. Detecting audio devices      ??
2. Testing outgoing audio       
3. Testing incoming audio       

[Progress bar running...]
```

### **Attempt 1 Failed:**
```
?? Audio System Check
???????????????????????
?? Test Attempt 1 of 3

1. Detecting audio devices      ?
2. Testing outgoing audio       
3. Testing incoming audio       

?? Test Failed
Audio devices not found. (Attempt 1/3)
Please check your VB Cable installation...

?? Test failed. 2 attempt(s) remaining.
Click 'Retry' to try again.

[?? Retry Button]
```

### **Attempt 2 (After Retry):**
```
?? Audio System Check
???????????????????????
?? Test Attempt 2 of 3

1. Detecting audio devices      ??
...
```

### **All 3 Attempts Failed:**
```
?? Audio System Check
???????????????????????
?? Test Attempt 3 of 3

1. Detecting audio devices      ?

?? Test Failed
Audio devices not found. (Attempt 3/3)

? All 3 test attempts failed.
?? Opening Driver Installation window...

[Window closes automatically]
[Driver Installation window opens]
```

---

## ?? Test Log Example

```
?? Test Attempt 1 of 3
Starting device detection...
? CABLE-A Input NOT found
?? Test failed. 2 attempt(s) remaining.
Click 'Retry' to try again.

?? Test Attempt 2 of 3
Starting device detection...
? CABLE-A Input NOT found
?? Test failed. 1 attempt(s) remaining.
Click 'Retry' to try again.

?? Test Attempt 3 of 3
Starting device detection...
? CABLE-A Input NOT found

? All 3 test attempts failed.
?? Opening Driver Installation window...
```

---

## ?? State Management

### **Attempt Counter:**

```
Initial:        _testAttemptCount = 0

First run:      _testAttemptCount = 1
First retry:    _testAttemptCount = 2
Second retry:   _testAttemptCount = 3

On success:     _testAttemptCount = 0 (reset)
After 3 fails:  Opens driver installation
```

### **Test Log:**

- **NOT cleared** between attempts
- Shows history of all attempts
- Helps user understand what failed

---

## ?? Smart Features

### **1. Progressive Error Messages**

```csharp
// Attempt 1
"Audio devices not found. (Attempt 1/3)"

// Attempt 2
"Audio devices not found. (Attempt 2/3)"

// Attempt 3
"Audio devices not found. (Attempt 3/3)"
```

### **2. Automatic Driver Installation**

After 3 failed attempts:
1. Shows message: "Opening Driver Installation window..."
2. Waits 1.5 seconds (user can read message)
3. Closes test window
4. Opens driver installation window
5. User can reinstall/repair driver
6. Can retry test after installation

### **3. Retry Button Visibility**

- **Shown:** After any failed attempt (< 3)
- **Hidden:** While test running
- **Hidden:** After 3rd failure (auto-opens driver window)

---

## ?? Success Scenarios

### **Scenario 1: Pass on First Try**
```
Attempt 1 ? ? Success ? Main window opens
```

### **Scenario 2: Fail Once, Pass Second**
```
Attempt 1 ? ? Failed
User clicks Retry
Attempt 2 ? ? Success ? Main window opens
```

### **Scenario 3: All 3 Attempts Fail**
```
Attempt 1 ? ? Failed ? Retry
Attempt 2 ? ? Failed ? Retry
Attempt 3 ? ? Failed ? Driver Installation window opens
User reinstalls driver
User retries login ? Test starts fresh (attempt count reset)
```

---

## ?? Technical Details

### **Files Modified:**

**TestAudioViewModel.cs:**
- Added `_testAttemptCount` field
- Added `MaxTestAttempts` constant (3)
- Updated `StartTestAsync()` to track attempts
- Added `HandleTestFailure()` method
- Modified `ResetTestState()` to preserve log
- Added driver installation window reference

**Changes:**
```diff
+ private int _testAttemptCount = 0;
+ private const int MaxTestAttempts = 3;

+ private async Task HandleTestFailure()
+ {
+     if (_testAttemptCount >= MaxTestAttempts)
+     {
+         // Open driver installation
+     }
+     else
+     {
+         // Show retry option
+     }
+ }
```

---

## ?? Configuration

### **Change Max Attempts:**

```csharp
// In TestAudioViewModel.cs
private const int MaxTestAttempts = 3;  // Change to 2, 4, 5, etc.
```

### **Change Delay Before Driver Window:**

```csharp
await Task.Delay(1500);  // Change to 1000, 2000, etc.
```

---

## ?? Comparison

| Feature | Before | After |
|---------|--------|-------|
| **Retry attempts** | Manual only | 3 automatic |
| **Attempt tracking** | None | Shown in UI |
| **After all failures** | User stuck | Auto driver install |
| **Test log** | Cleared on retry | Preserved |
| **Error messages** | Generic | Shows attempt count |
| **User guidance** | Minimal | Clear next steps |

---

## ? Benefits

### **For Users:**
- ? Clear attempt tracking (1/3, 2/3, 3/3)
- ? Know how many tries left
- ? Automatic help after failures
- ? Driver reinstall made easy
- ? Less frustration

### **For Support:**
- ? Test log shows all attempts
- ? Clear failure patterns
- ? Automatic driver installation prompt
- ? Reduced support tickets

---

## ?? Edge Cases Handled

### **1. User Closes Window Mid-Test**
- Attempt count preserved
- Can retry from login
- Test log preserved

### **2. User Closes After 3rd Failure**
- Driver window already shown
- Can manually retry later

### **3. Success After Multiple Failures**
- Attempt count reset to 0
- Ready for next session
- Clean slate

---

## ?? Testing Checklist

**Attempt Tracking:**
- [ ] Attempt 1 shows "1/3"
- [ ] Attempt 2 shows "2/3"
- [ ] Attempt 3 shows "3/3"
- [ ] Counter resets on success

**Retry Flow:**
- [ ] Retry button shows after failure
- [ ] Retry button works correctly
- [ ] Test log preserved between retries

**Driver Installation:**
- [ ] Opens after 3 failures
- [ ] Test window closes first
- [ ] Driver window shows correctly
- [ ] Can retry after installation

**Success Cases:**
- [ ] Pass on 1st attempt
- [ ] Pass on 2nd attempt
- [ ] Pass on 3rd attempt
- [ ] Counter resets after success

---

## ?? Summary

**Feature:**
- 3 automatic retry attempts for audio tests
- Progressive error messages with attempt count
- Automatic driver installation window after all failures
- Preserved test log for debugging

**User Flow:**
```
Test ? Fail (1/3) ? Retry
     ? Fail (2/3) ? Retry
     ? Fail (3/3) ? Auto Driver Installation
```

**Result:**
A robust, user-friendly testing system that automatically guides users to fix audio driver issues after multiple failures! ??

---

**Files Modified:**
- ? `TestAudioViewModel.cs` - Added retry logic and driver installation

**Build Status:** ? **SUCCESS**  
**Feature Status:** ? **Complete**  
**Max Attempts:** 3  
**Auto Driver Install:** ? **Enabled**
