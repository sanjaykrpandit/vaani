using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using Vaani.Common;
using Vaani.Models;
using Vaani.Services;

namespace Vaani.ViewModels;

public class MainViewModel : ViewModelBase
{
    #region Fields
    private readonly DeviceService _deviceService;
    private readonly TranslationService _translationService;
    private readonly ClientService _clientService = new();
    private readonly AnimatedTextDisplay _textAnimator = new() { WordDelayMs = 80 };
    private DispatcherTimer _deviceRefreshTimer = null!;
    private DispatcherTimer? _sessionExpiryTimer;
    private TranslationSettings _settings = new();

    // Session management
    private string _sessionInfo = string.Empty;
    private string _sessionTimeRemaining = string.Empty;
    private bool _showSessionInfo = false;
    private bool _isSessionExpiring = false;

    private bool _isRunning;
    private string _sourceLanguage = string.Empty;
    private string _targetLanguage = string.Empty;
    private LanguageInfo? _selectedSourceLanguage;
    private LanguageInfo? _selectedTargetLanguage;
    private Voice? _selectedTargetVoice;
    private GenderOption? _selectedGender;
    private string _targetVoice;

    private string _meetingInputDevice = "";
    private string _meetingOutputDevice = "";
    private bool _isSettingsPanelVisible = false;
    private AudioDeviceInfo? _selectedInputDevice;
    private AudioDeviceInfo? _selectedOutputDevice;

    private bool _isMessageViewActive = true;
    private MessageBubble? _currentRecognizingMessage;
    private MessageBubble? _currentOutgoingBubble;
    private MessageBubble? _currentIncomingBubble;
    private const int MaxMessages = 100;
    private string _logText = "";
    private bool _isMicrophoneMuted = false;
    private bool _isSpeakerMuted = false;
    private bool _isTransitioning = false;

    private bool _isMeetingAudioActive = false;

    // Animation cancellation tokens
    private CancellationTokenSource? _recognizingAnimationCts;
    private CancellationTokenSource? _translationAnimationCts;

    #endregion

    #region Constructor

    public MainViewModel()
    {
        _settings = LoadTranslationSettings();

        _deviceService  = new DeviceService();
        _translationService = new TranslationService();
        _translationService.LogMessage += OnLogMessage;
        _translationService.MessageReceived += OnMessageReceived;
        _translationService.TranslationReceived += OnTranslationReceived;
        _translationService.SystemMessage += OnSystemMessage;
        _translationService.SynthesizingStatusChanged += OnSynthesizingStatusChanged;

        InitializeCommands();
        InitializeCollections();
        InitializeDeviceRefreshTimer();
        InitializeSessionMonitoring();
        InitializeReactiveBindings();

        RefreshDevices();
        getConnectedMeetingDevices();        
        DisplayWelcomeMessage();
    }

    #endregion

    #region Properties - Collections

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<LanguageInfo> Languages { get; } = new();
    public ObservableCollection<GenderOption> Genders { get; } = new();
    public ObservableCollection<MessageBubble> Messages { get; } = new();

    #endregion

    #region Properties - Services

    public ToastService Toast { get; } = new();

    #endregion

    #region Properties - Selected Items

    public AudioDeviceInfo? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set
        {
            if (_selectedInputDevice == value)
                return;
                
            // Unselect previous device
            if (_selectedInputDevice != null)
                _selectedInputDevice.IsSelected = false;
            
            this.RaiseAndSetIfChanged(ref _selectedInputDevice, value);
            
            // Select new device
            if (_selectedInputDevice != null)
                _selectedInputDevice.IsSelected = true;
        }
    }

    public AudioDeviceInfo? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set
        {
            if (_selectedOutputDevice == value)
                return;
                
            // Unselect previous device
            if (_selectedOutputDevice != null)
                _selectedOutputDevice.IsSelected = false;
            
            this.RaiseAndSetIfChanged(ref _selectedOutputDevice, value);
            
            // Select new device
            if (_selectedOutputDevice != null)
                _selectedOutputDevice.IsSelected = true;
        }
    }

    public LanguageInfo? SelectedSourceLanguage
    {
        get => _selectedSourceLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSourceLanguage, value);
            if (value != null)
            {
                SourceLanguage = value.Code;
                _settings.SourceLanguage = value.Code;
                UpdateSourceVoice();
            }
        }
    }

    public LanguageInfo? SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTargetLanguage, value);
            if (value != null)
            {
                TargetLanguage = value.Code;
                _settings.TargetLanguage = value.Code;
            }
        }
    }

    public GenderOption? SelectedGender
    {
        get => _selectedGender;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedGender, value);
            if (value != null)
            {
                UpdateSourceVoice();
                AddLog($"🎭 Gender changed to: {value.DisplayName}");
            }
        }
    }

    #endregion

    #region Properties - State

    public bool IsMeetingAudioActive
    {
        get => _isMeetingAudioActive;
        set => this.RaiseAndSetIfChanged(ref _isMeetingAudioActive, value);
    }

    public string LogText
    {
        get => _logText;
        set => this.RaiseAndSetIfChanged(ref _logText, value);
    }

    public bool ConnectionStatus
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public bool IsSettingsPanelVisible
    {
        get => _isSettingsPanelVisible;
        set => this.RaiseAndSetIfChanged(ref _isSettingsPanelVisible, value);
    }

    public bool IsMessageViewActive
    {
        get => _isMessageViewActive;
        set => this.RaiseAndSetIfChanged(ref _isMessageViewActive, value);
    }

    public string SourceLanguage
    {
        get => _sourceLanguage;
        set => this.RaiseAndSetIfChanged(ref _sourceLanguage, value);
    }

    public string TargetLanguage
    {
        get => _targetLanguage;
        set => this.RaiseAndSetIfChanged(ref _targetLanguage, value);
    }

    public string TargetVoice
    {
        get => _targetVoice;
        set => this.RaiseAndSetIfChanged(ref _targetVoice, value);
    }

    public string MeetingInputDeviceName
    {
        get => _meetingInputDevice;
        set => this.RaiseAndSetIfChanged(ref _meetingInputDevice, value);
    }

    public string MeetingOutputDeviceName
    {
        get => _meetingOutputDevice;
        set => this.RaiseAndSetIfChanged(ref _meetingOutputDevice, value);
    }

    public bool IsMicrophoneMuted
    {
        get => _isMicrophoneMuted;
        set
        {
            this.RaiseAndSetIfChanged(ref _isMicrophoneMuted, value);
            _translationService.SetMicrophoneMute(value);
            AddLog(value ? "🎙 Microphone MUTED" : "🎙 Microphone UNMUTED");
        }
    }

    public bool IsSpeakerMuted
    {
        get => _isSpeakerMuted;
        set
        {
            this.RaiseAndSetIfChanged(ref _isSpeakerMuted, value);
            _translationService.SetSpeakerMute(value);
            AddLog(value ? "🔇 Speaker MUTED" : "🔊 Speaker UNMUTED");
        }
    }

    public bool IsTransitioning
    {
        get => _isTransitioning;
        set => this.RaiseAndSetIfChanged(ref _isTransitioning, value);
    }

    #endregion

    #region Properties - Session Management

    public string SessionInfo
    {
        get => _sessionInfo;
        set => this.RaiseAndSetIfChanged(ref _sessionInfo, value);
    }

    public string SessionTimeRemaining
    {
        get => _sessionTimeRemaining;
        set => this.RaiseAndSetIfChanged(ref _sessionTimeRemaining, value);
    }

    public bool ShowSessionInfo
    {
        get => _showSessionInfo;
        set => this.RaiseAndSetIfChanged(ref _showSessionInfo, value);
    }

    public bool IsSessionExpiring
    {
        get => _isSessionExpiring;
        set => this.RaiseAndSetIfChanged(ref _isSessionExpiring, value);
    }

    #endregion

    #region Properties - Computed

    public string TranslationButtonText => _isRunning ? "Stop" : "Start";
    public string StatusText => _isRunning ? "🎙 Active" : "Ready";

    #endregion

    #region Commands

    public ICommand ToggleSettingsPanelCommand { get; private set; } = null!;
    public ICommand ToggleTranslationCommand { get; private set; } = null!;
    public ICommand StartCommand { get; private set; } = null!;
    public ICommand StopCommand { get; private set; } = null!;
    public ICommand RefreshDevicesCommand { get; private set; } = null!;
    public ICommand ShowConsoleViewCommand { get; private set; } = null!;
    public ICommand ShowMessageViewCommand { get; private set; } = null!;
    public ICommand ClearMessage { get; private set; } = null!;
    public ICommand LogoutCommand { get; private set; } = null!;

    #endregion

    #region Initialization Methods

    private void OnMeetingAudioActivityChanged(object? sender, bool isActive)
    {
        Dispatcher.UIThread.Post(() => IsMeetingAudioActive = isActive);
    }
    
    private void InitializeCommands()
    {
        StartCommand = ReactiveCommand.CreateFromTask(
            StartTranslation,
            this.WhenAnyValue(x => x.IsRunning, running => !running));

        StopCommand = ReactiveCommand.CreateFromTask(
            StopTranslation,
            this.WhenAnyValue(x => x.IsRunning));

        RefreshDevicesCommand = ReactiveCommand.Create(RefreshDevices);

        ToggleSettingsPanelCommand = ReactiveCommand.Create(
            () => IsSettingsPanelVisible = !IsSettingsPanelVisible);

        ToggleTranslationCommand = ReactiveCommand.Create(() =>
        {
            if (!IsRunning)
            {
                _ = StartTranslation();
            }
            else
            {
                _ = StopTranslation();
            }
        });

        ShowConsoleViewCommand = ReactiveCommand.Create(() => IsMessageViewActive = false);
        ShowMessageViewCommand = ReactiveCommand.Create(() => IsMessageViewActive = true);
        ClearMessage = ReactiveCommand.Create(ClearLogsAndMessages);
        LogoutCommand = ReactiveCommand.CreateFromTask(LogoutAsync);
    }

    private void InitializeCollections()
    {
        // Collections are now initialized inline with property declarations
    }

    private void InitializeDeviceRefreshTimer()
    {
        _deviceRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _deviceRefreshTimer.Tick += (_, __) => RefreshDevices();
        _deviceRefreshTimer.Start();
    }

    private void InitializeSessionMonitoring()
    {
        var sessionManager = _clientService.GetSessionManager();
        
        if (sessionManager.HasActiveSession())
        {
            UpdateSessionInfo();
            
            sessionManager.SessionExpired += OnSessionExpired;
            sessionManager.SessionExpiring += OnSessionExpiring;
            sessionManager.HeartbeatFailed += OnHeartbeatFailed;
            
            _sessionExpiryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _sessionExpiryTimer.Tick += (_, __) => UpdateSessionInfo();
            _sessionExpiryTimer.Start();
        }
    }

    private void InitializeReactiveBindings()
    {
        this.WhenAnyValue(x => x.IsRunning)
            .Subscribe(isRunning =>
            {
                this.RaisePropertyChanged(nameof(TranslationButtonText));
                this.RaisePropertyChanged(nameof(StatusText));
            });
    }

    private void SetupDefaultLanguageSelection()
    {
        this.WhenAnyValue(x => x.Languages.Count)
            .Where(count => count > 0)
            .Take(1)
            .Subscribe(_ =>
            {
                SelectedSourceLanguage = Languages.FirstOrDefault(l => l.Code == SourceLanguage);
                SelectedTargetLanguage = Languages.FirstOrDefault(l => l.Code == TargetLanguage);
            });
    }

    private void DisplayWelcomeMessage()
    {
        var (recommendedMic, recommendedSpeaker) = _deviceService.GetRecommendedMeetingDevices();
        var actualIncoming = _deviceService.FindIncomingCableDevice();
        var actualOutgoing = _deviceService.FindOutgoingCableDevice();
        var physicalMic = _deviceService.FindPhysicalMicrophone();
        var physicalSpeaker = _deviceService.FindPhysicalSpeaker();

        AddLog("╔══════════════════════════════════════════════════════════════╗");
        AddLog("║              VAANI - Real-Time Translation App               ║");
        AddLog("╚══════════════════════════════════════════════════════════════╝");
        AddLog("");
        AddLog("📋 DETECTED AUDIO CONFIGURATION:");
        AddLog("");

        AddLog("🎧 YOUR DEVICES (Physical):");
        if (physicalMic != null)
            AddLog($"   ✓ Microphone: {physicalMic.FriendlyName}");
        else
            AddLog("   ⚠️ Microphone: Not detected!");

        if (physicalSpeaker != null)
            AddLog($"   ✓ Speaker: {physicalSpeaker.FriendlyName}");
        else
            AddLog("   ⚠️ Speaker: Not detected!");

        AddLog("");

        AddLog("🔌 VIRTUAL CABLE DEVICES:");
        if (actualOutgoing != null)
            AddLog($"   ✓ Outgoing (Your voice → Meeting): {actualOutgoing.FriendlyName}");
        else
            AddLog("   ❌ Outgoing cable: NOT FOUND!");

        if (actualIncoming != null)
            AddLog($"   ✓ Incoming (Meeting audio capture): {actualIncoming.FriendlyName}");
        else
            AddLog("   ❌ Incoming cable: NOT FOUND!");

        AddLog("");
        AddLog("═══════════════════════════════════════════════════════════════");
        AddLog("⚙️  MEETING APP SETUP REQUIRED:");
        AddLog("═══════════════════════════════════════════════════════════════");
        AddLog("");

        if (actualOutgoing != null && actualIncoming != null)
        {
            AddLog("Configure your meeting app (Teams/Meet/Zoom) with:");
            AddLog("");
            AddLog($"🎤 Microphone → {recommendedMic}");
            AddLog($"🔊 Speaker    → {recommendedSpeaker}");
            AddLog("");
            AddLog("✅ All devices detected! Ready to start.");
        }
        else
        {
            AddLog("❌ CABLE DEVICES MISSING!");
            AddLog("");
            AddLog("Please install VB-Audio Virtual Cable:");
            AddLog("   • Standard VB-Cable, OR");
            AddLog("   • VB-Cable A+B (recommended for best separation)");
            AddLog("");
            AddLog("Download from: https://vb-audio.com/Cable/");
            AddLog("");
            AddLog("After installation:");
            AddLog("   1. Restart this app");
            AddLog("   2. Set meeting Microphone → CABLE Output");
            AddLog("   3. Set meeting Speaker    → CABLE Input");
        }

        AddLog("");
        AddLog("Click START when ready to begin translation!");
        AddLog("═══════════════════════════════════════════════════════════════");
    }

    #endregion

    #region Translation Methods

    private async Task StartTranslation()
    {       
        IsTransitioning = true;
        try
        {
            LogText = "";
            Messages.Clear();
            _currentOutgoingBubble = null;
            _currentIncomingBubble = null;
            
            IsMicrophoneMuted = false;
            IsSpeakerMuted = false;

            await _translationService.StartTranslationAsync(_settings);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsRunning = false;
                IsTransitioning = false;
            });
        }
    }

    private async Task StopTranslation()
    {
        IsTransitioning = true;
        try
        {
            await _translationService.StopTranslationAsync();
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsRunning = false;
                IsTransitioning = false;
            });
        }
    }

    #endregion

    #region Device Management Methods

    private void RefreshDevices()
    {
        var prevInput = SelectedInputDevice;
        var prevOutput = SelectedOutputDevice;

        // Unsubscribe from old devices
        foreach (var device in InputDevices)
        {
            device.PropertyChanged -= OnInputDevicePropertyChanged;
        }
        foreach (var device in OutputDevices)
        {
            device.PropertyChanged -= OnOutputDevicePropertyChanged;
        }

        InputDevices.Clear();
        OutputDevices.Clear();

        var inputDevices = _deviceService.GetInputDevices()
            .Where(d => d.FriendlyName == null || !d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var outputDevices = _deviceService.GetOutputDevices()
            .Where(d => d.FriendlyName == null || !d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var device in inputDevices)
        {
            device.PropertyChanged += OnInputDevicePropertyChanged;
            InputDevices.Add(device);
        }

        foreach (var device in outputDevices)
        {
            device.PropertyChanged += OnOutputDevicePropertyChanged;
            OutputDevices.Add(device);
        }

        SelectedInputDevice = PickPriorityDevice(InputDevices, prevInput);
        SelectedOutputDevice = PickPriorityDevice(OutputDevices, prevOutput);
    }

    private void OnInputDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioDeviceInfo.IsSelected) && sender is AudioDeviceInfo device && device.IsSelected)
        {
            SelectedInputDevice = device;
        }
    }

    private void OnOutputDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioDeviceInfo.IsSelected) && sender is AudioDeviceInfo device && device.IsSelected)
        {
            SelectedOutputDevice = device;
        }
    }

    private AudioDeviceInfo? PickPriorityDevice(IList<AudioDeviceInfo> devices, AudioDeviceInfo? previous)
    {
        if (previous != null && devices.Any(d => d.Id == previous.Id))
            return devices.First(d => d.Id == previous.Id);

        var headset = devices.FirstOrDefault(d => d.FriendlyName != null &&
            d.FriendlyName.Contains("Headset", StringComparison.OrdinalIgnoreCase));
        if (headset != null) return headset;

        var local = devices.FirstOrDefault(d => d.FriendlyName != null &&
            (d.FriendlyName.Contains("Microphone", StringComparison.OrdinalIgnoreCase) ||
             d.FriendlyName.Contains("Speaker", StringComparison.OrdinalIgnoreCase)));
        if (local != null) return local;

        return devices.FirstOrDefault();
    }

    private void getConnectedMeetingDevices()
    {
        var (mic, speaker) = _deviceService.GetRecommendedMeetingDevices();
        MeetingInputDeviceName = mic;
        MeetingOutputDeviceName = speaker;
    }

    #endregion

    #region Language Management Methods

    public async Task LoadLanguagesAsync(TranslationSettings setting)
    {
        SourceLanguage = setting.SourceLanguage;
        TargetLanguage = setting.TargetLanguage;
        _sourceLanguage = SourceLanguage;
        _targetLanguage = TargetLanguage;
        _targetVoice = TargetVoice;

        var langs = await _clientService.GetLanguagesAsync();
        Languages.Clear();
        foreach (var lang in langs)
            Languages.Add(lang);

        var genders = _clientService.GetGenderOptions();
        Genders.Clear();
        foreach (var gender in genders)
            Genders.Add(gender);

        SelectedSourceLanguage = Languages.FirstOrDefault(l => l.Code == SourceLanguage);
        SelectedTargetLanguage = Languages.FirstOrDefault(l => l.Code == TargetLanguage);
        SelectedGender = Genders.FirstOrDefault(g => g.Value == "Male");
        
        _sourceLanguage = GetLanguageCode(_sourceLanguage);
        _targetLanguage = GetLanguageCode(_targetLanguage);
    }

    private void UpdateSourceVoice()
    {
        if (SelectedSourceLanguage == null || SelectedGender == null)
            return;

        var voice = _clientService.GetVoiceForLanguageAndGender("en-US", SelectedGender.Value);

        if (voice != null)
        {
            _settings.TargetVoice = voice.Name;
            TargetVoice = voice.Name;
            AddLog($"🎙️ Voice updated: {voice.DisplayName} ({voice.LanguageCode})");
        }
        else
        {
            AddLog($"⚠️ No voice found for {SelectedSourceLanguage.DisplayName} - {SelectedGender.DisplayName}");
        }
    }

    private TranslationSettings LoadTranslationSettings()
    {
        _settings = _clientService.GetTranslationSettings();
        _ = LoadLanguagesAsync(_settings);
        SetupDefaultLanguageSelection();
        return _settings;
    }   

    private string GetLanguageCode(string languageCode)
    {
        return languageCode ?? "en-US";
    }

    #endregion

    #region Event Handlers

    private void OnLogMessage(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => AddLog(message));
    }

    private void OnMessageReceived(object? sender, MessageEventArgs e)
    {
        if (sender == null || string.IsNullOrWhiteSpace(e.Text)) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (e.MessageType == MessageType.Recognizing)
            {
                HandleRecognizingMessage(e);
            }
            else if (e.MessageType == MessageType.Recognized)
            {
                HandleRecognizedMessage(e);
            }
        });
    }

    private void OnTranslationReceived(object? sender, TranslationEventArgs e)
    {
        if (sender == null || string.IsNullOrWhiteSpace(e.TranslatedText)) return;

        Dispatcher.UIThread.Post(() =>
        {
            HandleTranslation(e);
        });
    }

    private void OnSystemMessage(object? sender, SystemMessageEventArgs e)
    {
        if (sender == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (e.MessageType == SystemMessageType.Started)
            {
                IsRunning = true;
                IsTransitioning = false;
            }
            else if (e.MessageType == SystemMessageType.Stopped)
            {
                IsRunning = false;
                IsTransitioning = false;
            }
            
            var bubble = new MessageBubble
            {
                IsFromMeeting = false,
                IsRecognizing = false,
                OriginalText = e.Message,
                TranslatedText = e.Detail,
                IsSystemMessage = true
            };
            Messages.Add(bubble);
            TrimMessages();
        });
    }

    private void OnSynthesizingStatusChanged(object? sender, SynthesizingEventArgs e)
    {
        if (sender == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            var targetBubble = Messages.LastOrDefault(m =>
                m.IsFromMeeting == e.IsFromMeeting &&
                !string.IsNullOrEmpty(m.TranslatedText) &&
                !m.IsSystemMessage &&
                m.TranslatedText.Contains(e.TranslatedText, StringComparison.OrdinalIgnoreCase));

            if (targetBubble != null)
            {
                targetBubble.IsSynthesizing = e.IsSynthesizing;
            }
        });
    }

    private void OnSessionExpired(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddLog("⚠️ Session has expired!");
            
            if (IsRunning)
            {
                _ = StopTranslation();
            }
            
            Toast.Show("Your session has expired. Please log in again.");
            ShowSessionInfo = false;
            SessionInfo = string.Empty;
            SessionTimeRemaining = string.Empty;
        });
    }

    private void OnSessionExpiring(object? sender, TimeSpan remainingTime)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsSessionExpiring = true;
            AddLog($"⚠️ Session expiring in {remainingTime.Minutes} minutes!");
            Toast.Show($"Your session will expire in {remainingTime.Minutes} minutes");
        });
    }

    private void OnHeartbeatFailed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddLog("⚠️ Connection to server lost. Retrying...");
        });
    }

    private void HandleRecognizingMessage(MessageEventArgs e)
    {
        ref MessageBubble? currentBubble = ref (e.IsFromMeeting ? ref _currentIncomingBubble : ref _currentOutgoingBubble);

        if (currentBubble != null && currentBubble.IsRecognizing)
        {
            currentBubble.OriginalText = e.Text;
        }
        else
        {
            currentBubble = new MessageBubble
            {
                IsFromMeeting = e.IsFromMeeting,
                IsRecognizing = true,
                OriginalText = e.Text,
                TranslatedText = string.Empty
            };
            Messages.Add(currentBubble);
            TrimMessages();
        }
    }

    private void HandleRecognizedMessage(MessageEventArgs e)
    {
        ref MessageBubble? currentBubble = ref (e.IsFromMeeting ? ref _currentIncomingBubble : ref _currentOutgoingBubble);

        // Cancel any ongoing animation
        _recognizingAnimationCts?.Cancel();
        _recognizingAnimationCts = new CancellationTokenSource();

        if (currentBubble != null && currentBubble.IsRecognizing)
        {
            // ✅ Morph existing recognizing bubble to finalized state
            currentBubble.IsRecognizing = false;
            
            // ✅ FIXED: Just update the text directly, NO animation
            // The text is already visible from recognizing phase
            currentBubble.OriginalText = e.Text;
        }
        else
        {
            // ✅ No recognizing phase - create new finalized bubble with animation
            currentBubble = new MessageBubble
            {
                IsFromMeeting = e.IsFromMeeting,
                OriginalText = string.Empty,
                TranslatedText = string.Empty,
                IsRecognizing = false
            };
            Messages.Add(currentBubble);
            TrimMessages();

            // ✅ Animate only when there was NO recognizing phase
            var bubbleRef = currentBubble;
            _ = _textAnimator.DisplayProgressivelyAsync(
                e.Text,
                (displayedText, isComplete) =>
                {
                    bubbleRef.OriginalText = displayedText;
                },
                _recognizingAnimationCts.Token
            );
        }
    }

    private void HandleTranslation(TranslationEventArgs e)
    {
        MessageBubble? currentBubble = e.IsFromMeeting ? _currentIncomingBubble : _currentOutgoingBubble;

        _translationAnimationCts?.Cancel();
        _translationAnimationCts = new CancellationTokenSource();

        if (currentBubble != null && string.IsNullOrEmpty(currentBubble.TranslatedText))
        {
            var bubbleRef = currentBubble;
            _ = _textAnimator.DisplayProgressivelyAsync(
                e.TranslatedText,
                (displayedText, isComplete) =>
                {
                    bubbleRef.TranslatedText = displayedText;
                    
                    if (isComplete)
                    {
                        if (e.IsFromMeeting)
                            _currentIncomingBubble = null;
                        else
                            _currentOutgoingBubble = null;
                    }
                },
                _translationAnimationCts.Token
            );
        }
        else
        {
            var targetBubble = Messages.LastOrDefault(m =>
                m.IsFromMeeting == e.IsFromMeeting &&
                string.IsNullOrEmpty(m.TranslatedText) &&
                !m.IsSystemMessage);

            if (targetBubble != null)
            {
                _ = _textAnimator.DisplayProgressivelyAsync(
                    e.TranslatedText,
                    (displayedText, isComplete) =>
                    {
                        targetBubble.TranslatedText = displayedText;
                        
                        if (isComplete)
                        {
                            if (e.IsFromMeeting)
                                _currentIncomingBubble = null;
                            else
                                _currentOutgoingBubble = null;
                        }
                    },
                    _translationAnimationCts.Token
                );
            }
        }
    }

    #endregion

    #region Helper Methods

    private void AddLog(string message)
    {
        LogText += message + Environment.NewLine;
    }
    
    private void TrimMessages()
    {
        while (Messages.Count > MaxMessages)
        {
            Messages.RemoveAt(0);
        }
    }
    
    private void ClearLogsAndMessages()
    {
        LogText = string.Empty;
        Messages.Clear();
        _currentOutgoingBubble = null;
        _currentIncomingBubble = null;
        _currentRecognizingMessage = null;
        AddLog("🗑️ Logs and messages cleared");
    }

    private void UpdateSessionInfo()
    {
        var sessionManager = _clientService.GetSessionManager();

        if (!sessionManager.HasActiveSession() || sessionManager.CurrentSession == null)
        {
            ShowSessionInfo = false;
            return;
        }

        var session = sessionManager.CurrentSession;
        var config = session.Configuration;

        SessionInfo = $"Meeting: {config.MeetingName} ({config.MeetingId})";
        ShowSessionInfo = true;

        var remaining = session.GetRemainingTime();
        if (remaining.TotalMinutes > 60)
        {
            SessionTimeRemaining = $"Expires in: {remaining.Hours}h {remaining.Minutes}m";
        }
        else if (remaining.TotalMinutes > 5)
        {
            SessionTimeRemaining = $"Expires in: {remaining.Minutes} minutes";
        }
        else
        {
            SessionTimeRemaining = $"⚠️ Expires in: {remaining.Minutes}m {remaining.Seconds}s";
            IsSessionExpiring = true;
        }
    }

    private async Task LogoutAsync()
    {
        if (IsRunning)
        {
            await StopTranslation();
        }

        var sessionManager = _clientService.GetSessionManager();
        await sessionManager.LogoutAsync();

        AddLog("🚪 Logged out successfully");
        Toast.Show("Logged out successfully");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var loginWindow = new Authentication.Views.MeetingLoginWindow();
                desktop.MainWindow = loginWindow;
                loginWindow.Show();

                var currentWindow = desktop.Windows?.FirstOrDefault(w => w is Views.MainWindow);
                currentWindow?.Close();
            }
        });
    }

    public void Dispose()
    {
        _deviceRefreshTimer?.Stop();
        _sessionExpiryTimer?.Stop();
        _recognizingAnimationCts?.Cancel();
        _translationAnimationCts?.Cancel();
        _recognizingAnimationCts?.Dispose();
        _translationAnimationCts?.Dispose();
        _translationService?.Dispose();
    }

    #endregion
}
