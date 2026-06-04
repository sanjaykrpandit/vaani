using Microsoft.AspNetCore.SignalR.Client;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Channels;
using Vaani.Common;
using Vaani.Models;
using static Vaani.Common.ErrorMessages;

namespace Vaani.Services;

/// <summary>
/// Desktop translation service that proxies all Azure Cognitive Services work to Vaani.API.
/// Implements the same ITranslationService interface as the local TranslationService so
/// ViewModels and UI code require zero changes.
///
/// Architecture:
///   Desktop Microphone → AudioCapture → SendAudioChunk (SignalR) → Backend
///   Backend → ReceiveTranslationEvent (SignalR) → events → ViewModel
/// </summary>
public class BackendTranslationService : ITranslationService
{
    // ─── Events (same contract as original TranslationService) ────────────────
    public event EventHandler<string>? LogMessage;
    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<TranslationEventArgs>? TranslationReceived;
    public event EventHandler<SystemMessageEventArgs>? SystemMessage;
    public event EventHandler<SynthesizingEventArgs>? SynthesizingStatusChanged;

    // ─── State ────────────────────────────────────────────────────────────────
    public bool IsRunning => _connection?.State == HubConnectionState.Connected
                             && !string.IsNullOrEmpty(_translationSessionId);

    private HubConnection? _connection;
    private IWaveIn? _micCapture;
    private IWaveIn? _loopbackCapture;

    private string? _translationSessionId;
    private TranslationSettings? _currentSettings;
    private CancellationTokenSource? _cts;

    private bool _isMicMuted;
    private bool _isSpeakerMuted;
    private bool _isBypassMode;
    private bool _disposed;
    private bool _isStoppingAudioCapture;
    private int _stopInProgress;
    private int _micRecoveryInProgress;
    private Task? _audioDeviceMonitorTask;
    private string? _boundMicDeviceId;
    private string? _boundLoopbackDeviceId;

    // Ensures "Translation Stopped" system event is emitted only once per session
    private int _stoppedEventSent;

    // Audio format the backend expects: 16kHz, 16-bit, mono
    private static readonly WaveFormat AudioFormat = new(16000, 16, 1);

    // Device helper for finding physical mic + loopback device
    private readonly DeviceService _deviceService = new();

    private long _outgoingSeq;
    private long _incomingSeq;

    // ─── Reconnection tracking ────────────────────────────────────────────────
    // After this many consecutive reconnect attempts we give up and tell the user.
    private const int MaxReconnectAttempts = 5;
    private int _reconnectAttempts;

    // ─── Audio-chunk send circuit-breaker ────────────────────────────────────
    // Suppresses per-chunk error log spam during a connection drop.
    // After the first send failure, chunks are silently dropped for SilentDropWindow.
    // The error is logged once and the circuit re-closes after the window expires.
    private static readonly TimeSpan SilentDropWindow = TimeSpan.FromSeconds(3);
    private DateTime _lastChunkSendError = DateTime.MinValue;

    // Suppresses loopback capture while synthesized audio is playing to the CABLE device.
    // Prevents the translated output from feeding back into the incoming pipeline.
    private int _outgoingSynthesisActive;
    private static readonly TimeSpan LoopbackSuppressBuffer = TimeSpan.FromMilliseconds(500);

    // Suppresses physical mic capture while incoming translated audio plays through the
    // physical speaker to prevent acoustic echo feeding back into the pipeline.
    private int _incomingSynthesisActive;
    private static readonly TimeSpan MicSuppressBuffer = TimeSpan.FromMilliseconds(800);

    // Per-direction playback queues — serialise audio on each output device so sentences
    // never overlap. Outgoing (CABLE) and Incoming (speaker) run concurrently.
    private readonly Channel<byte[]> _outgoingPlaybackQueue =
        Channel.CreateBounded<byte[]>(new BoundedChannelOptions(3)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Channel<byte[]> _incomingPlaybackQueue =
        Channel.CreateBounded<byte[]>(new BoundedChannelOptions(3)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    // Bypass mode persistent players
    private WasapiOut? _bypassOutgoingPlayer;
    private BufferedWaveProvider? _bypassOutgoingBuffer;
    private WasapiOut? _bypassIncomingPlayer;
    private BufferedWaveProvider? _bypassIncomingBuffer;

    // ─────────────────────────────────────────────────────────────────────────
    // ITranslationService — Start
    // ─────────────────────────────────────────────────────────────────────────

    public async Task StartTranslationAsync(TranslationSettings settings)
    {
        if (IsRunning)
        {
            Log("Translation is already running.");
            return;
        }

        if (settings == null || string.IsNullOrWhiteSpace(settings.BackendTranslationHubUrl))
        {
            FireSystemMessage(
                "Configuration error",
                "Backend translation URL is not configured. Please re-join the meeting.",
                SystemMessageType.Error);
            return;
        }

        _currentSettings = settings;
        _isBypassMode = settings.IsBypassMode;
        _reconnectAttempts = 0;
        _cts = new CancellationTokenSource();

        Log($"Connecting to backend hub: {settings.BackendTranslationHubUrl}");

        // Build SignalR connection with JWT auth and auto-reconnect
        _connection = new HubConnectionBuilder()
            .WithUrl(settings.BackendTranslationHubUrl, options =>
            {
                options.AccessTokenProvider = () =>
                    Task.FromResult<string?>(settings.SessionToken);
            })
            .WithAutomaticReconnect(new[] {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20)
            })
            .Build();

        RegisterHubCallbacks();

        _connection.Reconnecting += ex =>
        {
            _reconnectAttempts++;
            Log($"SignalR reconnecting (attempt {_reconnectAttempts}/{MaxReconnectAttempts})...");

            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                // Give up — tell the user clearly instead of spinning forever
                FireSystemMessage(
                    "Translation disconnected",
                    "Unable to reconnect to the translation server after multiple attempts. " +
                    "Please check your internet connection and restart the translation.",
                    SystemMessageType.Error);
                // Cancel the session so no more audio is captured
                _ = StopTranslationAsync();
            }
            else
            {
                FireSystemMessage(
                    $"Reconnecting… (attempt {_reconnectAttempts}/{MaxReconnectAttempts})",
                    "Network issue detected. Attempting to restore translation.",
                    SystemMessageType.Error);
            }
            return Task.CompletedTask;
        };

        _connection.Reconnected += async (_connId) =>
        {
            _reconnectAttempts = 0;
            Log("SignalR reconnected. Restarting translation session...");
            FireSystemMessage("Translation restored", "Connection re-established.", SystemMessageType.Started);
            try
            {
                await StartBackendSessionAsync(settings);
            }
            catch (Exception ex)
            {
                // Reconnected callback must NOT propagate — log and surface cleanly
                Log($"Session restart after reconnect failed: {Classify(ex)}");
                FireSystemMessage(
                    "Session restart failed",
                    "Could not restore the translation session. Please restart translation.",
                    SystemMessageType.Error);
            }
        };

        _connection.Closed += ex =>
        {
            if (ex != null)
            {
                Log($"SignalR connection closed with error: {Classify(ex)}");
                FireSystemMessage(
                    "Translation connection closed",
                    "The connection to the translation server was lost. Please restart translation.",
                    SystemMessageType.Error);
            }
            else
            {
                Log("SignalR connection closed cleanly.");
            }
            return Task.CompletedTask;
        };

        try
        {
            // Apply a 30-second connection timeout independent of the session lifetime CTS
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(30));

            await _connection.StartAsync(connectCts.Token);
            Log("SignalR connected. Starting translation session...");
            await StartBackendSessionAsync(settings);
        }
        catch (OperationCanceledException)
        {
            FireSystemMessage(
                "Connection timed out",
                "Could not connect to the translation server within 30 seconds. " +
                "Please check your internet connection and try again.",
                SystemMessageType.Error);
            Log("Connection attempt timed out after 30 s.");
        }
        catch (Exception ex)
        {
            FireSystemMessage(
                "Connection failed",
                Classify(ex),
                SystemMessageType.Error);
            Log($"Connection failed: {Classify(ex)}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ITranslationService — Stop
    // ─────────────────────────────────────────────────────────────────────────

    public async Task StopTranslationAsync()
    {
        if (Interlocked.Exchange(ref _stopInProgress, 1) == 1)
            return;

        try
        {
        Log("Stopping backend translation...");

        StopAudioCapture();

        if (_connection != null && !string.IsNullOrEmpty(_translationSessionId))
        {
            try
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _connection.InvokeAsync("StopTranslation", _translationSessionId, stopCts.Token);
            }
            catch (Exception ex)
            {
                // Non-fatal — session will time out on the server side regardless
                Log($"Warning: StopTranslation hub call failed: {Classify(ex)}");
            }
        }

        _translationSessionId = null;

        try { _cts?.Cancel(); } catch { }

        if (_connection != null)
        {
            try { await _connection.StopAsync(); } catch { }
            await _connection.DisposeAsync();
            _connection = null;
        }

        EmitStoppedOnce();
        Log("Backend translation stopped.");
        }
        finally
        {
            Interlocked.Exchange(ref _stopInProgress, 0);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ITranslationService — Mute controls
    // ─────────────────────────────────────────────────────────────────────────

    public async void SetMicrophoneMute(bool muted)
    {
        _isMicMuted = muted;
        if (_connection?.State == HubConnectionState.Connected && !string.IsNullOrEmpty(_translationSessionId))
        {
            try
            {
                await _connection.InvokeAsync("ControlTranslation", new
                {
                    TranslationSessionId = _translationSessionId,
                    Action = muted ? 1 : 2   // MuteMicrophone = 1, UnmuteMicrophone = 2
                });
            }
            catch (Exception ex)
            {
                var msg = Classify(ex);
                Log($"Mute mic error: {msg}");
                // Notify UI so the user knows the mute command may not have reached the server
                if (!IsTransient(ex))
                    FireSystemMessage("Mute command failed", msg, SystemMessageType.Error);
            }
        }
        Log(muted ? "Microphone MUTED" : "Microphone UNMUTED");
    }

    public async void SetSpeakerMute(bool muted)
    {
        _isSpeakerMuted = muted;
        if (_connection?.State == HubConnectionState.Connected && !string.IsNullOrEmpty(_translationSessionId))
        {
            try
            {
                await _connection.InvokeAsync("ControlTranslation", new
                {
                    TranslationSessionId = _translationSessionId,
                    Action = muted ? 3 : 4   // MuteSpeaker = 3, UnmuteSpeaker = 4
                });
            }
            catch (Exception ex)
            {
                var msg = Classify(ex);
                Log($"Mute speaker error: {msg}");
                if (!IsTransient(ex))
                    FireSystemMessage("Mute command failed", msg, SystemMessageType.Error);
            }
        }
        Log(muted ? "Speaker MUTED" : "Speaker UNMUTED");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: backend session start
    // ─────────────────────────────────────────────────────────────────────────

    private async Task StartBackendSessionAsync(TranslationSettings settings)
    {
        try
        {
            var startRequest = new
            {
                MeetingId = settings.MeetingId ?? string.Empty,
                SessionId = settings.SessionId ?? string.Empty,
                SourceLanguage = settings.SourceLanguage,
                TargetLanguage = settings.TargetLanguage,
                SourceVoice = settings.SourceVoice,
                TargetVoice = settings.TargetVoice,
                Direction = _isBypassMode ? 4 : 3,
                AudioFormat = "Raw16Khz16BitMonoPcm"
            };

            // Session start should complete quickly once connected
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_cts!.Token);
            sessionCts.CancelAfter(TimeSpan.FromSeconds(15));

            await _connection!.InvokeAsync("StartTranslation", startRequest, sessionCts.Token);
            // TranslationSessionId arrives via the "SessionStarted" hub callback
        }
        catch (OperationCanceledException)
        {
            const string msg = "The translation server did not respond in time. Please check your connection.";
            Log($"StartBackendSession timed out: {msg}");
            FireSystemMessage("Session start timed out", msg, SystemMessageType.Error);
        }
        catch (Exception ex)
        {
            var msg = Classify(ex);
            Log($"StartBackendSession failed: {msg}");
            FireSystemMessage("Session start failed", msg, SystemMessageType.Error);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: SignalR hub callbacks
    // ─────────────────────────────────────────────────────────────────────────

    private void RegisterHubCallbacks()
    {
        // Session started — server sends back the TranslationSessionId
        _connection!.On<object>("SessionStarted", payload =>
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("translationSessionId", out var idEl))
                {
                    _translationSessionId = idEl.GetString();
                    _stoppedEventSent = 0; // reset for new active session
                    Log($"Translation session established: {_translationSessionId}");
                    FireSystemMessage("Translation Started", "", SystemMessageType.Started);
                    StartAudioCapture();
                }
            }
            catch (Exception ex) { Log($"SessionStarted parse error: {Classify(ex)}"); }
        });

        // Main event stream from backend
        _connection.On<object>("ReceiveTranslationEvent", payload =>
        {
            try { HandleTranslationEvent(payload); }
            catch (Exception ex) { Log($"Event handling error: {Classify(ex)}"); }
        });

        // Session stopped by server
        _connection.On<string>("SessionStopped", id =>
        {
            Log($"Server stopped session {id}");
            _translationSessionId = null;
            StopAudioCapture();
            EmitStoppedOnce();
        });

        // Error reported by the backend — map server-side codes to friendly text
        _connection.On<string, string>("ReceiveError", (code, rawMessage) =>
        {
            var friendlyMsg = MapBackendCode(code, rawMessage);
            Log($"Hub error [{code}]: {friendlyMsg}");
            FireSystemMessage("Translation error", friendlyMsg, SystemMessageType.Error);
        });
    }

    private void HandleTranslationEvent(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var eventTypeInt = root.TryGetProperty("eventType", out var et) ? et.GetInt32() : 0;
        var pipelineInt = root.TryGetProperty("pipeline", out var pl) ? pl.GetInt32() : 1;
        var originalText = root.TryGetProperty("originalText", out var ot) ? ot.GetString() ?? "" : "";
        var translatedText = root.TryGetProperty("translatedText", out var tt) ? tt.GetString() ?? "" : "";
        var audioData = root.TryGetProperty("audioData", out var ad) && ad.ValueKind != JsonValueKind.Null
            ? ad.GetBytesFromBase64()
            : null;
        var systemMessage = root.TryGetProperty("systemMessage", out var sm) ? sm.GetString() ?? "" : "";

        var direction = pipelineInt == 2 ? MessageDirection.Incoming : MessageDirection.Outgoing;
        var isFromMeeting = direction == MessageDirection.Incoming;

        switch (eventTypeInt)
        {
            case 1: // Recognizing
                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = direction,
                    Text = originalText,
                    MessageType = MessageType.Recognizing,
                    IsFromMeeting = isFromMeeting
                });
                break;

            case 2: // Recognized
                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = direction,
                    Text = originalText,
                    MessageType = MessageType.Recognized,
                    IsFromMeeting = isFromMeeting
                });
                if (!string.IsNullOrEmpty(translatedText))
                {
                    TranslationReceived?.Invoke(this, new TranslationEventArgs
                    {
                        Direction = direction,
                        OriginalText = originalText,
                        TranslatedText = translatedText,
                        IsFromMeeting = isFromMeeting
                    });
                }
                break;

            case 3: // Translated
                TranslationReceived?.Invoke(this, new TranslationEventArgs
                {
                    Direction = direction,
                    OriginalText = originalText,
                    TranslatedText = translatedText,
                    IsFromMeeting = isFromMeeting
                });
                break;

            case 4: // AudioOutput
                TranslationReceived?.Invoke(this, new TranslationEventArgs
                {
                    Direction = direction,
                    OriginalText = originalText,
                    TranslatedText = translatedText,
                    IsFromMeeting = isFromMeeting
                });
                if (audioData != null && audioData.Length > 0)
                {
                    if (direction == MessageDirection.Outgoing)
                        _outgoingPlaybackQueue.Writer.TryWrite(audioData);
                    else
                        _incomingPlaybackQueue.Writer.TryWrite(audioData);
                }
                break;

            case 5: // SynthesizingStarted
                SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                {
                    IsSynthesizing = true,
                    IsFromMeeting = isFromMeeting,
                    OriginalText = originalText,
                    TranslatedText = translatedText
                });
                break;

            case 6: // SynthesizingCompleted
                SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                {
                    IsSynthesizing = false,
                    IsFromMeeting = isFromMeeting,
                    OriginalText = originalText,
                    TranslatedText = translatedText
                });
                break;

            case 9: // Pipeline error from backend — map to friendly message
                var pipelineMsg = MapBackendCode("PIPELINE_ERROR", systemMessage);
                Log($"Backend pipeline error: {pipelineMsg}");
                FireSystemMessage("Translation pipeline error", pipelineMsg, SystemMessageType.Error);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: Audio capture (mic + loopback → backend)
    // ─────────────────────────────────────────────────────────────────────────

    private void StartAudioCapture()
    {
        _isStoppingAudioCapture = false;
        _boundMicDeviceId = null;
        _boundLoopbackDeviceId = null;

        // Device topology may have changed while app was open (plug/unplug).
        // Force refresh before opening capture streams.
        _deviceService.RefreshDeviceCache();

        // Drain any audio left from a previous session
        while (_outgoingPlaybackQueue.Reader.TryRead(out _)) { }
        while (_incomingPlaybackQueue.Reader.TryRead(out _)) { }

        // Start per-direction playback consumers (sequential per device, concurrent across devices)
        _ = Task.Run(() => RunPlaybackQueueAsync(_outgoingPlaybackQueue, MessageDirection.Outgoing, _cts!.Token));
        _ = Task.Run(() => RunPlaybackQueueAsync(_incomingPlaybackQueue, MessageDirection.Incoming, _cts!.Token));

        StartMicCapture();
        StartLoopbackCapture();

        if (_audioDeviceMonitorTask == null || _audioDeviceMonitorTask.IsCompleted)
            _audioDeviceMonitorTask = Task.Run(() => MonitorAudioDevicesAsync(_cts!.Token));

        if (_isBypassMode)
            StartBypassPlayers();
    }

    private void StartMicCapture()
    {
        try
        {
            IWaveIn? capture = null;
            Exception? lastMicEx = null;

            // Try WASAPI physical mic twice (refreshing cache between attempts),
            // then fall back to default WaveIn.
            for (var attempt = 1; attempt <= 2 && capture == null; attempt++)
            {
                var physicalMic = _deviceService.FindPhysicalMicrophone();
                if (physicalMic == null)
                    break;

                try
                {
                    Log($"Microphone: {physicalMic.FriendlyName}");
                    _boundMicDeviceId = physicalMic.ID;
                    capture = new WasapiCapture(physicalMic, false, 100);
                }
                catch (Exception ex)
                {
                    lastMicEx = ex;
                    if (attempt == 1)
                    {
                        Log($"WARNING: Physical microphone open failed ({ClassifyDevice(ex)}). Retrying after refresh...");
                        _deviceService.RefreshDeviceCache();
                    }
                }
            }

            if (capture == null)
            {
                if (lastMicEx != null)
                    Log($"WARNING: Falling back to default WaveIn microphone due to: {ClassifyDevice(lastMicEx)}");
                Log("WARNING: Physical microphone not found — using default WaveIn device.");
                _boundMicDeviceId = "__DEFAULT_WAVEIN__";
                capture = new WaveInEvent { WaveFormat = AudioFormat, BufferMilliseconds = 100 };
            }

            _micCapture = capture;
            var captureFormat = capture.WaveFormat; // read native format before StartRecording
            _micCapture.RecordingStopped += OnMicRecordingStopped;

            _micCapture.DataAvailable += async (_, e) =>
            {
                if (_isMicMuted || string.IsNullOrEmpty(_translationSessionId)) return;
                if (_connection?.State != HubConnectionState.Connected) return;
                // In bypass mode we don't play TTS to the speaker, so no acoustic echo to suppress
                if (!_isBypassMode && Volatile.Read(ref _incomingSynthesisActive) > 0) return;

                var pcm = ToTarget16kHz(e.Buffer, e.BytesRecorded, captureFormat);
                if (pcm.Length == 0) return;

                // Bypass: feed native-format bytes to the persistent CABLE-A player (full quality, no gaps)
                if (_isBypassMode)
                    _bypassOutgoingBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

                var chunk = BuildChunkFromPcm(pcm, 1);
                await SendAudioChunkSafeAsync(chunk, "mic");
            };

            _micCapture.StartRecording();
            Log("Microphone capture started.");
        }
        catch (Exception ex)
        {
            var msg = $"Could not start microphone: {ClassifyDevice(ex)}";
            Log($"ERROR: {msg}");
            FireSystemMessage("Microphone error", msg, SystemMessageType.Error);
        }
    }

    private async void OnMicRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_disposed || _isStoppingAudioCapture)
            return;

        if (_cts?.IsCancellationRequested != false)
            return;

        if (string.IsNullOrEmpty(_translationSessionId) || _connection?.State != HubConnectionState.Connected)
            return;

        if (Interlocked.Exchange(ref _micRecoveryInProgress, 1) == 1)
            return;

        try
        {
            var reason = e.Exception != null ? ClassifyDevice(e.Exception) : "device changed";
            Log($"WARNING: Microphone capture stopped ({reason}). Attempting auto-recovery...");

            try
            {
                _micCapture?.Dispose();
            }
            catch { }
            finally
            {
                _micCapture = null;
            }

            await Task.Delay(500);
            _deviceService.RefreshDeviceCache();
            StartMicCapture();
        }
        catch (Exception ex)
        {
            var msg = $"Microphone recovery failed: {ClassifyDevice(ex)}";
            Log($"ERROR: {msg}");
            FireSystemMessage("Microphone error", msg, SystemMessageType.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _micRecoveryInProgress, 0);
        }
    }

    private void StartLoopbackCapture()
    {
        try
        {
            MMDevice? loopback = null;
            Exception? lastLoopbackEx = null;

            for (var attempt = 1; attempt <= 2 && _loopbackCapture == null; attempt++)
            {
                loopback = _deviceService.FindIncomingCableDevice();
                if (loopback == null)
                    break;

                try
                {
                    Log($"Loopback device (meeting audio): {loopback.FriendlyName}");
                    _boundLoopbackDeviceId = loopback.ID;
                    _loopbackCapture = new WasapiCapture(loopback, false, 100);
                }
                catch (Exception ex)
                {
                    lastLoopbackEx = ex;
                    if (attempt == 1)
                    {
                        Log($"WARNING: Meeting audio capture open failed ({ClassifyDevice(ex)}). Retrying after refresh...");
                        _deviceService.RefreshDeviceCache();
                    }
                }
            }

            if (_loopbackCapture == null)
            {
                var reason = lastLoopbackEx != null ? $" ({ClassifyDevice(lastLoopbackEx)})" : string.Empty;
                const string baseMsg = "Meeting audio device (CABLE-B) not found. " +
                    "Ensure the meeting software output is set to 'CABLE-B Input'. " +
                    "Incoming translation will be unavailable until this is corrected.";
                var msg = baseMsg + reason;
                Log($"Warning: {msg}");
                FireSystemMessage("Meeting audio device missing", msg, SystemMessageType.Error);
                return;
            }

            var captureFormat = _loopbackCapture.WaveFormat;

            _loopbackCapture.DataAvailable += async (_, e) =>
            {
                if (_isSpeakerMuted || string.IsNullOrEmpty(_translationSessionId)) return;
                if (_connection?.State != HubConnectionState.Connected) return;
                // In bypass mode we don't inject TTS into CABLE, so no loopback echo to suppress
                if (!_isBypassMode && Volatile.Read(ref _outgoingSynthesisActive) > 0) return;

                var pcm = ToTarget16kHz(e.Buffer, e.BytesRecorded, captureFormat);
                if (pcm.Length == 0) return;

                // Bypass: feed native-format bytes to the persistent speaker player (full quality, no gaps)
                if (_isBypassMode)
                    _bypassIncomingBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

                var chunk = BuildChunkFromPcm(pcm, 2);
                await SendAudioChunkSafeAsync(chunk, "loopback");
            };

            _loopbackCapture.StartRecording();
            Log("Loopback capture started.");
        }
        catch (Exception ex)
        {
            var msg = $"Could not start meeting audio capture: {ClassifyDevice(ex)}";
            Log($"ERROR: {msg}");
            FireSystemMessage("Meeting audio error", msg, SystemMessageType.Error);
        }
    }

    private async Task MonitorAudioDevicesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);

                if (_isStoppingAudioCapture || _disposed || string.IsNullOrEmpty(_translationSessionId))
                    continue;

                // Force re-enumeration; this path is intentionally polling-based for stability.
                _deviceService.RefreshDeviceCache();

                var currentMic = _deviceService.FindPhysicalMicrophone();
                var currentMicId = currentMic?.ID;

                var currentLoopback = _deviceService.FindIncomingCableDevice();
                var currentLoopbackId = currentLoopback?.ID;

                if (!string.IsNullOrWhiteSpace(currentMicId) &&
                    !string.Equals(currentMicId, _boundMicDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Audio device change detected: microphone switched to {currentMic!.FriendlyName}. Rebinding...");
                    RebindMicrophoneCapture();
                }

                if (!string.IsNullOrWhiteSpace(currentLoopbackId) &&
                    !string.Equals(currentLoopbackId, _boundLoopbackDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Audio device change detected: meeting audio capture switched to {currentLoopback!.FriendlyName}. Rebinding...");
                    RebindLoopbackCapture();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Device monitor warning: {ClassifyDevice(ex)}");
            }
        }
    }

    private void RebindMicrophoneCapture()
    {
        if (_isStoppingAudioCapture || _disposed) return;
        if (Interlocked.Exchange(ref _micRecoveryInProgress, 1) == 1) return;

        try
        {
            try
            {
                if (_micCapture != null)
                    _micCapture.RecordingStopped -= OnMicRecordingStopped;
            }
            catch { }

            try { _micCapture?.StopRecording(); _micCapture?.Dispose(); } catch { }
            _micCapture = null;

            StartMicCapture();
        }
        finally
        {
            Interlocked.Exchange(ref _micRecoveryInProgress, 0);
        }
    }

    private void RebindLoopbackCapture()
    {
        if (_isStoppingAudioCapture || _disposed) return;

        try { _loopbackCapture?.StopRecording(); _loopbackCapture?.Dispose(); } catch { }
        _loopbackCapture = null;

        StartLoopbackCapture();
    }

    /// <summary>
    /// Sends an audio chunk to the hub with a circuit-breaker to prevent
    /// log spam during a temporary network drop (100ms chunks × many seconds = thousands of errors).
    /// First failure is logged and shown; subsequent failures within <see cref="SilentDropWindow"/>
    /// are silently dropped. Normal service resumes automatically when the connection is restored.
    /// </summary>
    private async Task SendAudioChunkSafeAsync(object chunk, string source)
    {
        try
        {
            await _connection!.SendAsync("SendAudioChunk", chunk);
            // Reset circuit on success
            _lastChunkSendError = DateTime.MinValue;
        }
        catch (Exception ex)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastChunkSendError) > SilentDropWindow)
            {
                // First error after a clean period — log once and open the circuit
                _lastChunkSendError = now;
                Log($"{source} audio send failed (chunks will be silently dropped for " +
                    $"{SilentDropWindow.TotalSeconds}s): {Classify(ex)}");
            }
            // Subsequent errors within the window are silently dropped — no log, no spam
        }
    }

    private void StartBypassPlayers()
    {
        // Outgoing: mic → CABLE-A
        try
        {
            if (_micCapture != null)
            {
                var cableDevice = _deviceService.FindOutgoingCableDevice();
                if (cableDevice != null)
                {
                    _bypassOutgoingBuffer = new BufferedWaveProvider(_micCapture.WaveFormat)
                    {
                        BufferDuration = TimeSpan.FromSeconds(1),
                        DiscardOnBufferOverflow = true
                    };
                    _bypassOutgoingPlayer = new WasapiOut(cableDevice, AudioClientShareMode.Shared, false, 10);
                    _bypassOutgoingPlayer.Init(_bypassOutgoingBuffer);
                    _bypassOutgoingPlayer.Play();
                    Log($"Bypass outgoing player started (mic → CABLE-A) [{_micCapture.WaveFormat}].");
                }
                else Log("Bypass outgoing: CABLE-A not found — mic audio will not reach the meeting.");
            }
        }
        catch (Exception ex) { Log($"ERROR starting bypass outgoing player: {ClassifyDevice(ex)}"); }

        // Incoming: CABLE-B → speaker
        try
        {
            if (_loopbackCapture != null)
            {
                _bypassIncomingBuffer = new BufferedWaveProvider(_loopbackCapture.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(1),
                    DiscardOnBufferOverflow = true
                };
                var physicalSpeaker = _deviceService.FindPhysicalSpeaker();
                _bypassIncomingPlayer = physicalSpeaker != null
                    ? new WasapiOut(physicalSpeaker, AudioClientShareMode.Shared, false, 10)
                    : new WasapiOut(AudioClientShareMode.Shared, 10);
                _bypassIncomingPlayer.Init(_bypassIncomingBuffer);
                _bypassIncomingPlayer.Play();
                Log($"Bypass incoming player started (CABLE-B → speaker) [{_loopbackCapture.WaveFormat}].");
            }
        }
        catch (Exception ex) { Log($"ERROR starting bypass incoming player: {ClassifyDevice(ex)}"); }
    }

    private void StopAudioCapture()
    {
        _isStoppingAudioCapture = true;
        Interlocked.Exchange(ref _micRecoveryInProgress, 0);
        _boundMicDeviceId = null;
        _boundLoopbackDeviceId = null;

        try
        {
            if (_micCapture != null)
                _micCapture.RecordingStopped -= OnMicRecordingStopped;
        }
        catch { }

        try { _micCapture?.StopRecording(); _micCapture?.Dispose(); _micCapture = null; } catch { }
        try { _loopbackCapture?.StopRecording(); _loopbackCapture?.Dispose(); _loopbackCapture = null; } catch { }
        try { _bypassOutgoingPlayer?.Stop(); _bypassOutgoingPlayer?.Dispose(); _bypassOutgoingPlayer = null; } catch { }
        try { _bypassIncomingPlayer?.Stop(); _bypassIncomingPlayer?.Dispose(); _bypassIncomingPlayer = null; } catch { }
        _bypassOutgoingBuffer = null;
        _bypassIncomingBuffer = null;
    }

    private object BuildChunkFromPcm(byte[] pcm, int pipeline) => new
    {
        TranslationSessionId = _translationSessionId,
        Pipeline = pipeline,
        Data = pcm,
        SequenceNumber = pipeline == 1
            ? Interlocked.Increment(ref _outgoingSeq)
            : Interlocked.Increment(ref _incomingSeq),
        CapturedAt = DateTime.UtcNow
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Private: Audio playback (translated audio → physical speaker or CABLE)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunPlaybackQueueAsync(Channel<byte[]> queue, MessageDirection direction, CancellationToken ct)
    {
        try
        {
            await foreach (var pcmData in queue.Reader.ReadAllAsync(ct))
                await PlayAudioAsync(pcmData, direction);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log($"Playback queue error: {ClassifyDevice(ex)}"); }
    }

    private async Task PlayAudioAsync(byte[] pcmData, MessageDirection direction)
    {
        try
        {
            if (direction == MessageDirection.Outgoing)
            {
                var cableDevice = _deviceService.FindOutgoingCableDevice();
                var suppressLoopback = !_isBypassMode && ShouldSuppressLoopbackDuringOutgoingPlayback(cableDevice);
                if (suppressLoopback) Interlocked.Increment(ref _outgoingSynthesisActive);
                try
                {
                    await AudioPlaybackManager.PlayAudioToCableDeviceAsync(pcmData, cableDevice);
                    if (suppressLoopback) await Task.Delay(LoopbackSuppressBuffer);
                }
                finally
                {
                    if (suppressLoopback) Interlocked.Decrement(ref _outgoingSynthesisActive);
                }
            }
            else
            {
                var physicalSpeaker = _deviceService.FindPhysicalSpeaker();
                var suppressMic = !_isBypassMode && ShouldSuppressMicrophoneDuringIncomingPlayback(physicalSpeaker);
                if (suppressMic) Interlocked.Increment(ref _incomingSynthesisActive);
                try
                {
                    await AudioPlaybackManager.PlayAudioToPhysicalSpeakerAsync(pcmData, physicalSpeaker);
                    if (suppressMic) await Task.Delay(MicSuppressBuffer);
                }
                finally
                {
                    if (suppressMic) Interlocked.Decrement(ref _incomingSynthesisActive);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Audio playback error: {ClassifyDevice(ex)}");
        }
    }

    private bool ShouldSuppressLoopbackDuringOutgoingPlayback(MMDevice? outgoingCableDevice)
    {
        if (outgoingCableDevice == null || string.IsNullOrWhiteSpace(_boundLoopbackDeviceId))
            return false;

        return string.Equals(outgoingCableDevice.ID, _boundLoopbackDeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSuppressMicrophoneDuringIncomingPlayback(MMDevice? physicalSpeaker)
    {
        if (physicalSpeaker == null)
            return true;

        return !IsIsolatedPlaybackDevice(physicalSpeaker);
    }

    private static bool IsIsolatedPlaybackDevice(MMDevice device)
    {
        var name = device.FriendlyName;
        return name.Contains("Headset", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Headphone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Earphone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Earbud", StringComparison.OrdinalIgnoreCase)
            || name.Contains("AirPods", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pods", StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: Audio format conversion
    // ─────────────────────────────────────────────────────────────────────────

    private static byte[] ToTarget16kHz(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        if (sourceFormat.SampleRate == AudioFormat.SampleRate &&
            sourceFormat.BitsPerSample == AudioFormat.BitsPerSample &&
            sourceFormat.Channels == AudioFormat.Channels &&
            sourceFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            var copy = new byte[bytesRecorded];
            Array.Copy(buffer, copy, bytesRecorded);
            return copy;
        }
        using var ms = new MemoryStream(buffer, 0, bytesRecorded);
        ISampleProvider samples = new RawSourceWaveStream(ms, sourceFormat).ToSampleProvider();
        if (samples.WaveFormat.Channels == 2)
            samples = new StereoToMonoSampleProvider(samples);
        if (samples.WaveFormat.SampleRate != AudioFormat.SampleRate)
            samples = new WdlResamplingSampleProvider(samples, AudioFormat.SampleRate);
        var pcm16 = samples.ToWaveProvider16();
        using var output = new MemoryStream();
        var buf = new byte[4096];
        int read;
        while ((read = pcm16.Read(buf, 0, buf.Length)) > 0)
            output.Write(buf, 0, read);
        return output.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: Logging and event helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void Log(string message) =>
        LogMessage?.Invoke(this, $"[BackendTranslation] {message}");

    private void FireSystemMessage(string message, string detail, SystemMessageType type) =>
        SystemMessage?.Invoke(this, new SystemMessageEventArgs
        {
            Message = message,
            Detail = detail,
            MessageType = type
        });

    private void EmitStoppedOnce()
    {
        if (Interlocked.Exchange(ref _stoppedEventSent, 1) == 1)
            return;

        FireSystemMessage("Translation Stopped", "", SystemMessageType.Stopped);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAudioCapture();
        try { _cts?.Cancel(); _cts?.Dispose(); } catch { }
        try { _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        GC.SuppressFinalize(this);
    }
}
