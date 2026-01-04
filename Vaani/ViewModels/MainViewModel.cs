using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vaani.Authentication.Models;
using Vaani.Authentication.Services;
using Vaani.Common;
using Vaani.Models;
using Vaani.Services;
namespace Vaani.ViewModels;

public class MainViewModel : ViewModelBase
{
    #region Fields
    private readonly DeviceService _deviceService;
    private readonly TranslationService _translationService;
    private readonly MeetingAuthenticationService _sessionService;
    private readonly ClientService _clientService = new();
    private readonly AnimatedTextDisplay _textAnimator = new() { WordDelayMs = 120 };
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
    private const int MaxMessages = 100;
    private string _logText = "";
    private bool _isMicrophoneMuted = false;
    private bool _isSpeakerMuted = false;
    private bool _isTransitioning = false;
    private bool _isSynthesizing = false;
    private bool _isMeetingAudioActive = false;

    // Animation cancellation tokens
    private CancellationTokenSource? _recognizingAnimationCts;
    private CancellationTokenSource? _translationAnimationCts;

    // Add this private field in the Fields region (near other private fields)
    private bool _inputDevicesLoaded = false;
    // Add this field near other private fields (Fields region)
    private CancellationTokenSource? _deviceRefreshCts = null;
    #endregion

    #region Constructor

    public MainViewModel()
    {
        _settings = LoadTranslationSettings();
        _deviceService = new DeviceService();
        _translationService = new TranslationService();
        _sessionService = new MeetingAuthenticationService();
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

        //StopCommand = ReactiveCommand.CreateFromTask(
        //    StopTranslation,
        //    this.WhenAnyValue(x => x.IsRunning));

        StopCommand = ReactiveCommand.CreateFromTask(
    StopTranslation,
    this.WhenAnyValue(
        x => x.IsRunning,
        x => x.IsSynthesizing,
        (running, synthesizing) => running && !synthesizing
    ));

        // RefreshDevices is now async Task, wire with CreateFromTask
        RefreshDevicesCommand = ReactiveCommand.CreateFromTask(RefreshDevices);

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
            Interval = TimeSpan.FromSeconds(7)
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
            AddLog($"⚠️ Startup initialization error: {ex.Message}");
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
        IsSettingsPanelVisible = false;
        IsTransitioning = true;
        try
        {
            LogText = "";
            Messages.Clear();
            _currentOutgoingBubble = null;
            _currentIncomingBubble = null;

            IsMicrophoneMuted = false;
            IsSpeakerMuted = false;

            (bool hasStarted, string message) = await _sessionService.StartSessionAsync();
            if (hasStarted)
            {
                await _translationService.StartTranslationAsync(_settings);
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
                IsRunning = false;
                IsTransitioning = false;
            });
        }
    }

    private async Task StopTranslation()
    {
        if (IsSynthesizing)
        {
            Toast.Show("Please wait for synthesis to complete before stopping.");
            return;
        }

        IsTransitioning = true;
        try
        {

            // Cancel any ongoing animations
            _recognizingAnimationCts?.Cancel();
            _translationAnimationCts?.Cancel();
            // Stop translation service

            // Await the stop to ensure clean shutdown before updating UI

           


            await _translationService.StopTranslationAsync();
        }
        finally
        {
            bool status = await _sessionService.EndSessionAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsRunning = false;
                IsTransitioning = false;
            });
        }
    }

    #endregion

    #region Device Management Methods


    // Replace the existing RefreshDevices method with this incremental, cancellable implementation.
    private async Task RefreshDevices()
    {
        var prevInput = SelectedInputDevice;
        var prevOutput = SelectedOutputDevice;

        // Cancel any in-progress refresh and create a new token for this run
        _deviceRefreshCts?.Cancel();
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

            // update properties of existing devices (preserve object references so bindings/selection remain)
            foreach (var id in existingInputById.Keys.Intersect(newInputById.Keys))
            {
                var oldDev = existingInputById[id];
                var newDev = newInputById[id];
                // update mutable properties that matter for the UI
                oldDev.FriendlyName = newDev.FriendlyName;
                // (copy other fields here if AudioDeviceInfo exposes them)
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

            foreach (var id in existingOutputById.Keys.Intersect(newOutputById.Keys))
            {
                var oldDev = existingOutputById[id];
                var newDev = newOutputById[id];
                oldDev.FriendlyName = newDev.FriendlyName;
            }

            foreach (var id in newOutputById.Keys.Except(existingOutputById.Keys))
            {
                var dev = newOutputById[id];
                dev.PropertyChanged += OnOutputDevicePropertyChanged;
                OutputDevices.Add(dev);
            }

            // --- Restore selection by Id (stable) or fall back to priority pick ---

            if (prevInput != null)
            {
                var restored = InputDevices.FirstOrDefault(d => d.Id == prevInput.Id);
                if (restored != null)
                {
                    restored.IsSelected = true;
                    SelectedInputDevice = restored;
                }
            }
            if (SelectedInputDevice == null)
                SelectedInputDevice = PickPriorityDevice(InputDevices, prevInput);

            if (prevOutput != null)
            {
                var restored = OutputDevices.FirstOrDefault(d => d.Id == prevOutput.Id);
                if (restored != null)
                {
                    restored.IsSelected = true;
                    SelectedOutputDevice = restored;
                }
            }
            if (SelectedOutputDevice == null)
                SelectedOutputDevice = PickPriorityDevice(OutputDevices, prevOutput);

            // Clear initial loading flag after first successful refresh
            if (!_inputDevicesLoaded)
            {
                _inputDevicesLoaded = true;
                IsInputDevicesLoading = false;
            }
        });

        sw.Stop();
        AddLog($"🔄 Devices refreshed in {sw.ElapsedMilliseconds}ms");
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
            IsSynthesizing = e.IsSynthesizing;

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