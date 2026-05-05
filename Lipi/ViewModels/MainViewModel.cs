using Lipi.Models;
using Lipi.Services;
using NAudio.Wave;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Reactive;
using Avalonia.Threading;

namespace Lipi.ViewModels;

public class MainViewModel : ReactiveObject, IDisposable
{
    private const int MaxBubbles = 100;
    private readonly MeetingAuthenticationService _authService = new();
    private readonly LipiSessionContext _session;
    private ILipiRealtimeClient? _realtimeClient;

    private AudioInputDevice? _selectedInputDevice;
    private SelectableLanguage? _selectedSourceLanguage;
    private ConnectionModeOption? _selectedConnectionMode;
    private bool _isRunning;
    private bool _isSettingsVisible = true;
    private bool _isHorizontal = false;
    private bool _isSubtitleMode;
    private bool _isSubtitleChromeVisible = true;
    private bool _showTranscriptText;
    private double _subtitleFontSize = 16;
    private double _subtitleBackgroundOpacity = 0.5;
    private string _subtitleTranslatedText = string.Empty;
    private string _subtitleTranscriptText = string.Empty;
    private string _status = "Ready";
    private bool _enforcingSelection;
    private TranscriptBubble? _currentRecognizingBubble;
    private readonly List<LanguageInfo> _activeTargetLanguages = [];

    public ObservableCollection<AudioInputDevice> InputDevices { get; } = [];
    public ObservableCollection<ConnectionModeOption> ConnectionModes { get; } = [];
    public ObservableCollection<SelectableLanguage> SourceLanguages { get; } = [];
    public ObservableCollection<SelectableLanguage> TargetLanguages { get; } = [];
    public ObservableCollection<TranscriptBubble> Bubbles { get; } = [];
    public ObservableCollection<TranslationLine> SubtitleTranslations { get; } = [];
    public ObservableCollection<SubtitleLanguageLine> SubtitleLanguageLines { get; } = [];

    public AudioInputDevice? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set => this.RaiseAndSetIfChanged(ref _selectedInputDevice, value);
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

    public bool IsHorizontal
    {
        get => _isHorizontal;
        set => this.RaiseAndSetIfChanged(ref _isHorizontal, value);
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
    public bool IsBubbleListVisible => !IsSubtitleMode;
    public bool IsSettingsPanelVisible => !IsSubtitleMode && IsSettingsVisible;
    public string HeaderPanelBackground => IsSubtitleMode ? "#7A000000" : "Transparent";
    public bool HasSubtitleContent => SubtitleLanguageLines.Count > 0 || !string.IsNullOrWhiteSpace(SubtitleTranscriptText);
    public bool HasSubtitleTranslations => SubtitleTranslations.Count > 0;
    public bool HasSubtitleLanguageLines => SubtitleLanguageLines.Count > 0;
    public bool SubtitleTranscriptVisible => ShowTranscriptText && !string.IsNullOrWhiteSpace(SubtitleTranscriptText);
    public double SubtitleTranscriptFontSize => Math.Max(14, Math.Round(SubtitleFontSize * 0.55, MidpointRounding.AwayFromZero));
    public string SubtitleBackgroundBrush => $"#{(int)Math.Round(SubtitleBackgroundOpacity * 255, MidpointRounding.AwayFromZero):X2}161616";
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

    public ICommand StartStopCommand { get; }
    public ICommand ToggleOrientationCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ToggleSubtitleModeCommand { get; }
    public ICommand IncreaseSubtitleFontCommand { get; }
    public ICommand DecreaseSubtitleFontCommand { get; }
    public ICommand IncreaseSubtitleBackgroundOpacityCommand { get; }
    public ICommand DecreaseSubtitleBackgroundOpacityCommand { get; }
    public ICommand ToggleTranscriptVisibilityCommand { get; }

    public MainViewModel(LipiSessionContext session)
    {
        _session = session;

        StartStopCommand = ReactiveCommand.CreateFromTask(ToggleStartStopAsync);
        
        // Subscribe to handle any errors in StartStopCommand
        ((ReactiveCommand<Unit, Unit>)StartStopCommand).ThrownExceptions.Subscribe(ex =>
        {
            Status = $"Error: {ex.Message}";
            IsSettingsVisible = true;
        });
        
        ToggleOrientationCommand = ReactiveCommand.Create(() => IsHorizontal = !IsHorizontal);
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

        this.WhenAnyValue(x => x.IsRunning).Subscribe(_ =>
        {
            this.RaisePropertyChanged(nameof(StartStopText));
            this.RaisePropertyChanged(nameof(StartStopBackground));
        });

        LoadFromApiSession();
        LoadConnectionModes();
        LoadInputDevices();
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

    private void LoadInputDevices()
    {
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            InputDevices.Add(new AudioInputDevice
            {
                DeviceNumber = i,
                Name = caps.ProductName
            });
        }

        SelectedInputDevice = InputDevices.FirstOrDefault();
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
        if (SelectedInputDevice == null)
        {
            Status = "Select an input audio device.";
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

        var startSession = await _authService.StartSessionAsync(_session.MeetingId, _session.SessionToken);
        if (!startSession.Success || !startSession.SessionId.HasValue)
        {
            Status = startSession.Message ?? "Unable to start session.";
            IsSettingsVisible = true;
            return;
        }

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
            startSession.SessionId.Value.ToString(),
            SelectedSourceLanguage.Code,
            selectedTargets,
            SelectedInputDevice.DeviceNumber);

        Status = "Live";
    }

    private async Task StopAsync()
    {
        if (_realtimeClient != null)
            await _realtimeClient.StopAsync();

        if (IsSubtitleMode)
            IsSubtitleMode = false;

        IsSettingsVisible = true;
        Status = "Stopped";
        ClearSubtitleState();
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
        Dispatcher.UIThread.Post(() => Status = message);

    private void OnRealtimeRunningStateChanged(bool running) =>
        Dispatcher.UIThread.Post(() => IsRunning = running);

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

    private void ClearSubtitleState()
    {
        SubtitleTranslatedText = string.Empty;
        SubtitleTranscriptText = string.Empty;
        SubtitleTranslations.Clear();
        SubtitleLanguageLines.Clear();
        this.RaisePropertyChanged(nameof(HasSubtitleTranslations));
        this.RaisePropertyChanged(nameof(HasSubtitleLanguageLines));
        this.RaisePropertyChanged(nameof(HasSubtitleContent));
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
                SubtitleBackgroundBrush = SubtitleBackgroundBrush
            });
        }
    }

    private void RaiseSubtitleStateProperties()
    {
        this.RaisePropertyChanged(nameof(IsHeaderVisible));
        this.RaisePropertyChanged(nameof(IsStatusRowVisible));
        this.RaisePropertyChanged(nameof(IsBubbleListVisible));
        this.RaisePropertyChanged(nameof(IsSettingsPanelVisible));
        this.RaisePropertyChanged(nameof(HeaderPanelBackground));
        this.RaisePropertyChanged(nameof(SubtitleModeButtonText));
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

    public void Dispose()
    {
        foreach (var target in TargetLanguages)
            target.PropertyChanged -= OnTargetLanguagePropertyChanged;

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