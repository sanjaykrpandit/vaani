using Microsoft.AspNetCore.SignalR.Client;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Text.Json;
using Vaani.Models;

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
    private WaveInEvent? _micCapture;
    private WaveInEvent? _loopbackCapture;

    private string? _translationSessionId;
    private TranslationSettings? _currentSettings;
    private CancellationTokenSource? _cts;

    private bool _isMicMuted;
    private bool _isSpeakerMuted;
    private bool _disposed;

    // Audio format the backend expects: 16kHz, 16-bit, mono
    private static readonly WaveFormat AudioFormat = new(16000, 16, 1);

    // Device helper for finding physical mic + loopback device
    private readonly DeviceService _deviceService = new();

    private long _outgoingSeq;
    private long _incomingSeq;

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
            Log("ERROR: BackendTranslationHubUrl is not configured in TranslationSettings.");
            return;
        }

        _currentSettings = settings;
        _cts = new CancellationTokenSource();

        Log($"Connecting to backend hub: {settings.BackendTranslationHubUrl}");

        // Build SignalR connection with JWT auth and auto-reconnect
        _connection = new HubConnectionBuilder()
            .WithUrl(settings.BackendTranslationHubUrl, options =>
            {
                options.AccessTokenProvider = () =>
                    Task.FromResult<string?>(settings.SessionToken);
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        RegisterHubCallbacks();

        _connection.Reconnecting += _ =>
        {
            Log("SignalR reconnecting...");
            FireSystemMessage("Reconnecting...", "", SystemMessageType.Error);
            return Task.CompletedTask;
        };

        _connection.Reconnected += async (_connId) =>
        {
            Log("SignalR reconnected. Restarting translation session...");
            await StartBackendSessionAsync(settings);
        };

        _connection.Closed += _ =>
        {
            Log("SignalR connection closed.");
            return Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync(_cts.Token);
            Log("SignalR connected. Starting translation session...");
            await StartBackendSessionAsync(settings);
        }
        catch (Exception ex)
        {
            Log($"ERROR connecting to backend hub: {ex.Message}");
            FireSystemMessage("Connection failed", ex.Message, SystemMessageType.Error);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ITranslationService — Stop
    // ─────────────────────────────────────────────────────────────────────────

    public async Task StopTranslationAsync()
    {
        Log("Stopping backend translation...");

        StopAudioCapture();

        if (_connection != null && !string.IsNullOrEmpty(_translationSessionId))
        {
            try
            {
                await _connection.InvokeAsync("StopTranslation", _translationSessionId);
            }
            catch (Exception ex)
            {
                Log($"Warning: error sending StopTranslation to hub: {ex.Message}");
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

        FireSystemMessage("Translation Stopped", "", SystemMessageType.Stopped);
        Log("Backend translation stopped.");
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
            catch (Exception ex) { Log($"Mute mic error: {ex.Message}"); }
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
            catch (Exception ex) { Log($"Mute speaker error: {ex.Message}"); }
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
                Direction = 3, // Both
                AudioFormat = "Raw16Khz16BitMonoPcm"
            };

            await _connection!.InvokeAsync("StartTranslation", startRequest, _cts!.Token);
            // Actual session ID arrives in the "SessionStarted" callback from hub
        }
        catch (Exception ex)
        {
            Log($"StartBackendSession error: {ex.Message}");
            FireSystemMessage("Session start failed", ex.Message, SystemMessageType.Error);
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
                    Log($"Translation session established: {_translationSessionId}");
                    FireSystemMessage("Translation Started", "", SystemMessageType.Started);

                    // Begin audio capture now that session is confirmed
                    StartAudioCapture();
                }
            }
            catch (Exception ex) { Log($"SessionStarted parse error: {ex.Message}"); }
        });

        // Main event stream from backend
        _connection.On<object>("ReceiveTranslationEvent", payload =>
        {
            try { HandleTranslationEvent(payload); }
            catch (Exception ex) { Log($"Event handling error: {ex.Message}"); }
        });

        // Session stopped by server
        _connection.On<string>("SessionStopped", id =>
        {
            Log($"Server stopped session {id}");
            _translationSessionId = null;
            StopAudioCapture();
            FireSystemMessage("Translation Stopped", "", SystemMessageType.Stopped);
        });

        // Error from server
        _connection.On<string, string>("ReceiveError", (code, message) =>
        {
            Log($"Hub error [{code}]: {message}");
            FireSystemMessage($"Error: {code}", message, SystemMessageType.Error);
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
                // Play synthesized audio locally
                if (audioData != null && audioData.Length > 0)
                    _ = PlayAudioAsync(audioData, direction);
                break;

            case 5: // SynthesizingStarted
                SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                {
                    IsSynthesizing = true,
                    IsFromMeeting = isFromMeeting
                });
                break;

            case 6: // SynthesizingCompleted
                SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                {
                    IsSynthesizing = false,
                    IsFromMeeting = isFromMeeting
                });
                break;

            case 9: // Error
                Log($"Backend pipeline error: {systemMessage}");
                FireSystemMessage("Pipeline error", systemMessage, SystemMessageType.Error);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: Audio capture (mic + loopback → backend)
    // ─────────────────────────────────────────────────────────────────────────

    private void StartAudioCapture()
    {
        StartMicCapture();
        StartLoopbackCapture();
    }

    private void StartMicCapture()
    {
        try
        {
            var physicalMic = _deviceService.FindPhysicalMicrophone();
            _micCapture = new WaveInEvent
            {
                WaveFormat = AudioFormat,
                BufferMilliseconds = 100
            };

            if (physicalMic != null)
                _micCapture.DeviceNumber = GetWaveInDeviceNumber(physicalMic.ID);

            _micCapture.DataAvailable += async (_, e) =>
            {
                if (_isMicMuted || string.IsNullOrEmpty(_translationSessionId)) return;
                if (_connection?.State != HubConnectionState.Connected) return;

                var chunk = BuildChunk(e.Buffer, e.BytesRecorded, 1); // Outgoing = 1
                try { await _connection.InvokeAsync("SendAudioChunk", chunk); }
                catch { /* connection drop — reconnect loop will recover */ }
            };

            _micCapture.StartRecording();
            Log("Microphone capture started.");
        }
        catch (Exception ex)
        {
            Log($"ERROR starting mic capture: {ex.Message}");
        }
    }

    private void StartLoopbackCapture()
    {
        try
        {
            var loopback = _deviceService.FindOutgoingCaptureCableDevice();
            if (loopback == null)
            {
                Log("Warning: loopback device not found — incoming pipeline will receive no audio.");
                return;
            }

            _loopbackCapture = new WaveInEvent
            {
                WaveFormat = AudioFormat,
                BufferMilliseconds = 100,
                DeviceNumber = GetWaveInDeviceNumber(loopback.ID)
            };

            _loopbackCapture.DataAvailable += async (_, e) =>
            {
                if (_isSpeakerMuted || string.IsNullOrEmpty(_translationSessionId)) return;
                if (_connection?.State != HubConnectionState.Connected) return;

                var chunk = BuildChunk(e.Buffer, e.BytesRecorded, 2); // Incoming = 2
                try { await _connection.InvokeAsync("SendAudioChunk", chunk); }
                catch { }
            };

            _loopbackCapture.StartRecording();
            Log("Loopback capture started.");
        }
        catch (Exception ex)
        {
            Log($"ERROR starting loopback capture: {ex.Message}");
        }
    }

    private void StopAudioCapture()
    {
        try { _micCapture?.StopRecording(); _micCapture?.Dispose(); _micCapture = null; } catch { }
        try { _loopbackCapture?.StopRecording(); _loopbackCapture?.Dispose(); _loopbackCapture = null; } catch { }
    }

    private object BuildChunk(byte[] buffer, int bytesRecorded, int pipeline)
    {
        var data = new byte[bytesRecorded];
        Array.Copy(buffer, data, bytesRecorded);
        return new
        {
            TranslationSessionId = _translationSessionId,
            Pipeline = pipeline,
            Data = data,
            SequenceNumber = pipeline == 1
                ? Interlocked.Increment(ref _outgoingSeq)
                : Interlocked.Increment(ref _incomingSeq),
            CapturedAt = DateTime.UtcNow
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: Audio playback (translated audio → physical speaker or CABLE)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task PlayAudioAsync(byte[] pcmData, MessageDirection direction)
    {
        try
        {
            if (direction == MessageDirection.Outgoing)
            {
                // Outgoing translated audio → CABLE device (goes into the meeting)
                var cableDevice = _deviceService.FindOutgoingCableDevice();
                await AudioPlaybackManager.PlayAudioToCableDeviceAsync(pcmData, cableDevice);
            }
            else
            {
                // Incoming translated audio → physical speaker (heard by the user)
                var physicalSpeaker = _deviceService.FindPhysicalSpeaker();
                await AudioPlaybackManager.PlayAudioToPhysicalSpeakerAsync(pcmData, physicalSpeaker);
            }
        }
        catch (Exception ex)
        {
            Log($"Audio playback error: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: Device helpers (WaveIn device number matching)
    // ─────────────────────────────────────────────────────────────────────────

    private static int GetWaveInDeviceNumber(string deviceId)
    {
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            if (deviceId.Contains(caps.ProductName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
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
