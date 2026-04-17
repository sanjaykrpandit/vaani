# Single Instance Application Feature

## Overview
Vaani now enforces single instance execution - only one instance of the application can run on a machine at any given time.

## Implementation Details

### SingleInstanceService
- **Location**: `Vaani\Services\SingleInstanceService.cs`
- **Mechanism**: Uses a named `Mutex` with a global scope to ensure only one instance can acquire the lock
- **Mutex Name**: `Global\\VaaniAudioTranscriptionApp_SingleInstance_F5E2A8B1`

### Behavior

#### First Instance
- Application starts normally
- Mutex is acquired successfully
- User can proceed with login/main application flow

#### Subsequent Instances
- Application detects that another instance is already running
- Shows a visual message window informing the user
- Automatically exits without starting the full application
- If visual message fails, falls back to console message

### User Experience

When a user tries to launch Vaani while it's already running:
1. A dialog window appears with the message:
   - **Title**: "Vaani - Already Running"
   - **Icon**: Warning icon (??)
   - **Message**: "Vaani is already running on this machine. Please close the existing instance before starting a new one, or switch to the running instance."
2. User clicks "OK" to dismiss
3. The second instance closes automatically

### Files Modified/Created

1. **Created**: `Vaani\Services\SingleInstanceService.cs`
   - Core single instance enforcement service
   - Uses Mutex for cross-process synchronization

2. **Created**: `Vaani\Views\AlreadyRunningWindow.axaml`
   - Visual message window XAML

3. **Created**: `Vaani\Views\AlreadyRunningWindow.axaml.cs`
   - Visual message window code-behind

4. **Modified**: `Vaani\Program.cs`
   - Added single instance check in Main method
   - Shows message window when duplicate instance detected
   - Proper cleanup of mutex on application exit

## Technical Notes

- The mutex uses a `Global\` prefix to work across all user sessions on the machine
- Handles `AbandonedMutexException` gracefully (occurs when previous instance crashed)
- Thread-safe implementation with proper disposal pattern
- Minimal performance impact (single mutex check at startup)

## Testing

To test the single instance feature:
1. Launch Vaani normally
2. Try to launch Vaani again while the first instance is running
3. Verify the "Already Running" message appears
4. Close the message and verify the second instance exits
5. Close the first instance
6. Launch Vaani again - it should start normally

## Edge Cases Handled

- **Crashed Previous Instance**: If a previous instance crashed without releasing the mutex, the new instance will acquire the abandoned mutex and start normally
- **Visual Message Failure**: If the Avalonia UI fails to initialize for the message, falls back to console output
- **Proper Cleanup**: Mutex is always released in the `finally` block to prevent lock leaks
