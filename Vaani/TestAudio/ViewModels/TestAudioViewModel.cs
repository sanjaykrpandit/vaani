using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using System;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vaani.Authentication.Models;
using Vaani.Authentication.Services;
using Vaani.Authentication.Views;
using Vaani.DriverInstallation.Views;
using Vaani.TestAudio.Models;
using Vaani.TestAudio.Services;
using Vaani.TestAudio.Views;
using Vaani.Views;

namespace Vaani.TestAudio.ViewModels;

public class TestAudioViewModel : ReactiveObject
{
    private readonly AudioLoopbackTestService _testService;
    private CancellationTokenSource? _cts;
    private readonly bool _isFromLogin; // Flag to indicate if launched from login flow
    private readonly SessionManager _sessionManager; // Cache to avoid repeated instantiation

    // Test state
    private TestPhase _currentPhase = TestPhase.NotStarted;
    private bool _isRunning;
    private string _statusMessage = "Click 'Start Test' to begin audio verification";
    private double _progressValue = 0;

    // Retry logic
    private int _testAttemptCount = 0;
    private const int MaxTestAttempts = 2   ;

    // Device detection state
    private bool _isDeviceDetectionRunning;
    private bool _isDeviceDetectionPassed;
    private bool _isDeviceDetectionFailed;

    // Cable-A test state
    private bool _isCableATestRunning;
    private bool _isCableATestPassed;
    private bool _isCableATestFailed;

    // Cable-B test state
    private bool _isCableBTestRunning;
    private bool _isCableBTestPassed;
    private bool _isCableBTestFailed;

    // Error handling
    private bool _hasError;
    private string _errorMessage = string.Empty;

    // Results (kept for internal use)
    private bool _isDeviceDetectionComplete;
    private string _deviceDetectionDetails = string.Empty;
    private bool _isCableATestComplete;
    private string _cableATestDetails = string.Empty;
    private bool _isCableBTestComplete;
    private string _cableBTestDetails = string.Empty;

    // Test log
    private string _testLog = string.Empty;

    // Overall result
    private bool _allTestsPassed;
    private bool _canContinue;

    public TestAudioViewModel(bool isFromLogin = false)
    {
        _testService = new AudioLoopbackTestService();
        _testService.LogMessage += OnLogMessage;
        _testService.AudioLevelChanged += OnAudioLevelChanged;
        _isFromLogin = isFromLogin;
        _sessionManager = new SessionManager(); // Initialize once

        StartTestCommand = ReactiveCommand.CreateFromTask(StartTestAsync, 
            this.WhenAnyValue(x => x.IsRunning, running => !running));
        
        RetryCommand = ReactiveCommand.CreateFromTask(StartTestAsync,
            this.WhenAnyValue(x => x.IsRunning, x => x.AllTestsPassed, 
                (running, passed) => !running && !passed));
        
        ContinueCommand = ReactiveCommand.Create(() => { }, 
            this.WhenAnyValue(x => x.CanContinue));
    }

    #region Properties

    public TestPhase CurrentPhase
    {
        get => _currentPhase;
        set => this.RaiseAndSetIfChanged(ref _currentPhase, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    // Device Detection
    public bool IsDeviceDetectionRunning
    {
        get => _isDeviceDetectionRunning;
        set => this.RaiseAndSetIfChanged(ref _isDeviceDetectionRunning, value);
    }

    public bool IsDeviceDetectionPassed
    {
        get => _isDeviceDetectionPassed;
        set => this.RaiseAndSetIfChanged(ref _isDeviceDetectionPassed, value);
    }

    public bool IsDeviceDetectionFailed
    {
        get => _isDeviceDetectionFailed;
        set => this.RaiseAndSetIfChanged(ref _isDeviceDetectionFailed, value);
    }

    // Cable-A Test
    public bool IsCableATestRunning
    {
        get => _isCableATestRunning;
        set => this.RaiseAndSetIfChanged(ref _isCableATestRunning, value);
    }

    public bool IsCableATestPassed
    {
        get => _isCableATestPassed;
        set => this.RaiseAndSetIfChanged(ref _isCableATestPassed, value);
    }

    public bool IsCableATestFailed
    {
        get => _isCableATestFailed;
        set => this.RaiseAndSetIfChanged(ref _isCableATestFailed, value);
    }

    // Cable-B Test
    public bool IsCableBTestRunning
    {
        get => _isCableBTestRunning;
        set => this.RaiseAndSetIfChanged(ref _isCableBTestRunning, value);
    }

    public bool IsCableBTestPassed
    {
        get => _isCableBTestPassed;
        set => this.RaiseAndSetIfChanged(ref _isCableBTestPassed, value);
    }

    public bool IsCableBTestFailed
    {
        get => _isCableBTestFailed;
        set => this.RaiseAndSetIfChanged(ref _isCableBTestFailed, value);
    }

    // Error Handling
    public bool HasError
    {
        get => _hasError;
        set => this.RaiseAndSetIfChanged(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    // Device Detection (internal)
    public bool IsDeviceDetectionComplete
    {
        get => _isDeviceDetectionComplete;
        set => this.RaiseAndSetIfChanged(ref _isDeviceDetectionComplete, value);
    }

    public string DeviceDetectionDetails
    {
        get => _deviceDetectionDetails;
        set => this.RaiseAndSetIfChanged(ref _deviceDetectionDetails, value);
    }

    // Cable-A Test (internal)
    public bool IsCableATestComplete
    {
        get => _isCableATestComplete;
        set => this.RaiseAndSetIfChanged(ref _isCableATestComplete, value);
    }

    public string CableATestDetails
    {
        get => _cableATestDetails;
        set => this.RaiseAndSetIfChanged(ref _cableATestDetails, value);
    }

    // Cable-B Test (internal)
    public bool IsCableBTestComplete
    {
        get => _isCableBTestComplete;
        set => this.RaiseAndSetIfChanged(ref _isCableBTestComplete, value);
    }

    public string CableBTestDetails
    {
        get => _cableBTestDetails;
        set => this.RaiseAndSetIfChanged(ref _cableBTestDetails, value);
    }

    // Test Log
    public string TestLog
    {
        get => _testLog;
        set => this.RaiseAndSetIfChanged(ref _testLog, value);
    }

    // Overall Result
    public bool AllTestsPassed
    {
        get => _allTestsPassed;
        set => this.RaiseAndSetIfChanged(ref _allTestsPassed, value);
    }

    public bool CanContinue
    {
        get => _canContinue;
        set => this.RaiseAndSetIfChanged(ref _canContinue, value);
    }

    #endregion

    #region Commands

    public ICommand StartTestCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand ContinueCommand { get; }

    #endregion

    #region Test Execution

    private async Task StartTestAsync()
    {
        // Increment attempt count
        _testAttemptCount++;

        // Reset state
        ResetTestState();

        _cts = new CancellationTokenSource();
        IsRunning = true;
        HasError = false;

        try
        {
            AppendLog($"\n?? Test Attempt {_testAttemptCount} of {MaxTestAttempts}");
            // Phase 1: Device Detection
            IsDeviceDetectionRunning = true;
            ProgressValue = 10;
            await RunDeviceDetectionAsync();
            IsDeviceDetectionRunning = false;

            if (!IsDeviceDetectionComplete || !IsDeviceDetectionPassed)
            {
                IsDeviceDetectionFailed = true;
                HasError = true;
                ErrorMessage = $"Audio devices not found.";// (Attempt {_testAttemptCount}/{MaxTestAttempts})
                CurrentPhase = TestPhase.Failed;
                IsRunning = false;

                // Check if we should show driver installation
                await HandleTestFailure();
                return;
            }

            ProgressValue = 33;
            await Task.Delay(100); // Brief pause to show success

            // Phase 2: Cable-A Test
            IsCableATestRunning = true;
            ProgressValue = 40;
            await RunCableATestAsync(_cts.Token);
            IsCableATestRunning = false;

            if (!IsCableATestComplete || !IsCableATestPassed)
            {
                IsCableATestFailed = true;
                HasError = true;
                ErrorMessage = $"Outgoing audio test failed."; //(Attempt { _testAttemptCount}/{ MaxTestAttempts})
                CurrentPhase = TestPhase.Failed;
                IsRunning = false;

                // Check if we should show driver installation
                await HandleTestFailure();
                return;
            }

            ProgressValue = 66;
            await Task.Delay(100); // Brief pause to show success

            // Phase 3: Cable-B Test
            IsCableBTestRunning = true;
            ProgressValue = 75;
            await RunCableBTestAsync(_cts.Token);
            IsCableBTestRunning = false;

            if (!IsCableBTestComplete || !IsCableBTestPassed)
            {
                IsCableBTestFailed = true;
                HasError = true;
                ErrorMessage = $"Incoming audio test failed.";//(Attempt {_testAttemptCount}/{MaxTestAttempts})
                CurrentPhase = TestPhase.Failed;
                IsRunning = false;

                // Check if we should show driver installation
                await HandleTestFailure();
                return;
            }

            ProgressValue = 100;
            await Task.Delay(500); // Brief pause to show all checkmarks

            // All tests passed!
            CurrentPhase = TestPhase.Completed;
            AllTestsPassed = true;
            CanContinue = true;
            AppendLog($"\n✅ All tests passed on attempt {_testAttemptCount}!");
            AppendLog("✅ Audio system is working correctly.");

            // Reset attempt count on success
            _testAttemptCount = 0;

            // Auto-redirect to appropriate window after brief delay
            await Task.Delay(800); // Reduced from 1500ms for faster navigation

            // Only navigate if NOT launched from login flow
            // (Login flow handles navigation itself)
            if (!_isFromLogin)
            {
                // Check session state before deciding where to navigate (use cached instance)
                bool hasValidSession = _sessionManager.LoadSession() && _sessionManager.HasActiveSession();
                
                if (hasValidSession)
                {
                    // Valid session exists, navigate to main window
                    AppendLog("Valid session found, navigating to Main Window...");
                    await OpenMainWindowAsync(null);
                }
                else
                {
                    // No valid session, navigate to login window
                    AppendLog("No valid session, navigating to Login Window...");
                    await OpenLoginWindowAsync(null);
                }
            }
            else
            {
                // If from login, just close the test window
                // MeetingLoginViewModel will handle opening MainWindow
                AppendLog("Test launched from login, closing test window...");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var testWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                        ?.Windows.FirstOrDefault(w => w is TestAudioWindow);
                    testWindow?.Close();
                });
            }

        }
        catch (OperationCanceledException)
        {
            HasError = true;
            ErrorMessage = "Test was cancelled.";
            CurrentPhase = TestPhase.Failed;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"An unexpected error occurred: {ex.Message}";
            CurrentPhase = TestPhase.Failed;
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task OpenMainWindowAsync(MeetingConfiguration config)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            // Create main window with session configuration
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            mainWindow.Show();

            // Close all other windows (optimize with single enumeration)
            var windowsToClose = desktop.Windows?
                .Where(w => w is MeetingLoginWindow or TestAudioWindow)
                .ToList();

            if (windowsToClose != null)
            {
                foreach (var window in windowsToClose)
                {
                    window.Close();
                }
            }
        });
    }

    private async Task OpenLoginWindowAsync(MeetingConfiguration config)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var testWindow = desktop.Windows?.FirstOrDefault(w => w is TestAudioWindow);
            var existingLoginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);

            if (existingLoginWindow != null)
            {
                // Login window exists, just show it again
                testWindow?.Hide();
                existingLoginWindow.Show();
                testWindow?.Close();
            }
            else
            {
                // No login window exists, create a new one
                var loginWindow = new MeetingLoginWindow();
                testWindow?.Hide();
                desktop.MainWindow = loginWindow;
                loginWindow.Show();
                testWindow?.Close();
            }
        });
    }

    /// <summary>
    /// Handle test failure - retry or show driver installation
    /// </summary>
    private async Task HandleTestFailure()
    {
        if (_testAttemptCount >= MaxTestAttempts)
        {
            // All attempts failed - show driver installation option
            AppendLog($"\n? All {MaxTestAttempts} test attempts failed.");
            AppendLog("?? Opening Driver Installation window...");
            
            await Task.Delay(1500); // Brief delay to show message
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                    return;

                var testWindow = desktop.Windows?.FirstOrDefault(w => w is TestAudioWindow);
                
                if (testWindow != null)
                {
                    // Hide test window
                    testWindow.Hide();
                    
                    // Show driver installation window with auto-reinstall flag
                    var driverWindow = new DriverInstallationWindow(autoReinstall: true);
                    driverWindow.Show();
                    
                    // Handle driver window closing
                    driverWindow.Closed += (s, e) =>
                    {
                        // Close test window when driver window closes
                        testWindow.Close();
                    };
                }
            });
        }
        else
        {
            // Still have attempts left
            int remainingAttempts = MaxTestAttempts - _testAttemptCount;
            AppendLog($"\n?? Test failed. {remainingAttempts} attempt(s) remaining.");
            AppendLog("Click 'Retry' to try again or close window to cancel.");
        }
    }

    private async Task RunDeviceDetectionAsync()
    {
        CurrentPhase = TestPhase.DeviceDetection;

        await Task.Run(() =>
        {
            var result = _testService.DetectDevices();

            Dispatcher.UIThread.Post(() =>
            {
                IsDeviceDetectionComplete = true;
                IsDeviceDetectionPassed = result.AllDevicesFound;  // ? Fixed: Use public property

                var details = $"CABLE-A Input: {(result.CableAInputFound ? "?" : "?")} {result.CableAInputName ?? "Not found"}\n" +
                             $"CABLE-A Output: {(result.CableAOutputFound ? "?" : "?")} {result.CableAOutputName ?? "Not found"}\n" +
                             $"CABLE-B Input: {(result.CableBInputFound ? "?" : "?")} {result.CableBInputName ?? "Not found"}\n" +
                             $"CABLE-B Output: {(result.CableBOutputFound ? "?" : "?")} {result.CableBOutputName ?? "Not found"}\n" +
                             $"Physical Mic: {(result.PhysicalMicFound ? "?" : "?")} {result.PhysicalMicName ?? "Not found"}\n" +
                             $"Physical Speaker: {(result.PhysicalSpeakerFound ? "?" : "?")} {result.PhysicalSpeakerName ?? "Not found"}";

                DeviceDetectionDetails = details;
            });
        });

        await Task.Delay(500);
    }

    private async Task RunCableATestAsync(CancellationToken ct)
    {
        CurrentPhase = TestPhase.CableATest;
        var result = await _testService.TestCableALoopbackAsync(ct);   
        IsCableATestComplete = true;

        var details = $"Status: {(result.IsSuccess ? "? PASSED" : "? FAILED")}\n" +
                     $"Message: {result.Message}\n" +
                     $"Audio Level: {result.AudioLevel:F2} dB\n" +
                     $"Duration: {result.TestDuration.TotalSeconds:F1}s";

        if (!string.IsNullOrEmpty(result.ErrorDetails))
        {
            details += $"\nDetails: {result.ErrorDetails}";
        }

        CableATestDetails = details;
        await Task.Delay(500);
        IsCableATestPassed = result.IsSuccess;
    }

    private async Task RunCableBTestAsync(CancellationToken ct)
    {
        CurrentPhase = TestPhase.CableBTest;
        var result = await _testService.TestCableBLoopbackAsync(ct);

        IsCableBTestComplete = true;     
        var details = $"Status: {(result.IsSuccess ? "? PASSED" : "? FAILED")}\n" +
                     $"Message: {result.Message}\n" +
                     $"Audio Level: {result.AudioLevel:F2} dB\n" +
                     $"Duration: {result.TestDuration.TotalSeconds:F1}s";

        if (!string.IsNullOrEmpty(result.ErrorDetails))
        {
            details += $"\nDetails: {result.ErrorDetails}";
        }

        CableBTestDetails = details;
        await Task.Delay(500);
        IsCableBTestPassed = result.IsSuccess;  // ? Fixed: Use public property
    }

    private void ResetTestState()
    {
        CurrentPhase = TestPhase.NotStarted;
        ProgressValue = 0;
        AllTestsPassed = false;
        CanContinue = false;
        // Don't clear TestLog - keep history of attempts
        // TestLog = string.Empty;
        HasError = false;
        ErrorMessage = string.Empty;

        // Reset device detection
        IsDeviceDetectionRunning = false;
        IsDeviceDetectionPassed = false;
        IsDeviceDetectionFailed = false;
        IsDeviceDetectionComplete = false;
        DeviceDetectionDetails = string.Empty;

        // Reset Cable-A
        IsCableATestRunning = false;
        IsCableATestPassed = false;
        IsCableATestFailed = false;
        IsCableATestComplete = false;
        CableATestDetails = string.Empty;

        // Reset Cable-B
        IsCableBTestRunning = false;
        IsCableBTestPassed = false;
        IsCableBTestFailed = false;
        IsCableBTestComplete = false;
        CableBTestDetails = string.Empty;
        
        // Note: _testAttemptCount is NOT reset here
        // It's only reset on successful test completion or window reload
    }

    #endregion

    #region Event Handlers

    private void OnLogMessage(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AppendLog(message);
        });
    }

    private void OnAudioLevelChanged(object? sender, double level)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Audio level tracking removed for simplified UI
            // Just log it for debugging
            AppendLog($"Audio level: {level:F1} dB");
        });
    }

    private void AppendLog(string message)
    {
        TestLog += message + "\n";
        
        // Auto-scroll by triggering property change
        this.RaisePropertyChanged(nameof(TestLog));
    }

    #endregion
}
