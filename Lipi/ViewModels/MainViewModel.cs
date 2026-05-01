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
    private readonly LipiRealtimeService _realtimeService = new();
    private readonly LipiSessionContext _session;

    private AudioInputDevice? _selectedInputDevice;
    private SelectableLanguage? _selectedSourceLanguage;
    private bool _isRunning;
    private bool _isSettingsVisible = true;
    private bool _isHorizontal = false;
    private string _status = "Ready";
    private bool _enforcingSelection;
    private TranscriptBubble? _currentRecognizingBubble;
    private readonly List<LanguageInfo> _activeTargetLanguages = [];

    public ObservableCollection<AudioInputDevice> InputDevices { get; } = [];
    public ObservableCollection<SelectableLanguage> SourceLanguages { get; } = [];
    public ObservableCollection<SelectableLanguage> TargetLanguages { get; } = [];
    public ObservableCollection<TranscriptBubble> Bubbles { get; } = [];

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

    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        set => this.RaiseAndSetIfChanged(ref _isSettingsVisible, value);
    }

    public bool IsHorizontal
    {
        get => _isHorizontal;
        set => this.RaiseAndSetIfChanged(ref _isHorizontal, value);
    }

    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string StartStopText => IsRunning ? "Stop" : "Start";

    public ICommand StartStopCommand { get; }
    public ICommand ToggleOrientationCommand { get; }
    public ICommand ShowSettingsCommand { get; }

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
        ShowSettingsCommand = ReactiveCommand.Create(() => IsSettingsVisible = !IsSettingsVisible);

        this.WhenAnyValue(x => x.IsRunning).Subscribe(_ => this.RaisePropertyChanged(nameof(StartStopText)));

        LoadFromApiSession();
        LoadInputDevices();

        _realtimeService.RecognizingReceived += (text, translations) =>
            Dispatcher.UIThread.Post(() => OnRecognizing(text, translations));

        _realtimeService.RecognizedReceived += (transcript, translations) =>
            Dispatcher.UIThread.Post(() => OnRecognized(transcript, translations));

        _realtimeService.ErrorReceived += msg =>
            Dispatcher.UIThread.Post(() => Status = msg);

        _realtimeService.RunningStateChanged += running =>
            Dispatcher.UIThread.Post(() => IsRunning = running);
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

        await _realtimeService.StartAsync(
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
        await _realtimeService.StopAsync();
        IsSettingsVisible = true;
        Status = "Stopped";
    }

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

        _currentRecognizingBubble = null;
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

        _realtimeService.Dispose();
    }
}