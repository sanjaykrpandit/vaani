using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vaani.Authentication.Models;
using Vaani.Authentication.Services;
using Vaani.Authentication.Views;
using Vaani.Common;
using Vaani.DriverInstallation.Services;
using Vaani.Services;
using Vaani.TestAudio.Views;
using Vaani.ViewModels;
using Vaani.Views;
using static Vaani.Common.ErrorMessages;


namespace Vaani.Authentication.ViewModels;

public class MeetingLoginViewModel : ReactiveObject, IDisposable
{
    private readonly MeetingAuthenticationService _authService;
    private readonly SessionManager _sessionManager;
    private readonly DriverInstallationService _driverService;
    private readonly AudioSourceVerificationService _audioVerificationService;
    private readonly DeviceService _deviceService; // Singleton instance for cache pre-warming

    private string _meetingId = "";
    private string _userName = "";
    private string _meetingPassword = "";
    private bool _isValidating;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    public string _appVersion = "";
    // Tracks and can cancel the in-flight login attempt (slow-network guard)
    private CancellationTokenSource? _loginCts;
    private bool _disposed;

    public MeetingLoginViewModel()
    {
        _authService = new MeetingAuthenticationService();
        _sessionManager = new SessionManager();
        _driverService = new DriverInstallationService();
        _audioVerificationService = new AudioSourceVerificationService();
        _deviceService = DeviceService.Instance; // Use singleton for cache sharing

        // Initialize commands
        ValidateCommand = ReactiveCommand.CreateFromTask(ValidateMeetingAsync);
        PasteCommand = ReactiveCommand.CreateFromTask(PasteMeetingIdAsync);
        RetryCommand = ReactiveCommand.Create(ResetState);

        AppVersion = $"Version {AppVersionHelper.GetAppVersion()}";

        ApplyMeetingIdFromProgram();
        Program.MeetingIdUpdated += OnProgramMeetingIdUpdated;

    }
   
   

    #region Properties

    public string UserName
    {
        get => _userName;
        set => this.RaiseAndSetIfChanged(ref _userName, value);
    }

    public string AppVersion
    {
        get => _appVersion;
        set => this.RaiseAndSetIfChanged(ref _appVersion, value);
    }

    public string MeetingId
    {
        get => _meetingId;
        set => this.RaiseAndSetIfChanged(ref _meetingId, value);
    }

    public string MeetingPassword
    {
        get => _meetingPassword;
        set => this.RaiseAndSetIfChanged(ref _meetingPassword, value);
    }

    public bool IsValidating
    {
        get => _isValidating;
        set => this.RaiseAndSetIfChanged(ref _isValidating, value);
    }

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

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    #endregion

    #region Commands

    public ICommand ValidateCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand RetryCommand { get; }

    #endregion

    #region Methods

    private async Task ValidateMeetingAsync()
    {
        if (string.IsNullOrWhiteSpace(MeetingId))
        {
            ShowError("Please enter a Meeting ID");
            return;
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            ShowError("Please enter a your name");
            return;
        }

        // Prevent concurrent submissions on slow network (double-click guard)
        if (_isValidating) return;

        // Reset state
        HasError = false;
        IsValidating = true;
        StatusMessage = "Verifying audio source...";

        // 60-second overall flow timeout — surfaces as a clean message, not a raw exception
        _loginCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = _loginCts.Token;

        try
        {
            // 🚀 OPTIMIZATION: Pre-warm device cache in background EARLY
            // This runs in parallel with audio verification and API calls
            // By the time we reach TestAudio, cache will be hot!
            _ = Task.Run(() => _deviceService.GetAllDevices());

            // ✅ STEP 1: Verify audio source FIRST
            var audioSourceResult = await _audioVerificationService
                .VerifyAudioSourceAsync()
                .WaitAsync(ct);

            if (!audioSourceResult.IsValid)
            {
                ShowError(audioSourceResult.ErrorMessage ?? "Audio source verification failed");
                return;
            }          

            // ✅ STEP 2: Proceed with meeting authentication
            StatusMessage = "Validating meeting ID...";

            var deviceId = MeetingAuthenticationService.GetDeviceId();
            var deviceName = MeetingAuthenticationService.GetDeviceName();

            // Call API to validate (device cache is warming in background)
            var response = await _authService.ValidateMeetingAsync(
                MeetingId.Trim(),
                deviceId,
                deviceName,
                UserName,
                MeetingPassword
            ).WaitAsync(ct);

            if (!response.IsValid)
            {
                // Show appropriate error message
                var errorMsg = response.ErrorCode switch
                {
                    "MEETING_NOT_FOUND" => "Meeting ID not found. Please check and try again.",
                    "MEETING_NOT_STARTED" => $"Meeting hasn't started yet. {response.Message}",
                    "MEETING_EXPIRED" => "This meeting has ended. Please contact the organizer.",
                    "MAX_PARTICIPANTS_REACHED" => "Maximum participants already connected to this meeting.",
                    "MEETING_REVOKED" => "This meeting has been cancelled by the organizer.",
                    "PASSWORD_REQUIRED" => "This meeting requires a password. Please enter the password.",
                    "PASSWORD_INCORRECT" => "Incorrect meeting password. Please try again.",
                    // Network / slow-connection errors
                    "TIMEOUT" => "The server took too long to respond. Please check your internet connection and try again.",
                    "NETWORK_ERROR" => "Cannot reach the server. Please check your internet connection and try again.",
                    "HTTP_408" or "HTTP_503" or "HTTP_504" => "The server is temporarily unavailable. Please try again in a moment.",
                    "UNKNOWN_ERROR" => "Something went wrong while contacting the server. Please try again.",
                    _ => response.Message ?? "Unable to validate meeting. Please try again."
                };

                ShowError(errorMsg);
                return;
            }

            StatusMessage = "Decrypting configuration...";
            await Task.Delay(100); // Small delay for UX

            MeetingConfiguration? config = null;
            try
            {
                // Placeholder: In production, get the key from the API response or derive it
                EncryptionService es = new EncryptionService();
                config = es.DecryptConfig(response.EncryptedConfig, deviceId);
                config.SessionToken = response.SessionToken;
                // ✅ Store backend hub URL so TranslationSettings can use it without Azure credentials
                if (!string.IsNullOrWhiteSpace(response.BackendTranslationHubUrl))
                    config.BackendTranslationHubUrl = response.BackendTranslationHubUrl;

            }
            catch (Exception ex)
            {
                ShowError($"Failed to decrypt configuration: {Classify(ex)}");
                return;
            }

            if (config == null)
            {
                ShowError("Invalid configuration received from server");
                return;
            }

            StatusMessage = "Starting session...";
            await Task.Delay(300, ct);

            // Start session — guard against unexpected threading errors
            try
            {
                _sessionManager.StartSession(
                    config,
                    response.SessionToken,
                    response.ValidUntil
                );
            }
            catch (Exception sessionEx)
            {
                ShowError($"Failed to initialise session: {Classify(sessionEx)}");
                return;
            }

            StatusMessage = "Success! Verifying audio setup...";
            await Task.Delay(500, ct);

            // ✅ STEP 3: Launch Audio Test BEFORE opening main window
            // Device cache is now pre-warmed from background task!
            var audioTestPassed = await LaunchAudioTestAsync();

            if (!audioTestPassed)
            {
                // User cancelled or test failed
                ShowError("Audio test was not completed. Please retry or check your audio setup.");

                // Clear session since we won't proceed
                _sessionManager.ClearSession();
                return;
            }

            // ✅ STEP 4: Open main window only after audio test passes
            StatusMessage = "Audio verified! Launching Vaani...";

            try
            {
                await OpenMainWindowAsync(config);
            }
            catch (Exception windowEx)
            {
                ShowError($"Failed to open the application: {Classify(windowEx)}");
            }
        }
        catch (OperationCanceledException) when (_loginCts?.IsCancellationRequested == true)
        {
            // Overall 60-second timeout expired — give a clean network message
            ShowError("The request timed out due to a slow network. Please check your internet connection and try again.");
        }
        catch (Exception ex)
        {
            // All other unexpected errors — never surface raw system messages
            ShowError(Classify(ex));
        }
        finally
        {
            IsValidating = false;
            _loginCts?.Dispose();
            _loginCts = null;
        }
    }

    /// <summary>
    /// Paste meeting ID from clipboard
    /// </summary>
    private async Task PasteMeetingIdAsync()
    {
        try
        {
            // Get clipboard from TopLevel
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                if (mainWindow != null)
                {
                    var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
                    if (clipboard != null)
                    {
                        var text = await clipboard.GetTextAsync();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            MeetingId = text.Trim();
                        }
                    }
                }
            }
        }
        catch
        {
            // Clipboard access failed, ignore
        }
    }

    /// <summary>
    /// Reset to initial state
    /// </summary>
    private void ResetState()
    {
        // Cancel any in-flight login attempt first
        try { _loginCts?.Cancel(); } catch { }
        HasError = false;
        IsValidating = false;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// Show error message
    /// </summary>
    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
        IsValidating = false;
    }

    /// <summary>
    /// Launches the audio test window after successful login.
    /// Returns true if test passed, false if user cancelled or test failed.
    /// </summary>
    private async Task<bool> LaunchAudioTestAsync()
    {
        bool testPassed = false;
        var tcs = new TaskCompletionSource<bool>();

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var testWindow = new TestAudioWindow(isFromLogin: true);
                    var loginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);
                    
                    if (loginWindow != null)
                    {
                        // Hide login window
                        loginWindow.Hide();
                    }
                    
                    // Show test window as main window
                    testWindow.Show();
                    
                    // Handle test window closing
                    testWindow.Closed += (s, e) =>
                    {
                        testPassed = testWindow.TestPassed;
                        
                        // Show login window again if test didn't pass
                        if (!testPassed && loginWindow != null && !loginWindow.IsVisible)
                        {
                            loginWindow.Show();
                        }
                        
                        tcs.TrySetResult(testPassed);
                    };
                }
                else
                {
                    tcs.TrySetResult(false);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                StatusMessage = $"Warning: Could not launch audio test - {ex.Message}";
                
                // In case of error, allow user to proceed (fail-safe)
                tcs.TrySetResult(true);
            }
        });

        return await tcs.Task;
    }

   
    /// <summary>
    /// Open main window with configuration
    /// </summary>
    private async Task OpenMainWindowAsync(MeetingConfiguration config)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Create main window with session configuration
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;
                mainWindow.Show();

                // Close all other windows (login and test)
                var loginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);
                loginWindow?.Close();
                
                var testWindow = desktop.Windows?.FirstOrDefault(w => w is TestAudioWindow);
                testWindow?.Close();
            }
        });
    }

    /// <summary>
    /// Check driver on window load and pre-warm device cache
    /// </summary>
    public async Task OnWindowLoadedAsync()
    {
        ApplyMeetingIdFromProgram();

        // 🚀 OPTIMIZATION: Pre-warm device cache immediately on window load
        // This runs in background while user types Meeting ID
        _ = Task.Run(() => _deviceService.GetAllDevices());

        // Check if driver is installed, if not show installation window
        if (!_driverService.IsVBCableInstalled())
        {
            await ShowDriverInstallationWindowAsync();
        }
    }

    /// <summary>
    /// Shows the driver installation window if driver is not installed.
    /// </summary>
    private async Task ShowDriverInstallationWindowAsync()
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var driverWindow = new Vaani.DriverInstallation.Views.DriverInstallationWindow();
                
                // Get the login window
                var loginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);
                
                // Hide login window
                if (loginWindow != null)
                {
                    loginWindow.Hide();
                }
                
                // Show driver window as main window
                driverWindow.Show();
                
                // When driver window closes, show login window again
                driverWindow.Closed += (s, e) =>
                {
                    // If driver is still not installed, treat close as app exit intent
                    if (!_driverService.IsVBCableInstalled())
                    {
                        desktop.Shutdown();
                        return;
                    }

                    if (loginWindow != null && !loginWindow.IsVisible)
                    {
                        try
                        {
                            loginWindow.Show();
                            desktop.MainWindow = loginWindow;
                        }
                        catch (InvalidOperationException)
                        {
                            // Hidden login window may have already been closed; recreate it
                            var newLoginWindow = new MeetingLoginWindow();
                            desktop.MainWindow = newLoginWindow;
                            newLoginWindow.Show();
                        }
                    }
                };
            }
        });
    }

    private void OnProgramMeetingIdUpdated(string meetingId)
    {
        if (_disposed)
            return;

        if (string.IsNullOrWhiteSpace(meetingId))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            MeetingId = meetingId.Trim();
        });
    }

    private void ApplyMeetingIdFromProgram()
    {
        if (!string.IsNullOrWhiteSpace(Program.MeetingId))
        {
            MeetingId = Program.MeetingId!;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Program.MeetingIdUpdated -= OnProgramMeetingIdUpdated;

        try { _loginCts?.Cancel(); } catch { }
        _loginCts?.Dispose();
        _loginCts = null;
    }

    #endregion
}