using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Reactive;
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

    private string _meetingId = string.Empty;
    private string _userName = string.Empty;
    private string _meetingPassword = string.Empty;
    private bool _isValidating;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private string _appVersion = string.Empty;

    private bool _isMeetingIdEditable = true;

    public MeetingLoginViewModel()
    {
        // Initialize readonly services directly in constructor
        _authService = new MeetingAuthenticationService();
        _sessionManager = new SessionManager();
        _driverService = new DriverInstallationService();
        _audioVerificationService = new AudioSourceVerificationService();
        _deviceService = DeviceService.Instance;

        // Commands
        ValidateCommand = ReactiveCommand.CreateFromTask(ValidateMeetingAsync);
        PasteCommand = ReactiveCommand.CreateFromTask(PasteMeetingIdAsync);
        RetryCommand = ReactiveCommand.Create(ResetState);

        AppVersion = $"Version {AppVersionHelper.GetAppVersion()}";

        // If Program.MeetingId was provided (from args or ClickOnce), pre-fill and lock the field
        if (!string.IsNullOrWhiteSpace(Program.MeetingId))
        {
            //MeetingId = Program.MeetingId;
            //IsMeetingIdEditable = false;
        }
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

    public bool IsMeetingIdEditable
    {
        get => _isMeetingIdEditable;
        set => this.RaiseAndSetIfChanged(ref _isMeetingIdEditable, value);
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
            ShowError("Please enter your name");
            return;
        }

        HasError = false;
        IsValidating = true;
        StatusMessage = "Verifying audio source...";

        // 60-second overall flow timeout — surfaces as a clean message, not a raw exception
        _loginCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = _loginCts.Token;

        try
        {
            _ = Task.Run(() => _deviceService.GetAllDevices());

            var audioResult = await _audioVerificationService.VerifyAudioSourceAsync();
            if (!audioResult.IsValid)
            {
                ShowError(audioResult.ErrorMessage ?? "Audio source verification failed");
                return;
            }

            StatusMessage = "Validating meeting ID...";

            var deviceId = MeetingAuthenticationService.GetDeviceId();
            var deviceName = MeetingAuthenticationService.GetDeviceName();

            var response = await _authService.ValidateMeetingAsync(
                MeetingId.Trim(),
                deviceId,
                deviceName,
                UserName,
                MeetingPassword);

            if (!response.IsValid)
            {
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
            await Task.Delay(100);

            MeetingConfiguration? config = null;
            try
            {
                var es = new EncryptionService();
                config = es.DecryptConfig(response.EncryptedConfig, deviceId);
                config.SessionToken = response.SessionToken;
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

            _sessionManager.StartSession(config, response.SessionToken, response.ValidUntil);

            StatusMessage = "Success! Verifying audio setup...";
            await Task.Delay(500, ct);

            var audioTestPassed = await LaunchAudioTestAsync();
            if (!audioTestPassed)
            {
                ShowError("Audio test was not completed. Please retry or check your audio setup.");
                _sessionManager.ClearSession();
                return;
            }

            StatusMessage = "Audio verified! Launching Vaani...";
            await OpenMainWindowAsync(config);
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

    private async Task PasteMeetingIdAsync()
    {
        try
        {
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
                            MeetingId = text.Trim();
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private void ResetState()
    {
        // Cancel any in-flight login attempt first
        try { _loginCts?.Cancel(); } catch { }
        HasError = false;
        IsValidating = false;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
        IsValidating = false;
    }

    private async Task<bool> LaunchAudioTestAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var testWindow = new TestAudioWindow(isFromLogin: true);
                    var loginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);

                    loginWindow?.Hide();

                    testWindow.Show();
                    testWindow.Closed += (s, e) =>
                    {
                        var passed = testWindow.TestPassed;
                        if (!passed && loginWindow != null && !loginWindow.IsVisible)
                            loginWindow.Show();

                        tcs.TrySetResult(passed);
                    };
                }
                else
                {
                    tcs.TrySetResult(false);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Warning: Could not launch audio test - {ex.Message}";
                tcs.TrySetResult(true);
            }
        });

        return await tcs.Task;
    }

    private async Task OpenMainWindowAsync(MeetingConfiguration config)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;
                mainWindow.Show();

                var loginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);
                loginWindow?.Close();

                var testWindow = desktop.Windows?.FirstOrDefault(w => w is TestAudioWindow);
                testWindow?.Close();
            }
        });
    }

    public async Task OnWindowLoadedAsync()
    {
        _ = Task.Run(() => _deviceService.GetAllDevices());

        if (!_driverService.IsVBCableInstalled())
        {
            await ShowDriverInstallationWindowAsync();
        }
    }

    private async Task ShowDriverInstallationWindowAsync()
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var driverWindow = new Vaani.DriverInstallation.Views.DriverInstallationWindow();

                var loginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);
                loginWindow?.Hide();

                driverWindow.Show();
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

    private void ShowError(string title, string message)
    {
        HasError = true;
        StatusMessage = title;
        ErrorMessage = message;
        IsValidating = false;
    }

    private void Cancel()
    {
        if (IsValidating) return;
    }

    private void Close()
    {
        // closing logic
    }

    #endregion
}