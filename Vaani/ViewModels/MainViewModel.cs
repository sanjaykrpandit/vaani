using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using Vaani.Authentication.Services;
using Vaani.Common;
using Vaani.Models;
using Vaani.Services;
using System.Linq;
using static Vaani.Common.ErrorMessages;

namespace Vaani.ViewModels;

public class MainViewModel : ViewModelBase
{
    #region Fields
    private readonly DeviceService _deviceService;
    // BackendTranslationService proxies all Azure work to Vaani.API via SignalR
    private readonly BackendTranslationService _backendService;
    private readonly DirectAzureTranslationService _directAzureService;
    private readonly DirectAzureBypassService _directAzureBypassService;
    private ITranslationService? _activeService;
    private readonly MeetingAuthenticationService _sessionService;
    private readonly ClientService _clientService = new();
    private readonly AnimatedTextDisplay _textAnimator = new() { WordDelayMs = 120 };
    private DispatcherTimer _deviceRefreshTimer = null!;
    private DispatcherTimer? _sessionExpiryTimer;
    private TranslationSettings _settings = new();
    private bool _useDirectAzure;

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
    private string? _targetVoice;

    private string _meetingInputDevice = "";
    private string _meetingOutputDevice = "";
    private bool _isSettingsPanelVisible = true;
    private AudioDeviceInfo? _selectedInputDevice;
    private AudioDeviceInfo? _selectedOutputDevice;

    private bool _isMessageViewActive = true;
    private MessageBubble? _currentRecognizingMessage;
    private MessageBubble? _currentOutgoingBubble;
    private MessageBubble? _currentIncomingBubble;

    // Tracks which bubble is currently synthesizing per direction so that
    // SynthesizingCompleted can clear it by reference instead of fragile text matching.
    private MessageBubble? _outgoingSynthesizingBubble;
    private MessageBubble? _incomingSynthesizingBubble;
    private const int MaxMessages = 100;
    private string _logText = "";
    // Keep logs bounded to prevent UI/memory growth in long sessions.
    private const int MaxLogLinesInUi = 400;
    private readonly Queue<string> _logLines = new();
    private readonly object _logLock = new();
    private bool _isMicrophoneMuted = false;
    private bool _isSpeakerMuted = false;
    private bool _isTransitioning = false;
    private bool _isSynthesizing = false;
    private bool _isMeetingAudioActive = false;
    private bool _isUserGuideEnabled = true;
    private bool _isBypassModeEnabled = false;
    private int _stopTranslationInProgress;
    private int _startTranslationInProgress;

    // De-dupe guards for occasional repeated backend/direct events.
    private string _lastRecognizedText = string.Empty;
    private bool _lastRecognizedIsFromMeeting;
    private DateTime _lastRecognizedAtUtc = DateTime.MinValue;
    private string _lastTranslationOriginal = string.Empty;
    private string _lastTranslationText = string.Empty;
    private bool _lastTranslationIsFromMeeting;
    private DateTime _lastTranslationAtUtc = DateTime.MinValue;


    // Animation cancellation tokens
    private CancellationTokenSource? _recognizingAnimationCts;

    // Add this private field in the Fields region (near other private fields)
    private bool _inputDevicesLoaded = false;
    // Add this field near other private fields (Fields region)
    private CancellationTokenSource? _deviceRefreshCts = null;
    private bool _isRefreshingDevices;
    private bool _autoSelectInputDevice = true;
    private bool _autoSelectOutputDevice = true;

    #endregion

    #region Constructor

    public MainViewModel()
    {
        _settings = LoadTranslationSettings();
        // Reuse singleton to leverage pre-warmed/shared device cache
        _deviceService = DeviceService.Instance;
        _backendService = new BackendTranslationService();
        _directAzureService = new DirectAzureTranslationService();
        _directAzureBypassService = new DirectAzureBypassService();
        _sessionService = new MeetingAuthenticationService();

        // Read default connection mode from config
        var configMode = Authentication.Services.ConfigurationService.Instance.Config.Realtime.DefaultConnectionMode;
        _useDirectAzure = string.Equals(configMode, "DirectAzure", StringComparison.OrdinalIgnoreCase);

        _activeService = _useDirectAzure ? _directAzureService : _backendService;
        WireServiceEvents(_activeService);

        InitializeCommands();
        InitializeCollections();
        InitializeDeviceRefreshTimer();
        InitializeSessionMonitoring();
        InitializeReactiveBindings();

        // Perform slow / I/O-bound startup tasks asynchronously so the UI can render quickly.
        _ = InitializeAsync();
    }

    #endregion

    #region Properties - Collections

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<LanguageInfo> Languages { get; } = new();
    public ObservableCollection<GenderOption> Genders { get; } = new();
    public ObservableCollection<MessageBubble> Messages { get; } = new();

    public ObservableCollection<MessageBubble> MessageHistory { get; } = new();

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
                UpdateVoices();
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
                UpdateVoices();
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
                UpdateVoices();
                AddLog($"🎭 Gender changed to: {value.DisplayName}");
            }
        }
    }

    #endregion

    #region Properties - State

    public bool IsUserGuideEnabled
    {
        get => _isUserGuideEnabled;
        set => this.RaiseAndSetIfChanged(ref _isUserGuideEnabled, value);
    }

    private bool _isInputDevicesLoading;
    public bool IsInputDevicesLoading
    {
        get => _isInputDevicesLoading;
        set => this.RaiseAndSetIfChanged(ref _isInputDevicesLoading, value);
    }

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
        set
        {
            if (_isSettingsPanelVisible == value)
                return;

            this.RaiseAndSetIfChanged(ref _isSettingsPanelVisible, value);
            OnSettingsPanelVisibilityChanged(value);
        }
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
        get => _targetVoice ?? string.Empty;
        set
        {
            this.RaiseAndSetIfChanged(ref _targetVoice, value);
        }
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
            _activeService?.SetMicrophoneMute(value);
            AddLog(value ? "🎙 Microphone MUTED" : "🎙 Microphone UNMUTED");
        }
    }

    public bool IsSpeakerMuted
    {
        get => _isSpeakerMuted;
        set
        {
            this.RaiseAndSetIfChanged(ref _isSpeakerMuted, value);
            _activeService?.SetSpeakerMute(value);
            AddLog(value ? "🔇 Speaker MUTED" : "🔊 Speaker UNMUTED");
        }
    }

    public bool IsBypassModeEnabled
    {
        get => _isBypassModeEnabled;
        set => this.RaiseAndSetIfChanged(ref _isBypassModeEnabled, value);
    }

    public bool UseDirectAzure
    {
        get => _useDirectAzure;
        set
        {
            if (_useDirectAzure == value) return;
            _useDirectAzure = value;
            this.RaisePropertyChanged();
            AddLog(_useDirectAzure ? "⚡ Mode: Direct Azure (local, low latency)" : "🌐 Mode: Server");
        }
    }

    public bool IsTransitioning
    {
        get => _isTransitioning;
        set => this.RaiseAndSetIfChanged(ref _isTransitioning, value);
    }


    public bool IsSynthesizing
    {
        get => _isSynthesizing;
        private set => this.RaiseAndSetIfChanged(ref _isSynthesizing, value);
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
    public ICommand CloseUserGuideCommand { get; private set; } = null!;

    #endregion

    #region Initialization Methods

    //private void OnMeetingAudioActivityChanged(object? sender, bool isActive)
    //{
    //    Dispatcher.UIThread.Post(() => IsMeetingAudioActive = isActive);
    //}


    private void InitializeCommands()
    {
        StartCommand = ReactiveCommand.CreateFromTask(
            StartTranslation,
            this.WhenAnyValue(x => x.IsRunning, running => !running));

        // ✅ FIXED: Allow stopping anytime (service handles interruption)
        StopCommand = ReactiveCommand.CreateFromTask(
            StopTranslation,
            this.WhenAnyValue(x => x.IsRunning));

        // RefreshDevices is now async Task, wire with CreateFromTask
        RefreshDevicesCommand = ReactiveCommand.CreateFromTask(RefreshDevices);

        ToggleSettingsPanelCommand = ReactiveCommand.Create(
            () => IsSettingsPanelVisible = !IsSettingsPanelVisible);

        ToggleTranslationCommand = ReactiveCommand.Create(() =>
        {
            if (IsTransitioning)
            {
                AddLog("⏳ Please wait, previous start/stop is still processing...");
                return;
            }

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
        CloseUserGuideCommand = ReactiveCommand.Create(() => IsUserGuideEnabled = false);
    }

    private void InitializeCollections()
    {
        // Collections are now initialized inline with property declarations
    }

    private void InitializeDeviceRefreshTimer()
    {
        _deviceRefreshTimer = new DispatcherTimer
        {
            // Slower poll to reduce CPU churn while settings panel stays open
            Interval = TimeSpan.FromSeconds(5)
        };

        // Tick only triggers a refresh when the settings panel is visible.
        _deviceRefreshTimer.Tick += async (_, __) =>
        {
            // Guard - only refresh when the panel is visible
            if (IsSettingsPanelVisible)
            {
                await RefreshDevices();
            }
        };

        // Do NOT start here. Timer is started/stopped when settings panel visibility changes.
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
                Interval = TimeSpan.FromSeconds(60)
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

        // ✅ Auto-enable/disable bypass mode based on language matching
        this.WhenAnyValue(
            x => x.SelectedSourceLanguage,
            x => x.SelectedTargetLanguage,
            (source, target) => new { Source = source, Target = target })
            .Subscribe(langs =>
            {
                if (langs.Source == null || langs.Target == null) return;

                var languagesMatch = IsBypassEligible(langs.Source.Code, langs.Target.Code);

                if (languagesMatch && !IsBypassModeEnabled)
                {
                    // Auto-enable bypass when same language
                    IsBypassModeEnabled = true;
                    AddLog("⚡ Auto-enabled bypass mode: same source and target language detected");
                    //Toast.Show("Bypass mode enabled - same language selected");
                }
                else if (!languagesMatch && IsBypassModeEnabled)
                {
                    // Auto-disable bypass when different languages
                    IsBypassModeEnabled = false;
                    AddLog("🔄 Auto-disabled bypass mode: different languages selected");
                    //Toast.Show("Bypass mode disabled - translation needed");
                }
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

    // New async initializer that runs slow startup tasks off the UI thread.
    private async Task InitializeAsync()
    {
        // Give a chance for the UI to render
        await Task.Yield();

        try
        {
            await RefreshDevices();
            await getConnectedMeetingDevices();
            await DisplayWelcomeMessage();
        }
        catch (Exception ex)
        {
            // Log but don't crash the UI during startup
            AddLog($"⚠️ Startup initialization error: {Classify(ex)}");
        }
    }

    // Converted DisplayWelcomeMessage to async so device lookups run off UI thread
    private async Task DisplayWelcomeMessage()
    {
        var (recommendedMic, recommendedSpeaker) = await Task.Run(() => _deviceService.GetRecommendedMeetingDevices());
        var actualIncoming = await Task.Run(() => _deviceService.FindIncomingCableDevice());
        var actualOutgoing = await Task.Run(() => _deviceService.FindOutgoingCableDevice());
        var physicalMic = await Task.Run(() => _deviceService.FindPhysicalMicrophone());
        var physicalSpeaker = await Task.Run(() => _deviceService.FindPhysicalSpeaker());

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
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
        });
    }
    private void OnSettingsPanelVisibilityChanged(bool isVisible)
    {
        if (isVisible)
        {
            // Start periodic refresh and run one immediate refresh in background
            _deviceRefreshTimer?.Start();

            // Fire-and-forget initial refresh (safely).
            _ = RefreshDevices();
        }
        else
        {
            // Stop periodic refresh to reduce work when settings not visible
            _deviceRefreshTimer?.Stop();
        }
    }
    #endregion

    #region Translation Methods

    private async Task StartTranslation()
    {
        if (Interlocked.Exchange(ref _startTranslationInProgress, 1) == 1)
            return;

        if (Interlocked.CompareExchange(ref _stopTranslationInProgress, 0, 0) == 1)
        {
            AddLog("⏳ Stop is still in progress. Please wait before starting again.");
            Interlocked.Exchange(ref _startTranslationInProgress, 0);
            return;
        }

        IsSettingsPanelVisible = false;
        IsTransitioning = true;
        IsMicrophoneMuted = false;
        IsSpeakerMuted = false;
        try
        {
            LogText = "";
            Messages.Clear();
            _currentOutgoingBubble = null;
            _currentIncomingBubble = null;
            _lastRecognizedText = string.Empty;
            _lastRecognizedAtUtc = DateTime.MinValue;
            _lastTranslationOriginal = string.Empty;
            _lastTranslationText = string.Empty;
            _lastTranslationAtUtc = DateTime.MinValue;

            (bool hasStarted, string message, int? startedSessionId) = await _sessionService.StartSessionAsync();
            if (hasStarted)
            {
                if (startedSessionId.HasValue)
                {
                    _settings.SessionId = startedSessionId.Value.ToString();
                }

                var runtimeSettings = BuildRuntimeSettings();
                var bypassFromSelection = IsBypassEligible(SelectedSourceLanguage?.Code, SelectedTargetLanguage?.Code);
                runtimeSettings.IsBypassMode = bypassFromSelection || IsBypassEligible(runtimeSettings.SourceLanguage, runtimeSettings.TargetLanguage);

                // Pick service based on selected connection mode
                var targetService = !_useDirectAzure
                    ? (ITranslationService)_backendService
                    : runtimeSettings.IsBypassMode
                        ? _directAzureBypassService
                        : _directAzureService;
                if (_activeService != targetService)
                {
                    UnwireServiceEvents(_activeService);
                    _activeService = targetService;
                    WireServiceEvents(_activeService);
                }

                await _activeService.StartTranslationAsync(runtimeSettings);
            }
            else
            {
                AddLog("Failed to start session. Please check your credentials.");
                Toast.Show(message);
                return;
            }

        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Do NOT set IsRunning here — it is driven exclusively by OnSystemMessage
                // (Started → true, Stopped → true). Setting it false here races with and
                // overwrites the SessionStarted callback on every subsequent start.
                IsTransitioning = false;
            });

            Interlocked.Exchange(ref _startTranslationInProgress, 0);
        }
    }

    private async Task StopTranslation()
    {
        if (Interlocked.Exchange(ref _stopTranslationInProgress, 1) == 1)
        {
            AddLog("⏳ Stop already in progress...");
            return;
        }

        var stopConfirmed = false;

        IsTransitioning = true;
        IsRunning = false;
        IsMicrophoneMuted = true;
        IsSpeakerMuted = true;

        try
        {
            // Detach event stream early so no additional UI updates are queued while stopping.
            UnwireServiceEvents(_activeService);

            // Cancel animations immediately
            _recognizingAnimationCts?.Cancel();
            _recognizingAnimationCts?.Dispose();
            _recognizingAnimationCts = null;
            foreach (var bubble in Messages)
            {
                bubble.TranslationAnimationCts?.Cancel();
                bubble.TranslationAnimationCts?.Dispose();
                bubble.TranslationAnimationCts = null;
            }

            _currentOutgoingBubble = null;
            _currentIncomingBubble = null;
            _currentRecognizingMessage = null;
            _lastRecognizedText = string.Empty;
            _lastRecognizedAtUtc = DateTime.MinValue;
            _lastTranslationOriginal = string.Empty;
            _lastTranslationText = string.Empty;
            _lastTranslationAtUtc = DateTime.MinValue;

            // Stop the service — it stops recognizers first, waits for in-flight synthesis, then disposes
            AddLog("🛑 Stopping translation (waiting for current audio to finish)...");
            if (_activeService != null)
            {
                var service = _activeService;
                var stopTask = Task.Run(async () => await service.StopTranslationAsync());
                var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(8)));
                if (completed == stopTask)
                {
                    await stopTask;
                    stopConfirmed = true;
                    AddLog("✅ Translation service stopped");
                }
                else
                {
                    AddLog("⚠️ Translation stop timed out. Continuing shutdown to keep app responsive.");
                }
            }

            // Force-clear any synthesis indicators left over
            IsSynthesizing = false;
            foreach (var bubble in Messages.Where(m => m.IsSynthesizing))
                bubble.IsSynthesizing = false;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error during stop: {Classify(ex)}");
        }
        finally
        {
            try
            {
                AddLog("🔌 Ending session...");
                var sessionLog = BuildSessionLog();
                var sessionTranscript = BuildSessionTranscript();
                var endSessionTask = Task.Run(async () =>
                    await _sessionService.EndSessionAsync(sessionLog, sessionTranscript));
                var endCompleted = await Task.WhenAny(endSessionTask, Task.Delay(TimeSpan.FromSeconds(5)));
                if (endCompleted == endSessionTask)
                {
                    bool status = await endSessionTask;
                    AddLog(status ? "✅ Session ended successfully" : "⚠️ Session end failed");
                }
                else
                {
                    AddLog("⚠️ Session end timed out. It may complete in background.");
                }
            }
            catch (Exception ex)
            {
                AddLog($"⚠️ Error ending session: {Classify(ex)}");
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsTransitioning = false;

                var bubble = new MessageBubble
                {
                    IsFromMeeting = false,
                    IsRecognizing = false,
                    OriginalText = "Translation Stopped",
                    TranslatedText = string.Empty,
                    IsSystemMessage = true
                };
                Messages.Add(bubble);
                TrimMessages();
            });

            // Re-wire events for next start cycle.
            WireServiceEvents(_activeService);

            Interlocked.Exchange(ref _stopTranslationInProgress, 0);
        }
    }



    // Helper to serialize logs into a reasonable payload
    private string BuildSessionLog()
    {
        // Keep last N log lines to avoid overly large payloads
        const int maxLogLines = 1000;
        var lines = (LogText ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > maxLogLines)
            lines = lines.Skip(lines.Length - maxLogLines).ToArray();

        return string.Join("\n", lines);
    }

    // Helper to build a simple transcript from messages
    private string BuildSessionTranscript()
    {
        // Export all message bubbles in chronological order
        var transcriptLines = Messages.Select(m =>
        {
            var time = m.Timestamp.ToString("o");
            var who = m.IsSystemMessage ? "SYSTEM" : (m.IsFromMeeting ? "MEETING" : "YOU");
            var original = string.IsNullOrEmpty(m.OriginalText) ? string.Empty : $"ORIG: {m.OriginalText}";
            var translated = string.IsNullOrEmpty(m.TranslatedText) ? string.Empty : $"TRANS: {m.TranslatedText}";
            return $"[{time}] {who} {original} {translated}".Trim();
        }).ToList();

        // Keep last N lines to limit size
        const int maxLines = 5000;
        if (transcriptLines.Count > maxLines)
            transcriptLines = transcriptLines.Skip(transcriptLines.Count - maxLines).ToList();

        return string.Join("\n", transcriptLines);
    }

    #endregion

    #region Device Management Methods

    // Replace the existing RefreshDevices method with this incremental, cancellable implementation.
    private async Task RefreshDevices()
    {
        // Always refresh device cache before UI refresh so plug/unplug is reflected quickly.
        _deviceService.RefreshDeviceCache();

        var prevInput = SelectedInputDevice;
        var prevOutput = SelectedOutputDevice;

        // Cancel + dispose previous refresh CTS before creating a new one
        if (_deviceRefreshCts != null)
        {
            try { _deviceRefreshCts.Cancel(); } catch { }
            _deviceRefreshCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _deviceRefreshCts = cts;
        var token = cts.Token;

        // Show loading indicator only on the very first load
        if (!_inputDevicesLoaded)
            IsInputDevicesLoading = true;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        List<AudioDeviceInfo> inputDevices;
        List<AudioDeviceInfo> outputDevices;



        try
        {

            //var (allInputs, allOutputs) = await Task.Run(() => _deviceService.GetAllDevices(), token);
            //inputDevices = allInputs
            //    .Where(d => d.FriendlyName == null || !d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
            //    .ToList();
            //outputDevices = allOutputs
            //    .Where(d => d.FriendlyName == null || !d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
            //    .ToList();


            inputDevices = await Task.Run(() =>
                _deviceService.GetInputDevices()
                    .Where(d => d.FriendlyName == null || !d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
                    .ToList(), token);

            outputDevices = await Task.Run(() =>
                _deviceService.GetOutputDevices()
                    .Where(d => d.FriendlyName == null || !d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
                    .ToList(), token);


        }
        catch (OperationCanceledException)
        {
            // refresh was cancelled; leave UI state as-is
            return;
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ Failed to enumerate devices: {ex.Message}");
            if (!_inputDevicesLoaded)
                IsInputDevicesLoading = false;
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isRefreshingDevices = true;

            // --- INPUT DEVICES: diff + update in-place to avoid UI churn ---

            var existingInputById = InputDevices.ToDictionary(d => d.Id);
            var newInputById = inputDevices.ToDictionary(d => d.Id);

            // remove devices that disappeared
            foreach (var id in existingInputById.Keys.Except(newInputById.Keys).ToList())
            {
                var dev = existingInputById[id];
                dev.PropertyChanged -= OnInputDevicePropertyChanged;
                InputDevices.Remove(dev);
            }

            // If selected input disappeared, clear stale reference so fallback selection can run.
            if (SelectedInputDevice != null && !newInputById.ContainsKey(SelectedInputDevice.Id))
            {
                SelectedInputDevice = null;
                _autoSelectInputDevice = true;
            }

            // update properties of existing devices (preserve object references so bindings/selection remain)
            foreach (var id in existingInputById.Keys.Intersect(newInputById.Keys))
            {
                var oldDev = existingInputById[id];
                var newDev = newInputById[id];
                // update mutable properties that matter for the UI
                oldDev.FriendlyName = newDev.FriendlyName;
                oldDev.IsDefault = newDev.IsDefault;
                oldDev.IsCableDevice = newDev.IsCableDevice;
            }

            // add newly found devices
            foreach (var id in newInputById.Keys.Except(existingInputById.Keys))
            {
                var dev = newInputById[id];
                dev.PropertyChanged += OnInputDevicePropertyChanged;
                InputDevices.Add(dev);
            }

            // --- OUTPUT DEVICES: same diff approach ---

            var existingOutputById = OutputDevices.ToDictionary(d => d.Id);
            var newOutputById = outputDevices.ToDictionary(d => d.Id);

            foreach (var id in existingOutputById.Keys.Except(newOutputById.Keys).ToList())
            {
                var dev = existingOutputById[id];
                dev.PropertyChanged -= OnOutputDevicePropertyChanged;
                OutputDevices.Remove(dev);
            }

            // If selected output disappeared, clear stale reference so fallback selection can run.
            if (SelectedOutputDevice != null && !newOutputById.ContainsKey(SelectedOutputDevice.Id))
            {
                SelectedOutputDevice = null;
                _autoSelectOutputDevice = true;
            }

            foreach (var id in existingOutputById.Keys.Intersect(newOutputById.Keys))
            {
                var oldDev = existingOutputById[id];
                var newDev = newOutputById[id];
                oldDev.FriendlyName = newDev.FriendlyName;
                oldDev.IsDefault = newDev.IsDefault;
                oldDev.IsCableDevice = newDev.IsCableDevice;
            }

            foreach (var id in newOutputById.Keys.Except(existingOutputById.Keys))
            {
                var dev = newOutputById[id];
                dev.PropertyChanged += OnOutputDevicePropertyChanged;
                OutputDevices.Add(dev);
            }

            // --- Restore selection by Id (stable) or fall back to priority pick ---

            if (prevInput != null && !_autoSelectInputDevice)
            {
                var restored = InputDevices.FirstOrDefault(d => d.Id == prevInput.Id);
                if (restored != null)
                {
                    restored.IsSelected = true;
                    SelectedInputDevice = restored;
                }
            }
            if (SelectedInputDevice == null)
            {
                SelectedInputDevice = InputDevices.FirstOrDefault(d => d.IsDefault && !d.IsCableDevice)
                    ?? PickPriorityDevice(InputDevices, prevInput);
            }

            if (prevOutput != null && !_autoSelectOutputDevice)
            {
                var restored = OutputDevices.FirstOrDefault(d => d.Id == prevOutput.Id);
                if (restored != null)
                {
                    restored.IsSelected = true;
                    SelectedOutputDevice = restored;
                }
            }
            if (SelectedOutputDevice == null)
            {
                SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.IsDefault && !d.IsCableDevice)
                    ?? PickPriorityDevice(OutputDevices, prevOutput);
            }

            // Clear initial loading flag after first successful refresh
            if (!_inputDevicesLoaded)
            {
                _inputDevicesLoaded = true;
                IsInputDevicesLoading = false;
            }

            _isRefreshingDevices = false;
        });

        sw.Stop();
        AddLog($"🔄 Devices refreshed in {sw.ElapsedMilliseconds}ms");

        // Keep meeting setup labels in sync with latest topology changes.
        await getConnectedMeetingDevices();
    }

    private void OnInputDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioDeviceInfo.IsSelected) && sender is AudioDeviceInfo device && device.IsSelected)
        {
            if (!_isRefreshingDevices)
                _autoSelectInputDevice = false;
            SelectedInputDevice = device;
        }
    }

    private void OnOutputDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioDeviceInfo.IsSelected) && sender is AudioDeviceInfo device && device.IsSelected)
        {
            if (!_isRefreshingDevices)
                _autoSelectOutputDevice = false;
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

    // getConnectedMeetingDevices converted to async to avoid blocking UI during lookup
    private async Task getConnectedMeetingDevices()
    {
        var (mic, speaker) = await Task.Run(() => _deviceService.GetRecommendedMeetingDevices());
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            MeetingInputDeviceName = mic;
            MeetingOutputDeviceName = speaker;
        });
    }

    #endregion

    #region Language Management Methods

    public Task LoadLanguagesAsync(TranslationSettings setting)
    {
        SourceLanguage = setting.SourceLanguage;
        TargetLanguage = setting.TargetLanguage;
        _sourceLanguage = SourceLanguage;
        _targetLanguage = TargetLanguage;
        _targetVoice = TargetVoice;

        var langs = _clientService.GetLanguagesAsync();
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
        return Task.CompletedTask;
    }

    private void UpdateVoices()
    {
        if (SelectedGender == null)
            return;

        if (SelectedSourceLanguage != null)
        {
            var sourceVoice = _clientService.GetVoiceForLanguageAndGender(_settings.SourceLanguage, "Male");
            if (sourceVoice != null)
            {
                _settings.SourceVoice = sourceVoice.Name;
                AddLog($"🎙️ Incoming voice updated: {sourceVoice.DisplayName} ({sourceVoice.LanguageCode})");
            }
            else
            {
                AddLog($"⚠️ No incoming voice found for {SelectedSourceLanguage.DisplayName} - {SelectedGender.DisplayName}");
            }
        }

        if (SelectedTargetLanguage != null)
        {
            var targetVoice = _clientService.GetVoiceForLanguageAndGender(_settings.TargetLanguage, SelectedGender.Value);
            if (targetVoice != null)
            {
                _settings.TargetVoice = targetVoice.Name;
                TargetVoice = targetVoice.Name;
                AddLog($"🎙️ Outgoing voice updated: {targetVoice.DisplayName} ({targetVoice.LanguageCode})");
            }
            else
            {
                AddLog($"⚠️ No outgoing voice found for {SelectedTargetLanguage.DisplayName} - {SelectedGender.DisplayName}");
            }
        }
    }

    private TranslationSettings LoadTranslationSettings()
    {
        _settings = _clientService.GetTranslationSettings();
        LoadLanguagesAsync(_settings);
        SetupDefaultLanguageSelection();
        return _settings;
    }

    private string GetLanguageCode(string languageCode)
    {
        return languageCode ?? "en-US";
    }

    private TranslationSettings BuildRuntimeSettings()
    {
        if (SelectedSourceLanguage != null)
            _settings.SourceLanguage = SelectedSourceLanguage.Code;
        else if (!string.IsNullOrWhiteSpace(SourceLanguage))
            _settings.SourceLanguage = SourceLanguage;

        if (SelectedTargetLanguage != null)
            _settings.TargetLanguage = SelectedTargetLanguage.Code;
        else if (!string.IsNullOrWhiteSpace(TargetLanguage))
            _settings.TargetLanguage = TargetLanguage;

        SourceLanguage = _settings.SourceLanguage;
        TargetLanguage = _settings.TargetLanguage;

        // Enforce bypass strictly for same language only.
        var bypassAllowed = IsBypassEligible(_settings.SourceLanguage, _settings.TargetLanguage);
        _settings.IsBypassMode = bypassAllowed;
        IsBypassModeEnabled = bypassAllowed;
        _settings.UseDirectAzure = _useDirectAzure;

        UpdateVoices();

        AddLog($"🌐 Using languages: {_settings.SourceLanguage} → {_settings.TargetLanguage}");
        AddLog(_settings.IsBypassMode
            ? "⚡ Bypass mode active (same source and target language)"
            : "🔄 Translation mode active (different source and target language)");

        return new TranslationSettings
        {
            AzureSubscriptionKey = _settings.AzureSubscriptionKey,
            AzureRegion = _settings.AzureRegion,
            BackendTranslationHubUrl = _settings.BackendTranslationHubUrl,
            UseBackendTranslation = _settings.UseBackendTranslation,
            UseDirectAzure = _settings.UseDirectAzure,
            IsBypassMode = _settings.IsBypassMode,
            SourceLanguage = _settings.SourceLanguage,
            TargetLanguage = _settings.TargetLanguage,
            SourceVoice = _settings.SourceVoice,
            TargetVoice = _settings.TargetVoice,
            MeetingId = _settings.MeetingId,
            SessionId = _settings.SessionId,
            MeetingName = _settings.MeetingName,
            SessionToken = _settings.SessionToken,
            SessionExpiresAt = _settings.SessionExpiresAt,
            IsFromMeetingSession = _settings.IsFromMeetingSession
        };
    }

    private static bool IsBypassEligible(string? sourceLanguageCode, string? targetLanguageCode)
    {
        if (string.IsNullOrWhiteSpace(sourceLanguageCode) || string.IsNullOrWhiteSpace(targetLanguageCode))
            return false;

        return string.Equals(sourceLanguageCode, targetLanguageCode, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Event Handlers

    private void OnLogMessage(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => AddLog(message));
    }

    private void OnMessageReceived(object? sender, MessageEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _stopTranslationInProgress, 0, 0) == 1)
            return;

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
        if (Interlocked.CompareExchange(ref _stopTranslationInProgress, 0, 0) == 1)
            return;

        if (sender == null || string.IsNullOrWhiteSpace(e.TranslatedText)) return;

        Dispatcher.UIThread.Post(() =>
        {
            HandleTranslation(e);
        });
    }

    private void OnSystemMessage(object? sender, SystemMessageEventArgs e)
    {
        if (sender == null) return;

        // During stop, ignore late non-stop status updates to prevent UI churn/hangs.
        if (Interlocked.CompareExchange(ref _stopTranslationInProgress, 0, 0) == 1
            && e.MessageType != SystemMessageType.Stopped)
            return;

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

    //private void OnSynthesizingStatusChanged(object? sender, SynthesizingEventArgs e)
    //{
    //    if (sender == null) return;      

    //    Dispatcher.UIThread.Post(() =>
    //    {
    //        IsSynthesizing = e.IsSynthesizing;

    //        var targetBubble = Messages.LastOrDefault(m =>
    //            m.IsFromMeeting == e.IsFromMeeting &&
    //            !string.IsNullOrEmpty(m.TranslatedText) &&
    //            !m.IsSystemMessage &&
    //            m.TranslatedText.Contains(e.TranslatedText, StringComparison.OrdinalIgnoreCase));

    //        if (targetBubble != null)
    //        {
    //            targetBubble.IsSynthesizing = e.IsSynthesizing;
    //        }
    //    });
    //}

    private void OnSynthesizingStatusChanged(object? sender, SynthesizingEventArgs e)
    {
        if (sender == null) return;

        if (Interlocked.CompareExchange(ref _stopTranslationInProgress, 0, 0) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            IsSynthesizing = e.IsSynthesizing;

            ref MessageBubble? trackedRef = ref (e.IsFromMeeting
                ? ref _incomingSynthesizingBubble
                : ref _outgoingSynthesizingBubble);

            if (e.IsSynthesizing)
            {
                // SynthesizingStarted: find the bubble by text and store the reference.
                // Text matching is reliable here because OriginalText is always set before
                // synthesis starts and TranslatedText may still be animating.
                MessageBubble? targetBubble = null;

                // 1) Exact translated-text match.
                if (!string.IsNullOrWhiteSpace(e.TranslatedText))
                {
                    targetBubble = Messages.LastOrDefault(m =>
                        m.IsFromMeeting == e.IsFromMeeting &&
                        !m.IsSystemMessage &&
                        !string.IsNullOrEmpty(m.TranslatedText) &&
                        string.Equals(m.TranslatedText, e.TranslatedText, StringComparison.OrdinalIgnoreCase));

                    // 2) Partial/progressive text match (animation may be mid-way).
                    if (targetBubble == null)
                    {
                        targetBubble = Messages.LastOrDefault(m =>
                            m.IsFromMeeting == e.IsFromMeeting &&
                            !m.IsSystemMessage &&
                            !string.IsNullOrEmpty(m.TranslatedText) &&
                            (m.TranslatedText.Contains(e.TranslatedText, StringComparison.OrdinalIgnoreCase) ||
                             e.TranslatedText.Contains(m.TranslatedText, StringComparison.OrdinalIgnoreCase)));
                    }
                }

                // 3) OriginalText match — reliable because recognition finishes before synthesis.
                if (targetBubble == null && !string.IsNullOrWhiteSpace(e.OriginalText))
                {
                    targetBubble = Messages.LastOrDefault(m =>
                        m.IsFromMeeting == e.IsFromMeeting &&
                        !m.IsSystemMessage &&
                        !string.IsNullOrEmpty(m.OriginalText) &&
                        (string.Equals(m.OriginalText, e.OriginalText, StringComparison.OrdinalIgnoreCase) ||
                         m.OriginalText.Contains(e.OriginalText, StringComparison.OrdinalIgnoreCase) ||
                         e.OriginalText.Contains(m.OriginalText, StringComparison.OrdinalIgnoreCase)));
                }

                // 4) Last-resort fallback: most recent translated bubble for this direction.
                if (targetBubble == null)
                {
                    targetBubble = Messages.LastOrDefault(m =>
                        m.IsFromMeeting == e.IsFromMeeting &&
                        !m.IsSystemMessage &&
                        !string.IsNullOrEmpty(m.TranslatedText));
                }

                if (targetBubble != null)
                {
                    trackedRef = targetBubble;
                    targetBubble.IsSynthesizing = true;
                }
            }
            else
            {
                // SynthesizingCompleted: clear the bubble we stored on Started.
                // This avoids re-running text matching against partially-animated text
                // which is what caused bubbles to get stuck.
                if (trackedRef != null)
                {
                    trackedRef.IsSynthesizing = false;
                    trackedRef = null;
                }
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

    //private void HandleRecognizingMessage(MessageEventArgs e)
    //{
    //    ref MessageBubble? currentBubble = ref (e.IsFromMeeting ? ref _currentIncomingBubble : ref _currentOutgoingBubble);

    //    if (currentBubble != null && currentBubble.IsRecognizing)
    //    {
    //        currentBubble.OriginalText = e.Text;
    //    }
    //    else
    //    {
    //        currentBubble = new MessageBubble
    //        {
    //            IsFromMeeting = e.IsFromMeeting,
    //            IsRecognizing = true,
    //            OriginalText = e.Text,
    //            TranslatedText = string.Empty
    //        };
    //        Messages.Add(currentBubble);
    //        TrimMessages();
    //    }
    //}

    private void HandleRecognizingMessage(MessageEventArgs e)
    {
        // Use the per-direction ref so other handlers still get updated reference
        ref MessageBubble? currentBubble = ref (e.IsFromMeeting ? ref _currentIncomingBubble : ref _currentOutgoingBubble);

        // First try to find an existing recognizing bubble in the Messages collection
        var existingRecognizing = Messages.LastOrDefault(m =>
            m.IsFromMeeting == e.IsFromMeeting &&
            m.IsRecognizing &&
            !m.IsSystemMessage);

        if (existingRecognizing != null)
        {
            // Reuse the bubble already being shown as recognizing
            existingRecognizing.OriginalText = e.Text;
            currentBubble = existingRecognizing;
            return;
        }

        // If the cached currentBubble is a live recognizing bubble, update it
        if (currentBubble != null && currentBubble.IsRecognizing)
        {
            currentBubble.OriginalText = e.Text;
            return;
        }

        // Otherwise create a single new recognizing bubble for this direction
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

    private void HandleRecognizedMessage(MessageEventArgs e)
    {
        var recognizedText = e.Text?.Trim() ?? string.Empty;
        var nowUtc = DateTime.UtcNow;

        // Ignore likely TTS self-echo (translated audio feeding back into recognition).
        if (IsLikelyTtsEcho(recognizedText, e.IsFromMeeting, nowUtc))
        {
            AddLog($"↩️ Ignored likely TTS echo: {recognizedText}");
            return;
        }

        if (!string.IsNullOrEmpty(recognizedText) &&
            string.Equals(_lastRecognizedText, recognizedText, StringComparison.OrdinalIgnoreCase) &&
            _lastRecognizedIsFromMeeting == e.IsFromMeeting &&
            (nowUtc - _lastRecognizedAtUtc) < TimeSpan.FromSeconds(2))
        {
            return;
        }
        _lastRecognizedText = recognizedText;
        _lastRecognizedIsFromMeeting = e.IsFromMeeting;
        _lastRecognizedAtUtc = nowUtc;

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
        var originalText = e.OriginalText?.Trim() ?? string.Empty;
        var translatedText = e.TranslatedText?.Trim() ?? string.Empty;

        // In bypass mode (same source/target language), avoid duplicating transcript
        // into TranslatedText. Show only the original transcript bubble.
        if (_settings.IsBypassMode || IsBypassModeEnabled)
            return;

        var nowUtc = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(translatedText) &&
            string.Equals(_lastTranslationOriginal, originalText, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_lastTranslationText, translatedText, StringComparison.OrdinalIgnoreCase) &&
            _lastTranslationIsFromMeeting == e.IsFromMeeting &&
            (nowUtc - _lastTranslationAtUtc) < TimeSpan.FromSeconds(2))
        {
            return;
        }
        _lastTranslationOriginal = originalText;
        _lastTranslationText = translatedText;
        _lastTranslationIsFromMeeting = e.IsFromMeeting;
        _lastTranslationAtUtc = nowUtc;

        MessageBubble? currentBubble = e.IsFromMeeting ? _currentIncomingBubble : _currentOutgoingBubble;

        // ✅ FIX: Use per-bubble cancellation token instead of shared direction token
        MessageBubble? targetBubble = null;

        if (currentBubble != null && string.IsNullOrEmpty(currentBubble.TranslatedText))
        {
            targetBubble = currentBubble;
        }
        else
        {
            targetBubble = Messages.LastOrDefault(m =>
                m.IsFromMeeting == e.IsFromMeeting &&
                string.IsNullOrEmpty(m.TranslatedText) &&
                !m.IsSystemMessage);
        }

        if (targetBubble == null)
            return;

        // Cancel only THIS bubble's animation (if any)
        targetBubble.TranslationAnimationCts?.Cancel();
        targetBubble.TranslationAnimationCts = new CancellationTokenSource();

        var bubbleRef = targetBubble;
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

                    // Clean up the cancellation token
                    bubbleRef.TranslationAnimationCts?.Dispose();
                    bubbleRef.TranslationAnimationCts = null;
                }
            },
            targetBubble.TranslationAnimationCts.Token
        );
    }

    private bool IsLikelyTtsEcho(string recognizedText, bool isFromMeeting, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(recognizedText))
            return false;

        if (string.IsNullOrWhiteSpace(_lastTranslationText))
            return false;

        if (_lastTranslationIsFromMeeting != isFromMeeting)
            return false;

        // Short time window where echo loops commonly occur.
        if ((nowUtc - _lastTranslationAtUtc) > TimeSpan.FromSeconds(6))
            return false;

        return string.Equals(recognizedText, _lastTranslationText, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Helper Methods

    private void AddLog(string message)
    {
        lock (_logLock)
        {
            _logLines.Enqueue(message);
            while (_logLines.Count > MaxLogLinesInUi)
                _logLines.Dequeue();

            LogText = string.Join(Environment.NewLine, _logLines) + Environment.NewLine;
        }

        // ✅ Also log to Output window for debugging
        System.Diagnostics.Debug.WriteLine($"[MainViewModel] {message}");
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
        lock (_logLock)
        {
            _logLines.Clear();
            LogText = string.Empty;
        }

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
            var sessionManager = _clientService.GetSessionManager();
            await sessionManager.LogoutAsync(true);
        }
        else
        {
            var sessionManager = _clientService.GetSessionManager();
            await sessionManager.LogoutAsync(false);
        }
               

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
        _recognizingAnimationCts?.Dispose();
        
        // Dispose all bubble animation tokens
        foreach (var bubble in Messages)
        {
            bubble.TranslationAnimationCts?.Cancel();
            bubble.TranslationAnimationCts?.Dispose();
        }
        
        _backendService?.Dispose();
        _directAzureService?.Dispose();
        _directAzureBypassService?.Dispose();
    }

    private void WireServiceEvents(ITranslationService? service)
    {
        if (service == null) return;
        service.LogMessage += OnLogMessage;
        service.MessageReceived += OnMessageReceived;
        service.TranslationReceived += OnTranslationReceived;
        service.SystemMessage += OnSystemMessage;
        service.SynthesizingStatusChanged += OnSynthesizingStatusChanged;
    }

    private void UnwireServiceEvents(ITranslationService? service)
    {
        if (service == null) return;
        service.LogMessage -= OnLogMessage;
        service.MessageReceived -= OnMessageReceived;
        service.TranslationReceived -= OnTranslationReceived;
        service.SystemMessage -= OnSystemMessage;
        service.SynthesizingStatusChanged -= OnSynthesizingStatusChanged;
    }

    #endregion

    #region Cleanup Methods

    /// <summary>
    /// Clean up all resources when the application is closing
    /// </summary>
// No-op patch placeholder to ensure latest file context is in sync after service wiring updates.
    public async Task CleanupAsync()
    {
        if (IsRunning)
        {
            AddLog("🔄 Application closing - stopping translation...");

            // Wait for synthesis to complete (with timeout) - already handled in MainWindow
            // Just proceed with stopping translation
            try
            {
                await StopTranslation();
                AddLog("✅ Translation stopped successfully");
            }
            catch (Exception ex)
            {
                AddLog($"⚠️ Error stopping translation: {Classify(ex)}");
            }
        }
        else
        {
            // Send final session end with logs/transcript
            try
            {
                var sessionLog = BuildSessionLog();
                var sessionTranscript = BuildSessionTranscript();
                await _sessionService.EndSessionAsync(sessionLog, sessionTranscript);
                AddLog("✅ Session end reported to server");
            }
            catch (Exception ex)
            {
                AddLog($"⚠️ Error reporting session end: {Classify(ex)}");
            }
        }

        // Stop all timers
        try
        {
            _deviceRefreshTimer?.Stop();
            _sessionExpiryTimer?.Stop();
            AddLog("✅ Timers stopped");
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ Error stopping timers: {Classify(ex)}");
        }

        // Cancel all animations
        try
        {
            _recognizingAnimationCts?.Cancel();
            
            // Cancel all bubble animations
            foreach (var bubble in Messages)
            {
                bubble.TranslationAnimationCts?.Cancel();
                bubble.TranslationAnimationCts?.Dispose();
            }
            
            _deviceRefreshCts?.Cancel();
            AddLog("✅ Animations cancelled");
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ Error cancelling animations: {Classify(ex)}");
        }

// No-op patch placeholder to ensure latest file context is in sync after service wiring updates.
        AddLog("✅ Application cleanup complete");
    }
    #endregion
}