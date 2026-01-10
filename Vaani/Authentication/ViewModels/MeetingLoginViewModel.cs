using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using System;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Vaani.Authentication.Models;
using Vaani.Authentication.Services;
using Vaani.Authentication.Views;
using Vaani.DriverInstallation.Services;
using Vaani.Services;
using Vaani.Views;


namespace Vaani.Authentication.ViewModels;

public class MeetingLoginViewModel : ReactiveObject
{
    private readonly MeetingAuthenticationService _authService;
    private readonly SessionManager _sessionManager;
    private readonly DriverInstallationService _driverService;
    private readonly AudioSourceVerificationService _audioVerificationService;

    private string _meetingId = "";
    private bool _isValidating;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    public string? _appVersion;

    public MeetingLoginViewModel()
    {
        _authService = new MeetingAuthenticationService();
        _sessionManager = new SessionManager();
        _driverService = new DriverInstallationService();
        _audioVerificationService = new AudioSourceVerificationService();

        // Initialize commands
        ValidateCommand = ReactiveCommand.CreateFromTask(ValidateMeetingAsync);
        PasteCommand = ReactiveCommand.CreateFromTask(PasteMeetingIdAsync);
        RetryCommand = ReactiveCommand.Create(ResetState);

        AppVersion = $"Version {AppVersionHelper.GetAppVersion()}";
    }
   
   

    #region Properties

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

        // Reset state
        HasError = false;
        IsValidating = true;
        StatusMessage = "Verifying audio source...";

        try
        {
            // ✅ STEP 1: Verify audio source FIRST
            var audioSourceResult = await _audioVerificationService.VerifyAudioSourceAsync();

            if (!audioSourceResult.IsValid)
            {
                ShowError(audioSourceResult.ErrorMessage ?? "Audio source verification failed");
                return;
            }          

            // ✅ STEP 2: Proceed with meeting authentication
            StatusMessage = "Validating meeting ID...";

            var deviceId = MeetingAuthenticationService.GetDeviceId();
            var deviceName = MeetingAuthenticationService.GetDeviceName();

            // Call API to validate
            var response = await _authService.ValidateMeetingAsync(
                MeetingId.Trim(),
                deviceId,
                deviceName
            );

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
                    "NETWORK_ERROR" => "Network error. Please check your internet connection.",
                    "TIMEOUT" => "Request timed out. Please try again.",
                    _ => response.Message ?? "Unable to validate meeting. Please try again."
                };

                ShowError(errorMsg);
                return;
            }

            StatusMessage = "Decrypting configuration...";
            await Task.Delay(100); // Small delay for UX

            // TODO: In production, encryption key should come from API response
            // For now, using a placeholder approach
            // The backend API will need to provide the key or a way to derive it

            // For demonstration, we'll assume the encrypted config can be decrypted
            // In real implementation, you'll need to handle the encryption key properly
            MeetingConfiguration? config = null;

            try
            {
                // Placeholder: In production, get the key from the API response or derive it
                EncryptionService es = new EncryptionService();
                config = es.DecryptConfig(response.EncryptedConfig, deviceId);
                config.SessionToken = response.SessionToken;             

            }
            catch (Exception ex)
            {
                ShowError($"Failed to decrypt configuration: {ex.Message}");
                return;
            }

            if (config == null)
            {
                ShowError("Invalid configuration received from server");
                return;
            }

            StatusMessage = "Starting session...";
            await Task.Delay(300);

            // Start session
            _sessionManager.StartSession(
                config,
                response.SessionToken,
                response.ValidUntil
            );

            StatusMessage = "Success! Launching Vaani...";
            await Task.Delay(500);

            // Open main window and close login
            await OpenMainWindowAsync(config);
        }
        catch (Exception ex)
        {
            ShowError($"Unexpected error: {ex.Message}");
        }
        finally
        {
            IsValidating = false;
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
    /// Create mock configuration for testing (remove in production)
    /// In production, decrypt the response.EncryptedConfig properly
    /// </summary>
    /// 
    private MeetingConfiguration CreateMockConfiguration(MeetingValidationResponse response)
    {
        // Mock configuration with your actual Azure credentials
        // TODO: Replace with actual decryption when API is ready
        return new MeetingConfiguration
        {
            MeetingId = MeetingId,
            MeetingName = response.MeetingName,
            AzureConfig = new AzureConfiguration
            {
                // Using your existing Azure credentials from ClientService
                SubscriptionKey = "BnsKkEvkgEN4Muh48WOKOWQtT96WpJCNVcjPqBFClKpIyEAu1JBtJQQJ99BKACHYHv6XJ3w3AAAAACOGiTqU",
                Region = "eastus2"
            },
            TranslationConfig = new TranslationConfiguration
            {
                VendorLanguage = new LanguageConfiguration
                {
                    Code = "hi-IN",
                    Voice = "hi-IN-MadhurNeural"
                },
                OrganizerLanguage = new LanguageConfiguration
                {
                    Code = "en-US",
                    Voice = "en-US-GuyNeural"
                }
            },
            TimeWindow = new TimeWindow
            {
                ValidFrom = DateTime.UtcNow,
                ValidUntil = response.ValidUntil
            },
            Features = response.Features,
            SessionToken = response.SessionToken,
            Metadata = new ConfigurationMetadata
            {
                ApiVersion = "v1",
                EncryptedAt = DateTime.UtcNow,
                ConfigVersion = 1
            }
        };
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

                // TODO: Pass configuration to MainWindow/MainViewModel
                // You'll need to modify MainViewModel to accept session configuration

                desktop.MainWindow = mainWindow;
                mainWindow.Show();

                // Close login window
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    var loginWindow = lifetime.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);
                    loginWindow?.Close();
                }
            }
        });
    }

    /// <summary>
    /// Check driver on window load
    /// </summary>
    public async Task OnWindowLoadedAsync()
    {
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
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var driverWindow = new Vaani.DriverInstallation.Views.DriverInstallationWindow();

            // Show as dialog
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var loginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow);
                if (loginWindow != null)
                {
                    await driverWindow.ShowDialog(loginWindow);
                }
                else
                {
                    driverWindow.Show();
                }
            }
        });
    }

    #endregion
}