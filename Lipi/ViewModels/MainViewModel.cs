using Avalonia.Threading;
using Lipi.Models;
using Lipi.Services;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive;
using System.Windows.Input;

namespace Lipi.ViewModels;

public class MainViewModel : ReactiveObject, IDisposable
{
    private const int MaxBubbles = 100;
    private const int DefaultWaveInDeviceNumber = -1;
    private readonly MeetingAuthenticationService _authService = new();
    private readonly LipiSessionContext _session;
    private readonly AudioInputDeviceService _audioInputDeviceService = new();
    private readonly AudioOutputDeviceService _audioOutputDeviceService = new();
    private readonly SemaphoreSlim _inputDeviceRefreshLock = new(1, 1);
    private ILipiRealtimeClient? _realtimeClient;
    private HashSet<string> _knownInputDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _knownOutputDeviceIds = new(StringComparer.OrdinalIgnoreCase);

    private AudioInputDevice? _selectedInputDevice;
    private AudioOutputDevice? _selectedOutputDevice;
    private AudioSourceOption? _selectedAudioSourceMode;
    private SelectableLanguage? _selectedSourceLanguage;
    private ConnectionModeOption? _selectedConnectionMode;
    private bool _isRunning;
    private bool _isSettingsVisible = true;
    private bool _isSubtitleMode;
    private bool _isSubtitleChromeVisible = true;
    private bool _showTranscriptText;
    private double _subtitleFontSize = 16;
    private double _subtitleBackgroundOpacity = 0.5;
    private bool _useBlackSubtitleText;
    private string _subtitleTranslatedText = string.Empty;
    private string _subtitleTranscriptText = string.Empty;
    private string _status = "Ready";
    private bool _enforcingSelection;
    private bool _isHandlingRealtimeError;
    private bool _isRefreshingInputDevices;
    private bool _isRefreshingOutputDevices;
    private bool _autoSelectInputDevice = true;
    private bool _autoSelectOutputDevice = true;
    private TranscriptBubble? _currentRecognizingBubble;
    private readonly List<LanguageInfo> _activeTargetLanguages = [];
    private readonly DispatcherTimer _subtitleInactivityTimer;
    private string? _activeSessionId;
    private string? _lastReportedErrorFingerprint;
    private DateTime _lastReportedErrorAtUtc = DateTime.MinValue;

    public ObservableCollection<AudioInputDevice> InputDevices { get; } = [];
    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = [];
    public ObservableCollection<AudioSourceOption> AudioSourceModes { get; } = [];
    public ObservableCollection<ConnectionModeOption> ConnectionModes { get; } = [];
    public ObservableCollection<SelectableLanguage> SourceLanguages { get; } = [];
    public ObservableCollection<SelectableLanguage> TargetLanguages { get; } = [];
    public ObservableCollection<TranscriptBubble> Bubbles { get; } = [];
    public ObservableCollection<TranslationLine> SubtitleTranslations { get; } = [];
    public ObservableCollection<SubtitleLanguageLine> SubtitleLanguageLines { get; } = [];

    public AudioInputDevice? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set
        {
            if (_selectedInputDevice == value)
                return;

            this.RaiseAndSetIfChanged(ref _selectedInputDevice, value);

            if (!_isRefreshingInputDevices && value != null)
                _autoSelectInputDevice = false;

            this.RaisePropertyChanged(nameof(SelectedAudioDevice));

            if (!_isRefreshingInputDevices && IsRunning && SelectedAudioSourceMode?.Mode == AudioSourceMode.Microphone)
                _ = TrySwitchActiveAudioDeviceAsync();
        }
    }

    public AudioOutputDevice? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set
        {
            if (_selectedOutputDevice == value)
                return;

            this.RaiseAndSetIfChanged(ref _selectedOutputDevice, value);

            if (!_isRefreshingOutputDevices && value != null)
                _autoSelectOutputDevice = false;

            this.RaisePropertyChanged(nameof(SelectedAudioDevice));

            if (!_isRefreshingOutputDevices && IsRunning && SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker)
                _ = TrySwitchActiveAudioDeviceAsync();
        }
    }

    public AudioSourceOption? SelectedAudioSourceMode
    {
        get => _selectedAudioSourceMode;
        set
        {
            if (_selectedAudioSourceMode == value)
                return;

            this.RaiseAndSetIfChanged(ref _selectedAudioSourceMode, value);
            this.RaisePropertyChanged(nameof(AvailableAudioDevices));
            this.RaisePropertyChanged(nameof(SelectedAudioDevice));
            this.RaisePropertyChanged(nameof(AudioDeviceLabel));

            EnsureSelectedAudioDeviceForCurrentSource();

            if (IsRunning)
                _ = TrySwitchActiveAudioDeviceAsync();
        }
    }

    public IEnumerable<IAudioDeviceOption> AvailableAudioDevices => SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker
        ? OutputDevices.Cast<IAudioDeviceOption>()
        : InputDevices.Cast<IAudioDeviceOption>();

    public IAudioDeviceOption? SelectedAudioDevice
    {
        get => SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker
            ? SelectedOutputDevice
            : SelectedInputDevice;
        set
        {
            if (SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker)
                SelectedOutputDevice = value as AudioOutputDevice;
            else
                SelectedInputDevice = value as AudioInputDevice;

            this.RaisePropertyChanged(nameof(SelectedAudioDevice));
        }
    }

    public SelectableLanguage? SelectedSourceLanguage
    {
        get => _selectedSourceLanguage;
        set => this.RaiseAndSetIfChanged(ref _selectedSourceLanguage, value);
    }

    public ConnectionModeOption? SelectedConnectionMode
    {
        get => _selectedConnectionMode;
        set => this.RaiseAndSetIfChanged(ref _selectedConnectionMode, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        set
        {
            if (_isSettingsVisible == value)
                return;

            this.RaiseAndSetIfChanged(ref _isSettingsVisible, value);
            this.RaisePropertyChanged(nameof(IsSettingsPanelVisible));
        }
    }

    public bool IsSubtitleMode
    {
        get => _isSubtitleMode;
        set
        {
            if (_isSubtitleMode == value)
                return;

            this.RaiseAndSetIfChanged(ref _isSubtitleMode, value);

            if (value)
            {
                IsSettingsVisible = false;
                IsSubtitleChromeVisible = false;
            }
            else
            {
                IsSubtitleChromeVisible = true;
            }

            RaiseSubtitleStateProperties();
        }
    }

    public bool IsSubtitleChromeVisible
    {
        get => _isSubtitleChromeVisible;
        set
        {
            if (_isSubtitleChromeVisible == value)
                return;

            this.RaiseAndSetIfChanged(ref _isSubtitleChromeVisible, value);

            this.RaisePropertyChanged(nameof(IsHeaderVisible));
            this.RaisePropertyChanged(nameof(IsStatusRowVisible));
            this.RaisePropertyChanged(nameof(HeaderChromeOpacity));
        }
    }

    public bool ShowTranscriptText
    {
        get => _showTranscriptText;
        set
        {
            if (_showTranscriptText == value)
                return;

            this.RaiseAndSetIfChanged(ref _showTranscriptText, value);

            this.RaisePropertyChanged(nameof(TranscriptToggleText));
            this.RaisePropertyChanged(nameof(SubtitleTranscriptVisible));
        }
    }

    public bool UseBlackSubtitleText
    {
        get => _useBlackSubtitleText;
        set
        {
            if (_useBlackSubtitleText == value)
                return;

            this.RaiseAndSetIfChanged(ref _useBlackSubtitleText, value);
            ApplySubtitleTheme();

            this.RaisePropertyChanged(nameof(SubtitleBackgroundBrush));
            this.RaisePropertyChanged(nameof(SubtitlePrimaryForeground));
            this.RaisePropertyChanged(nameof(SubtitleSecondaryForeground));
            this.RaisePropertyChanged(nameof(SubtitleRecognizingForeground));
            this.RaisePropertyChanged(nameof(SubtitleTranscriptForeground));
            this.RaisePropertyChanged(nameof(SubtitleColorButtonForeground));
            this.RaisePropertyChanged(nameof(SubtitleColorButtonBackground));
            this.RaisePropertyChanged(nameof(SubtitleColorButtonBorderBrush));
        }
    }

    public double SubtitleFontSize
    {
        get => _subtitleFontSize;
        set
        {
            var normalizedValue = Math.Clamp(value, 18, 72);
            if (Math.Abs(_subtitleFontSize - normalizedValue) < 0.001)
                return;

            this.RaiseAndSetIfChanged(ref _subtitleFontSize, normalizedValue);
            foreach (var line in SubtitleTranslations)
                line.DisplayFontSize = normalizedValue;
            foreach (var line in SubtitleLanguageLines)
                line.DisplayFontSize = normalizedValue;

            this.RaisePropertyChanged(nameof(SubtitleTranscriptFontSize));
        }
    }

    public string SubtitleTranslatedText
    {
        get => _subtitleTranslatedText;
        private set
        {
            if (string.Equals(_subtitleTranslatedText, value, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _subtitleTranslatedText, value);

            this.RaisePropertyChanged(nameof(HasSubtitleContent));
            this.RaisePropertyChanged(nameof(ShowSubtitleTranslatedPlaceholder));
        }
    }

    public string SubtitleTranscriptText
    {
        get => _subtitleTranscriptText;
        private set
        {
            if (string.Equals(_subtitleTranscriptText, value, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _subtitleTranscriptText, value);

            this.RaisePropertyChanged(nameof(SubtitleTranscriptVisible));
        }
    }

    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public bool IsHeaderVisible => !IsSubtitleMode || IsSubtitleChromeVisible;
    public bool IsStatusRowVisible => !IsSubtitleMode || IsSubtitleChromeVisible;
    public double HeaderChromeOpacity => IsHeaderVisible ? 1d : 0d;
    public bool IsBubbleListVisible => !IsSubtitleMode;
    public bool IsSettingsPanelVisible => !IsSubtitleMode && IsSettingsVisible;
    public string HeaderPanelBackground => IsSubtitleMode ? "#7A000000" : "Transparent";
    public bool HasSubtitleContent => SubtitleLanguageLines.Count > 0 || !string.IsNullOrWhiteSpace(SubtitleTranscriptText);
    public bool HasSubtitleTranslations => SubtitleTranslations.Count > 0;
    public bool HasSubtitleLanguageLines => SubtitleLanguageLines.Count > 0;
    public bool ShowSubtitleTranslatedPlaceholder => !HasSubtitleLanguageLines && !string.IsNullOrWhiteSpace(SubtitleTranslatedText);
    public bool SubtitleTranscriptVisible => ShowTranscriptText && !string.IsNullOrWhiteSpace(SubtitleTranscriptText);
    public double SubtitleTranscriptFontSize => Math.Max(14, Math.Round(SubtitleFontSize * 0.55, MidpointRounding.AwayFromZero));
    public string SubtitleBackgroundBrush => UseBlackSubtitleText
        ? "#00161616"
        : $"#{(int)Math.Round(SubtitleBackgroundOpacity * 255, MidpointRounding.AwayFromZero):X2}161616";
    public string SubtitlePrimaryForeground => UseBlackSubtitleText ? "#FF4D4D4D" : "White";
    public string SubtitleSecondaryForeground => UseBlackSubtitleText ? "#F04D4D4D" : "#F0FFFFFF";
    public string SubtitleRecognizingForeground => UseBlackSubtitleText ? "#CC4D4D4D" : "#CCFFFFFF";
    public string SubtitleTranscriptForeground => UseBlackSubtitleText ? "#E64D4D4D" : "#E6FFFFFF";
    public string SubtitleColorButtonForeground => UseBlackSubtitleText ? "#FF4D4D4D" : "White";
    public string SubtitleColorButtonBackground => UseBlackSubtitleText ? "#FFF5F5F5" : "#CC111111";
    public string SubtitleColorButtonBorderBrush => UseBlackSubtitleText ? "#66000000" : "#66FFFFFF";
    public string SubtitleModeButtonText => IsSubtitleMode ? "Exit Sub" : "Sub Title";
    public string TranscriptToggleText => ShowTranscriptText ? "Hide Txt" : "Transcript";
    public string StartStopBackground => IsRunning ? "#66C62828" : "#6643A047";

    public double SubtitleBackgroundOpacity
    {
        get => _subtitleBackgroundOpacity;
        set
        {
            var normalizedValue = Math.Clamp(value, 0.15, 0.9);
            if (Math.Abs(_subtitleBackgroundOpacity - normalizedValue) < 0.001)
                return;

            this.RaiseAndSetIfChanged(ref _subtitleBackgroundOpacity, normalizedValue);
            foreach (var line in SubtitleLanguageLines)
                line.SubtitleBackgroundBrush = SubtitleBackgroundBrush;
            this.RaisePropertyChanged(nameof(SubtitleBackgroundBrush));
        }
    }

    public string StartStopText => IsRunning ? "Stop" : "Start";
    public string AudioDeviceLabel => SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker
        ? "Speaker / System Audio"
        : "Microphone";

    public ICommand StartStopCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ToggleSubtitleModeCommand { get; }
    public ICommand IncreaseSubtitleFontCommand { get; }
    public ICommand DecreaseSubtitleFontCommand { get; }
    public ICommand IncreaseSubtitleBackgroundOpacityCommand { get; }
    public ICommand DecreaseSubtitleBackgroundOpacityCommand { get; }
    public ICommand ToggleTranscriptVisibilityCommand { get; }
    public ICommand ToggleSubtitleTextColorCommand { get; }

    public MainViewModel(LipiSessionContext session)
    {
        _session = session;
        var wasRunning = IsRunning;

        _subtitleInactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _subtitleInactivityTimer.Tick += (_, _) => ClearSubtitleState();

        StartStopCommand = ReactiveCommand.CreateFromTask(ToggleStartStopAsync);
        
        // Subscribe to handle any errors in StartStopCommand
        ((ReactiveCommand<Unit, Unit>)StartStopCommand).ThrownExceptions.Subscribe(ex =>
        {
            Status = $"Error: {ex.Message}";
            ResetToDefaultView();
        });
        
        ShowSettingsCommand = ReactiveCommand.Create(() =>
        {
            if (IsSubtitleMode)
                IsSubtitleMode = false;

            IsSettingsVisible = !IsSettingsVisible;
        });
        ToggleSubtitleModeCommand = ReactiveCommand.Create(() => IsSubtitleMode = !IsSubtitleMode);
        IncreaseSubtitleFontCommand = ReactiveCommand.Create(() => SubtitleFontSize += 2);
        DecreaseSubtitleFontCommand = ReactiveCommand.Create(() => SubtitleFontSize -= 2);
        IncreaseSubtitleBackgroundOpacityCommand = ReactiveCommand.Create(() => SubtitleBackgroundOpacity += 0.05);
        DecreaseSubtitleBackgroundOpacityCommand = ReactiveCommand.Create(() => SubtitleBackgroundOpacity -= 0.05);
        ToggleTranscriptVisibilityCommand = ReactiveCommand.Create(() => ShowTranscriptText = !ShowTranscriptText);
        ToggleSubtitleTextColorCommand = ReactiveCommand.Create(() => UseBlackSubtitleText = !UseBlackSubtitleText);

        this.WhenAnyValue(x => x.IsRunning).Subscribe(_ =>
        {
            this.RaisePropertyChanged(nameof(StartStopText));
            this.RaisePropertyChanged(nameof(StartStopBackground));

            if (wasRunning && !IsRunning)
                ResetToDefaultView();

            wasRunning = IsRunning;
        });

        _audioInputDeviceService.DevicesChanged += OnInputDevicesChanged;
        _audioOutputDeviceService.DevicesChanged += OnInputDevicesChanged;

        LoadFromApiSession();
        LoadAudioSourceModes();
        LoadConnectionModes();
        _ = RefreshInputDevicesAsync();
    }

    private void LoadAudioSourceModes()
    {
        AudioSourceModes.Clear();
        AudioSourceModes.Add(new AudioSourceOption { Mode = AudioSourceMode.Microphone, DisplayName = "Microphone" });
        AudioSourceModes.Add(new AudioSourceOption { Mode = AudioSourceMode.Speaker, DisplayName = "Speaker / System Audio" });
        SelectedAudioSourceMode = AudioSourceModes.FirstOrDefault(a => a.Mode == AudioSourceMode.Microphone) ?? AudioSourceModes.FirstOrDefault();
    }

    private void LoadConnectionModes()
    {
        ConnectionModes.Clear();
        ConnectionModes.Add(new ConnectionModeOption { Mode = LipiConnectionMode.Server, DisplayName = "Server" });
        ConnectionModes.Add(new ConnectionModeOption { Mode = LipiConnectionMode.DirectAzure, DisplayName = "Direct Azure" });

        var configuredMode = ConfigurationService.Instance.Config.Realtime.DefaultConnectionMode;
        var parsedMode = Enum.TryParse<LipiConnectionMode>(configuredMode, ignoreCase: true, out var mode)
            ? mode
            : LipiConnectionMode.Server;

        SelectedConnectionMode = ConnectionModes.FirstOrDefault(c => c.Mode == parsedMode)
            ?? ConnectionModes.FirstOrDefault(c => c.Mode == LipiConnectionMode.Server);
    }

    private void LoadFromApiSession()
    {
        foreach (var language in _session.AvailableLanguages)
        {
            var item = new SelectableLanguage
            {
                Code = language.Code,
                DisplayName = language.DisplayName
            };

            SourceLanguages.Add(item);
            var target = new SelectableLanguage
            {
                Code = language.Code,
                DisplayName = language.DisplayName,
                IsSelected = false
            };

            target.PropertyChanged += OnTargetLanguagePropertyChanged;
            TargetLanguages.Add(target);
        }

        SelectedSourceLanguage = SourceLanguages.FirstOrDefault(l =>
            l.Code.Equals(_session.SourceLanguage, StringComparison.OrdinalIgnoreCase))
            ?? SourceLanguages.FirstOrDefault();
    }

    private void OnTargetLanguagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_enforcingSelection || e.PropertyName != nameof(SelectableLanguage.IsSelected) || sender is not SelectableLanguage changed)
            return;

        if (!changed.IsSelected)
            return;

        var selectedCount = TargetLanguages.Count(t => t.IsSelected);
        if (selectedCount <= 3)
            return;

        _enforcingSelection = true;
        changed.IsSelected = false;
        _enforcingSelection = false;

        Status = "Maximum 3 output languages allowed.";
    }

    private async Task RefreshInputDevicesAsync()
    {
        await _inputDeviceRefreshLock.WaitAsync();
        try
        {
            var previousSelectedId = SelectedInputDevice?.Id;
            var knownIds = new HashSet<string>(_knownInputDeviceIds, StringComparer.OrdinalIgnoreCase);
            var previousSelectedOutputId = SelectedOutputDevice?.Id;
            var knownOutputIds = new HashSet<string>(_knownOutputDeviceIds, StringComparer.OrdinalIgnoreCase);

            var devices = _audioInputDeviceService
                .GetInputDevices()
                .OrderByDescending(d => d.IsDefault && d.IsBluetoothOrHeadset)
                .ThenByDescending(d => d.IsBluetoothOrHeadset)
                .ThenByDescending(d => d.IsDefault)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var outputDevices = _audioOutputDeviceService
                .GetOutputDevices()
                .OrderByDescending(d => d.IsDefault && d.IsBluetoothOrHeadset)
                .ThenByDescending(d => d.IsDefault)
                .ThenByDescending(d => d.IsBluetoothOrHeadset)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var preferredNewDevice = devices.FirstOrDefault(d => d.IsBluetoothOrHeadset && !knownIds.Contains(d.Id));
            var preferredDevice = preferredNewDevice ?? devices.FirstOrDefault(d => d.IsBluetoothOrHeadset);
            if ((preferredDevice != null || _autoSelectInputDevice) && TryPromotePreferredDeviceToDefault(devices, preferredDevice?.Id))
            {
                devices = _audioInputDeviceService
                    .GetInputDevices()
                    .OrderByDescending(d => d.IsDefault && d.IsBluetoothOrHeadset)
                    .ThenByDescending(d => d.IsBluetoothOrHeadset)
                    .ThenByDescending(d => d.IsDefault)
                    .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var nextSelection = preferredDevice != null
                ? devices.FirstOrDefault(d => string.Equals(d.Id, preferredDevice.Id, StringComparison.OrdinalIgnoreCase))
                : null;

            var restoredPreviousSelection = !string.IsNullOrWhiteSpace(previousSelectedId)
                ? devices.FirstOrDefault(d => string.Equals(d.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
                : null;

            if (restoredPreviousSelection == null && !string.IsNullOrWhiteSpace(previousSelectedId))
                _autoSelectInputDevice = true;

            if (nextSelection == null && !_autoSelectInputDevice)
                nextSelection = restoredPreviousSelection;

            if (nextSelection == null)
                nextSelection = PickPreferredInputDevice(devices, previousSelectedId);

            var preferredOutput = outputDevices.FirstOrDefault(d => d.IsDefault)
                ?? outputDevices.FirstOrDefault(d => d.IsBluetoothOrHeadset);

            var nextOutputSelection = preferredOutput != null
                ? outputDevices.FirstOrDefault(d => string.Equals(d.Id, preferredOutput.Id, StringComparison.OrdinalIgnoreCase))
                : null;

            var restoredPreviousOutputSelection = !string.IsNullOrWhiteSpace(previousSelectedOutputId)
                ? outputDevices.FirstOrDefault(d => string.Equals(d.Id, previousSelectedOutputId, StringComparison.OrdinalIgnoreCase))
                : null;

            if (restoredPreviousOutputSelection == null && !string.IsNullOrWhiteSpace(previousSelectedOutputId))
                _autoSelectOutputDevice = true;

            if (nextOutputSelection == null && !_autoSelectOutputDevice)
                nextOutputSelection = restoredPreviousOutputSelection;

            if (nextOutputSelection == null)
                nextOutputSelection = PickPreferredOutputDevice(outputDevices, previousSelectedOutputId);

            _knownInputDeviceIds = devices
                .Select(d => d.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _knownOutputDeviceIds = outputDevices
                .Select(d => d.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _isRefreshingInputDevices = true;
                _isRefreshingOutputDevices = true;
                try
                {
                    InputDevices.Clear();
                    foreach (var device in devices)
                        InputDevices.Add(device);

                    OutputDevices.Clear();
                    foreach (var device in outputDevices)
                        OutputDevices.Add(device);

                    if (nextSelection == null)
                    {
                        SelectedInputDevice = null;
                        _autoSelectInputDevice = true;
                    }
                    else
                    {
                        SelectedInputDevice = InputDevices.FirstOrDefault(d => string.Equals(d.Id, nextSelection.Id, StringComparison.OrdinalIgnoreCase));
                    }

                    if (nextOutputSelection == null)
                    {
                        SelectedOutputDevice = null;
                        _autoSelectOutputDevice = true;
                    }
                    else
                    {
                        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => string.Equals(d.Id, nextOutputSelection.Id, StringComparison.OrdinalIgnoreCase));
                    }
                }
                finally
                {
                    _isRefreshingInputDevices = false;
                    _isRefreshingOutputDevices = false;
                }

                this.RaisePropertyChanged(nameof(AvailableAudioDevices));
                this.RaisePropertyChanged(nameof(SelectedAudioDevice));

                if (SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker && SelectedOutputDevice == null)
                    Status = "No active speaker output detected.";
                else if (SelectedInputDevice == null)
                    Status = "No active microphone detected.";
            });

            await TrySwitchActiveAudioDeviceAsync();
        }
        finally
        {
            _inputDeviceRefreshLock.Release();
        }
    }

    private async Task ToggleStartStopAsync()
    {
        if (!IsRunning)
            await StartAsync();
        else
            await StopAsync();
    }

    private async Task StartAsync()
    {
        if (SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker)
        {
            if (SelectedOutputDevice == null)
            {
                Status = "Select a speaker output device.";
                return;
            }
        }
        else if (SelectedInputDevice == null)
        {
            Status = "Select a microphone.";
            return;
        }

        var captureSelection = await ResolveSelectedAudioCaptureSelectionAsync();
        if (captureSelection == null)
        {
            Status = SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker
                ? "Selected speaker is no longer available. Choose another output device."
                : "Selected microphone is no longer available. Reconnect it or choose another device.";
            IsSettingsVisible = true;
            return;
        }

        if (SelectedSourceLanguage == null)
        {
            Status = "Select speaking language.";
            return;
        }

        var selectedTargets = TargetLanguages.Where(t => t.IsSelected).Select(t => t.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selectedTargets.Count == 0)
        {
            Status = "Select at least one translation language.";
            return;
        }

        if (selectedTargets.Count > 3)
        {
            Status = "Maximum 3 output languages allowed.";
            return;
        }

        selectedTargets.RemoveAll(c => c.Equals(SelectedSourceLanguage.Code, StringComparison.OrdinalIgnoreCase));
        if (selectedTargets.Count == 0)
        {
            Status = "Target languages must be different from source language.";
            return;
        }

        Status = "Starting...";
        IsSettingsVisible = false;
        Bubbles.Clear();
        _currentRecognizingBubble = null;
        _activeTargetLanguages.Clear();
        ClearSubtitleState();
        ShowTranscriptText = false;
        SubtitleTranslatedText = "Ready to translate";

        var startSession = await _authService.StartSessionAsync(_session.MeetingId, _session.SessionToken);
        if (!startSession.Success || !startSession.SessionId.HasValue)
        {
            Status = startSession.Message ?? "Unable to start session.";
            IsSettingsVisible = true;
            ClearSubtitleState();
            return;
        }

        _activeSessionId = startSession.SessionId.Value.ToString();

        foreach (var code in selectedTargets)
        {
            var lang = _session.AvailableLanguages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            _activeTargetLanguages.Add(new LanguageInfo
            {
                Code = code,
                DisplayName = lang?.DisplayName ?? code
            });
        }

        ReplaceRealtimeClient(CreateRealtimeClient());
        if (_realtimeClient == null)
        {
            Status = "Unable to create realtime client.";
            IsSettingsVisible = true;
            return;
        }

        await _realtimeClient.StartAsync(
            _session.HubUrl,
            _session.SessionToken,
            _session.MeetingId,
            _activeSessionId,
            SelectedSourceLanguage.Code,
            selectedTargets,
            captureSelection);

        IsSubtitleMode = true;
        Status = BuildLiveStatus();
    }

    private async Task StopAsync()
    {
        if (_realtimeClient != null)
            await _realtimeClient.StopAsync();

        Status = "Stopped";
        ResetToDefaultView();
    }

    private ILipiRealtimeClient CreateRealtimeClient() =>
        SelectedConnectionMode?.Mode == LipiConnectionMode.DirectAzure
            ? new DirectAzureLipiRealtimeClient(_authService)
            : new LipiRealtimeService();

    private void ReplaceRealtimeClient(ILipiRealtimeClient newClient)
    {
        if (ReferenceEquals(_realtimeClient, newClient))
            return;

        if (_realtimeClient != null)
        {
            _realtimeClient.RecognizingReceived -= OnRealtimeRecognizingReceived;
            _realtimeClient.RecognizedReceived -= OnRealtimeRecognizedReceived;
            _realtimeClient.ErrorReceived -= OnRealtimeErrorReceived;
            _realtimeClient.RunningStateChanged -= OnRealtimeRunningStateChanged;
            _realtimeClient.Dispose();
        }

        _realtimeClient = newClient;
        _realtimeClient.RecognizingReceived += OnRealtimeRecognizingReceived;
        _realtimeClient.RecognizedReceived += OnRealtimeRecognizedReceived;
        _realtimeClient.ErrorReceived += OnRealtimeErrorReceived;
        _realtimeClient.RunningStateChanged += OnRealtimeRunningStateChanged;
    }

    private void OnRealtimeRecognizingReceived(string text, Dictionary<string, string> translations) =>
        Dispatcher.UIThread.Post(() => OnRecognizing(text, translations));

    private void OnRealtimeRecognizedReceived(string transcript, Dictionary<string, string> translations) =>
        Dispatcher.UIThread.Post(() => OnRecognized(transcript, translations));

    private void OnRealtimeErrorReceived(string message) =>
        Dispatcher.UIThread.Post(() => _ = HandleRealtimeErrorAsync(message));

    private void OnRealtimeRunningStateChanged(bool running) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsRunning = running;
        });

    private void OnRecognizing(string transcript, Dictionary<string, string> translations)
    {
        var bubble = _currentRecognizingBubble;

        if (bubble == null)
        {
            bubble = CreateBubble(isRecognizing: true);
            _currentRecognizingBubble = bubble;
            Bubbles.Add(bubble);
            TrimBubbles();
        }

        bubble.Transcript = transcript;
        bubble.IsRecognizing = true;
        ApplyTranslationsToBubble(bubble, translations);
        UpdateRecognizingSubtitleState(transcript, translations);
    }

    private void OnRecognized(string transcript, Dictionary<string, string> translations)
    {
        var bubble = _currentRecognizingBubble;
        if (bubble == null)
        {
            bubble = CreateBubble(isRecognizing: false);
            Bubbles.Add(bubble);
            TrimBubbles();
        }

        bubble.Transcript = transcript;
        bubble.IsRecognizing = false;
        ApplyTranslationsToBubble(bubble, translations);
        UpdateRecognizedSubtitleState(transcript, translations);

        _currentRecognizingBubble = null;
    }

    private void UpdateRecognizingSubtitleState(string transcript, Dictionary<string, string> translations)
    {
        SubtitleTranslatedText = ResolveSubtitleTranslatedText(translations);
        SubtitleTranscriptText = transcript;
        EnsureSubtitleLanguageLines();

        foreach (var line in SubtitleLanguageLines)
        {
            line.DisplayFontSize = SubtitleFontSize;

            var hasRecognizingText = translations.TryGetValue(line.LanguageCode, out var translated) && !string.IsNullOrWhiteSpace(translated);
            if (!hasRecognizingText)
                continue;

            if (!line.IsLatestRecognizing && !string.IsNullOrWhiteSpace(line.LatestText))
                line.OlderRecognizedText = line.LatestText;

            line.LatestText = translated;
            line.IsLatestRecognizing = true;
        }

        this.RaisePropertyChanged(nameof(HasSubtitleLanguageLines));
        this.RaisePropertyChanged(nameof(HasSubtitleContent));
        this.RaisePropertyChanged(nameof(ShowSubtitleTranslatedPlaceholder));
        RestartSubtitleInactivityTimer();
    }

    private void UpdateRecognizedSubtitleState(string transcript, Dictionary<string, string> translations)
    {
        SubtitleTranslatedText = ResolveSubtitleTranslatedText(translations);
        SubtitleTranscriptText = transcript;
        EnsureSubtitleLanguageLines();

        foreach (var line in SubtitleLanguageLines)
        {
            line.DisplayFontSize = SubtitleFontSize;
            if (translations.TryGetValue(line.LanguageCode, out var translated) && !string.IsNullOrWhiteSpace(translated))
                line.LatestText = translated;

            line.IsLatestRecognizing = false;
        }

        RefreshSubtitleTranslations(translations);
        this.RaisePropertyChanged(nameof(HasSubtitleLanguageLines));
        this.RaisePropertyChanged(nameof(HasSubtitleContent));
        this.RaisePropertyChanged(nameof(ShowSubtitleTranslatedPlaceholder));
        RestartSubtitleInactivityTimer();
    }

    private string ResolveSubtitleTranslatedText(Dictionary<string, string> translations)
    {
        foreach (var target in _activeTargetLanguages)
        {
            if (translations.TryGetValue(target.Code, out var translated) && !string.IsNullOrWhiteSpace(translated))
                return translated;
        }

        var fallback = translations.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return fallback ?? string.Empty;
    }

    private void RestartSubtitleInactivityTimer()
    {
        _subtitleInactivityTimer.Stop();
        _subtitleInactivityTimer.Start();
    }

    private void ClearSubtitleState()
    {
        _subtitleInactivityTimer.Stop();
        SubtitleTranslatedText = string.Empty;
        SubtitleTranscriptText = string.Empty;
        SubtitleTranslations.Clear();
        SubtitleLanguageLines.Clear();
        this.RaisePropertyChanged(nameof(HasSubtitleTranslations));
        this.RaisePropertyChanged(nameof(HasSubtitleLanguageLines));
        this.RaisePropertyChanged(nameof(HasSubtitleContent));
        this.RaisePropertyChanged(nameof(ShowSubtitleTranslatedPlaceholder));
    }

    private void RefreshSubtitleTranslations(Dictionary<string, string> translations)
    {
        SubtitleTranslations.Clear();

        foreach (var target in _activeTargetLanguages)
        {
            if (!translations.TryGetValue(target.Code, out var translated) || string.IsNullOrWhiteSpace(translated))
                continue;

            SubtitleTranslations.Add(new TranslationLine
            {
                LanguageCode = target.Code,
                LanguageName = target.DisplayName,
                Text = translated,
                DisplayFontSize = SubtitleFontSize
            });
        }

        this.RaisePropertyChanged(nameof(HasSubtitleTranslations));
    }

    private void ApplySubtitleTheme()
    {
        foreach (var line in SubtitleLanguageLines)
        {
            line.SubtitleBackgroundBrush = SubtitleBackgroundBrush;
            line.PrimaryForeground = SubtitlePrimaryForeground;
            line.SecondaryForeground = SubtitleSecondaryForeground;
            line.RecognizingForeground = SubtitleRecognizingForeground;
        }
    }

    private void EnsureSubtitleLanguageLines()
    {
        foreach (var target in _activeTargetLanguages)
        {
            if (SubtitleLanguageLines.Any(line => line.LanguageCode.Equals(target.Code, StringComparison.OrdinalIgnoreCase)))
                continue;

            SubtitleLanguageLines.Add(new SubtitleLanguageLine
            {
                LanguageCode = target.Code,
                LanguageName = target.DisplayName,
                DisplayFontSize = SubtitleFontSize,
                SubtitleBackgroundBrush = SubtitleBackgroundBrush,
                PrimaryForeground = SubtitlePrimaryForeground,
                SecondaryForeground = SubtitleSecondaryForeground,
                RecognizingForeground = SubtitleRecognizingForeground
            });
        }

        ApplySubtitleTheme();
    }

    private string FormatRealtimeErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Realtime error.";

        if (message.Contains("Quota exceeded", StringComparison.OrdinalIgnoreCase))
        {
            var provider = SelectedConnectionMode?.Mode == LipiConnectionMode.DirectAzure
                ? "Azure Speech"
                : "server realtime";

            return $"{provider} quota exceeded. Check active session limits, pricing tier, or usage quota. {message}";
        }

        if (message.Contains("1007", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("remote host", StringComparison.OrdinalIgnoreCase))
        {
            return $"Realtime connection closed by service. {message}";
        }

        return message;
    }

    private async Task HandleRealtimeErrorAsync(string message)
    {
        var formattedMessage = FormatRealtimeErrorMessage(message);
        _ = ReportClientErrorAsync("CLIENT_RUNTIME_ERROR", formattedMessage);

        if (_isHandlingRealtimeError)
        {
            Status = formattedMessage;
            return;
        }

        _isHandlingRealtimeError = true;
        try
        {
            if (_realtimeClient != null)
            {
                try
                {
                    await _realtimeClient.StopAsync();
                }
                catch
                {
                }
            }

            IsRunning = false;
            _currentRecognizingBubble = null;
            ResetToDefaultView();
            Status = formattedMessage;
        }
        finally
        {
            _isHandlingRealtimeError = false;
        }
    }

    private void RaiseSubtitleStateProperties()
    {
        this.RaisePropertyChanged(nameof(IsHeaderVisible));
        this.RaisePropertyChanged(nameof(IsStatusRowVisible));
        this.RaisePropertyChanged(nameof(HeaderChromeOpacity));
        this.RaisePropertyChanged(nameof(IsBubbleListVisible));
        this.RaisePropertyChanged(nameof(IsSettingsPanelVisible));
        this.RaisePropertyChanged(nameof(HeaderPanelBackground));
        this.RaisePropertyChanged(nameof(SubtitleModeButtonText));
    }

    private void ResetToDefaultView()
    {
        if (IsSubtitleMode)
            IsSubtitleMode = false;

        IsSettingsVisible = true;
        ClearSubtitleState();
        _activeSessionId = null;
    }

    private async Task ReportClientErrorAsync(string errorCode, string message)
    {
        if (string.IsNullOrWhiteSpace(_session.SessionToken) ||
            string.IsNullOrWhiteSpace(_session.MeetingId) ||
            string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var fingerprint = $"{errorCode}|{message.Trim()}|{SelectedConnectionMode?.Mode}|{_activeSessionId}";
        var now = DateTime.UtcNow;
        if (string.Equals(_lastReportedErrorFingerprint, fingerprint, StringComparison.Ordinal) &&
            now - _lastReportedErrorAtUtc < TimeSpan.FromSeconds(15))
        {
            return;
        }

        _lastReportedErrorFingerprint = fingerprint;
        _lastReportedErrorAtUtc = now;

        try
        {
            await _authService.ReportClientErrorAsync(new LipiDirectClientErrorReportRequest
            {
                MeetingId = _session.MeetingId,
                SessionId = _activeSessionId,
                SourceLanguage = SelectedSourceLanguage?.Code ?? _session.SourceLanguage,
                ConnectionMode = SelectedConnectionMode?.Mode.ToString(),
                ErrorCode = errorCode,
                Message = message.Trim(),
                OccurredAtUtc = now
            }, _session.SessionToken);
        }
        catch
        {
        }
    }

    private TranscriptBubble CreateBubble(bool isRecognizing)
    {
        var bubble = new TranscriptBubble
        {
            IsRecognizing = isRecognizing
        };

        foreach (var target in _activeTargetLanguages)
        {
            bubble.Translations.Add(new TranslationLine
            {
                LanguageCode = target.Code,
                LanguageName = target.DisplayName,
                Text = string.Empty
            });
        }

        return bubble;
    }

    private static void ApplyTranslationsToBubble(TranscriptBubble bubble, Dictionary<string, string> translations)
    {
        foreach (var line in bubble.Translations)
        {
            if (translations.TryGetValue(line.LanguageCode, out var value))
                line.Text = value;
        }
    }

    private void TrimBubbles()
    {
        while (Bubbles.Count > MaxBubbles)
            Bubbles.RemoveAt(0);
    }

    private void OnInputDevicesChanged(object? sender, EventArgs e)
    {
        _ = RefreshInputDevicesAsync();
    }

    private void EnsureSelectedAudioDeviceForCurrentSource()
    {
        if (SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker)
        {
            if (SelectedOutputDevice != null)
                return;

            var preferredOutput = PickPreferredOutputDevice(OutputDevices.ToList(), previousSelectedId: null);
            if (preferredOutput != null)
            {
                _isRefreshingOutputDevices = true;
                try
                {
                    SelectedOutputDevice = OutputDevices.FirstOrDefault(d => string.Equals(d.Id, preferredOutput.Id, StringComparison.OrdinalIgnoreCase)) ?? preferredOutput;
                }
                finally
                {
                    _isRefreshingOutputDevices = false;
                }

                this.RaisePropertyChanged(nameof(SelectedAudioDevice));
                return;
            }

            _ = RefreshInputDevicesAsync();
            return;
        }

        if (SelectedInputDevice != null)
            return;

        var preferredInput = PickPreferredInputDevice(InputDevices.ToList(), previousSelectedId: null);
        if (preferredInput != null)
        {
            _isRefreshingInputDevices = true;
            try
            {
                SelectedInputDevice = InputDevices.FirstOrDefault(d => string.Equals(d.Id, preferredInput.Id, StringComparison.OrdinalIgnoreCase)) ?? preferredInput;
            }
            finally
            {
                _isRefreshingInputDevices = false;
            }

            this.RaisePropertyChanged(nameof(SelectedAudioDevice));
            return;
        }

        _ = RefreshInputDevicesAsync();
    }

    private bool TryPromotePreferredDeviceToDefault(IReadOnlyList<AudioInputDevice> devices, string? preferredDeviceId)
    {
        var preferredDevice = !string.IsNullOrWhiteSpace(preferredDeviceId)
            ? devices.FirstOrDefault(d => string.Equals(d.Id, preferredDeviceId, StringComparison.OrdinalIgnoreCase))
            : devices.FirstOrDefault(d => d.IsBluetoothOrHeadset);

        if (preferredDevice == null || preferredDevice.IsDefault)
            return false;

        return _audioInputDeviceService.TrySetDefaultInputDevice(preferredDevice.Id);
    }

    private static AudioInputDevice? PickPreferredInputDevice(IReadOnlyList<AudioInputDevice> devices, string? previousSelectedId)
    {
        return devices.FirstOrDefault(d => d.IsDefault && d.IsBluetoothOrHeadset)
            ?? devices.FirstOrDefault(d => d.IsBluetoothOrHeadset)
            ?? devices.FirstOrDefault(d => d.IsDefault)
            ?? devices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(previousSelectedId) && string.Equals(d.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.DeviceNumber.HasValue)
            ?? devices.FirstOrDefault();
    }

    private static AudioOutputDevice? PickPreferredOutputDevice(IReadOnlyList<AudioOutputDevice> devices, string? previousSelectedId)
    {
        return devices.FirstOrDefault(d => d.IsDefault && d.IsBluetoothOrHeadset)
            ?? devices.FirstOrDefault(d => d.IsDefault)
            ?? devices.FirstOrDefault(d => d.IsBluetoothOrHeadset)
            ?? devices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(previousSelectedId) && string.Equals(d.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault();
    }

    private async Task<int?> ResolveSelectedInputDeviceNumberAsync()
    {
        if (SelectedInputDevice?.IsDefault == true)
            return DefaultWaveInDeviceNumber;

        if (SelectedInputDevice?.DeviceNumber.HasValue == true)
            return SelectedInputDevice.DeviceNumber.Value;

        if (!string.IsNullOrWhiteSpace(SelectedInputDevice?.Id))
        {
            var resolved = _audioInputDeviceService.ResolveInputDeviceNumber(SelectedInputDevice.Id);
            if (resolved.HasValue && SelectedInputDevice != null)
            {
                if (SelectedInputDevice.IsDefault)
                    return DefaultWaveInDeviceNumber;

                SelectedInputDevice.DeviceNumber = resolved.Value;
                return resolved.Value;
            }
        }

        await RefreshInputDevicesAsync();
        return SelectedInputDevice?.DeviceNumber;
    }

    private async Task<AudioCaptureSelection?> ResolveSelectedAudioCaptureSelectionAsync()
    {
        if (SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker)
        {
            if (!string.IsNullOrWhiteSpace(SelectedOutputDevice?.Id))
            {
                return new AudioCaptureSelection
                {
                    SourceMode = AudioSourceMode.Speaker,
                    OutputDeviceId = SelectedOutputDevice.Id
                };
            }

            await RefreshInputDevicesAsync();

            return !string.IsNullOrWhiteSpace(SelectedOutputDevice?.Id)
                ? new AudioCaptureSelection
                {
                    SourceMode = AudioSourceMode.Speaker,
                    OutputDeviceId = SelectedOutputDevice.Id
                }
                : null;
        }

        if (SelectedInputDevice == null)
            return null;

        var resolvedDeviceNumber = await ResolveSelectedInputDeviceNumberAsync();

        return resolvedDeviceNumber.HasValue
            ? new AudioCaptureSelection
            {
                SourceMode = AudioSourceMode.Microphone,
                InputDeviceNumber = resolvedDeviceNumber.Value
            }
            : null;
    }

    private async Task TrySwitchActiveAudioDeviceAsync()
    {
        if (!IsRunning || _realtimeClient == null)
            return;

        var selection = await ResolveSelectedAudioCaptureSelectionAsync();

        if (selection == null)
        {
            Status = SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker
                ? "Speaker output changed. No active fallback device is available."
                : "Microphone changed. No active fallback device is available.";
            return;
        }

        try
        {
            await _realtimeClient.SwitchCaptureDeviceAsync(selection);
            Status = BuildLiveStatus();
        }
        catch (Exception ex)
        {
            Status = SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker
                ? $"Speaker switch failed: {ex.Message}"
                : $"Microphone switch failed: {ex.Message}";
        }
    }

    private string BuildLiveStatus()
    {
        return SelectedAudioSourceMode?.Mode == AudioSourceMode.Speaker
            ? $"Live - Speaker: {SelectedOutputDevice?.Name ?? "Unknown"}"
            : $"Live - Mic: {SelectedInputDevice?.Name ?? "Unknown"}";
    }

    public void Dispose()
    {
        _subtitleInactivityTimer.Stop();
        foreach (var target in TargetLanguages)
            target.PropertyChanged -= OnTargetLanguagePropertyChanged;

        _audioInputDeviceService.DevicesChanged -= OnInputDevicesChanged;
        _audioInputDeviceService.Dispose();
        _audioOutputDeviceService.DevicesChanged -= OnInputDevicesChanged;
        _audioOutputDeviceService.Dispose();
        _inputDeviceRefreshLock.Dispose();

        if (_realtimeClient != null)
        {
            _realtimeClient.RecognizingReceived -= OnRealtimeRecognizingReceived;
            _realtimeClient.RecognizedReceived -= OnRealtimeRecognizedReceived;
            _realtimeClient.ErrorReceived -= OnRealtimeErrorReceived;
            _realtimeClient.RunningStateChanged -= OnRealtimeRunningStateChanged;
            _realtimeClient.Dispose();
        }
    }
}