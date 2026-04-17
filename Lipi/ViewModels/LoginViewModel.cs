using ReactiveUI;
using System.Windows.Input;
using Lipi.Models;
using Lipi.Services;

namespace Lipi.ViewModels;

public class LoginViewModel : ReactiveObject
{
    private readonly MeetingAuthenticationService _authService = new();
    private readonly EncryptionService _encryption = new();

    private string _meetingId = string.Empty;
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _status = string.Empty;
    private bool _isBusy;
    private bool _isAutoLoginProcessing;

    public event Action<LipiSessionContext>? LoginSucceeded;

    public string MeetingId
    {
        get => _meetingId;
        set => this.RaiseAndSetIfChanged(ref _meetingId, value);
    }

    public string UserName
    {
        get => _userName;
        set => this.RaiseAndSetIfChanged(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool IsAutoLoginProcessing
    {
        get => _isAutoLoginProcessing;
        set => this.RaiseAndSetIfChanged(ref _isAutoLoginProcessing, value);
    }

    public ICommand LoginCommand { get; }

    public LoginViewModel(string? initialMeetingId = null)
    {
        if (!string.IsNullOrWhiteSpace(initialMeetingId))
            _meetingId = initialMeetingId.Trim();

        LoginCommand = ReactiveCommand.CreateFromTask(LoginAsync);
    }

    private async Task LoginAsync()
    {
        if (IsBusy)
            return;

        if (string.IsNullOrWhiteSpace(MeetingId) || string.IsNullOrWhiteSpace(UserName))
        {
            Status = "Meeting ID and user name are required.";
            return;
        }

        IsBusy = true;
        Status = "Validating meeting...";

        try
        {
            var response = await _authService.ValidateMeetingAsync(MeetingId, UserName, Password);
            if (!response.IsValid)
            {
                Status = response.Message ?? "Login failed.";
                return;
            }

            var deviceId = MeetingAuthenticationService.GetDeviceId();
            var deviceName = MeetingAuthenticationService.GetDeviceName();
            var config = _encryption.DecryptConfig(response.EncryptedConfig, deviceId);

            var hubUrl = ResolveLipiHubUrl(
                config.BackendTranslationHubUrl,
                response.BackendTranslationHubUrl,
                _authService.ApiBaseUrl);

            var languages = config.AvailableLanguages ?? [];
            if (languages.Count == 0)
            {
                Status = "No languages were returned from API for this meeting.";
                return;
            }

            LoginSucceeded?.Invoke(new LipiSessionContext
            {
                MeetingId = config.MeetingId,
                SessionToken = response.SessionToken,
                DeviceId = deviceId,
                DeviceName = deviceName,
                HubUrl = hubUrl,
                AvailableLanguages = languages,
                SourceLanguage = config.TranslationConfig?.VendorLanguage?.Code ?? languages[0].Code
            });
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsAutoLoginProcessing = false;
        }
    }

    private static string ResolveLipiHubUrl(string configHub, string responseHub, string apiBaseUrl)
    {
        var candidate = !string.IsNullOrWhiteSpace(configHub) ? configHub : responseHub;
        if (string.IsNullOrWhiteSpace(candidate))
            return $"{apiBaseUrl.TrimEnd('/')}/hubs/lipi";

        if (candidate.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return candidate.Replace("/hubs/translation", "/hubs/lipi", StringComparison.OrdinalIgnoreCase);

        var relative = candidate.Replace("/hubs/translation", "/hubs/lipi", StringComparison.OrdinalIgnoreCase);
        return $"{apiBaseUrl.TrimEnd('/')}/{relative.TrimStart('/')}";
    }
}