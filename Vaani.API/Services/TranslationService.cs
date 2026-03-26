using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Vaani.API.Data;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Services;

/// <summary>
/// Backend translation service.
/// Manages per-session Azure Cognitive Services lifecycle: recognizers, synthesizers,
/// push-stream audio ingestion, and event emission back to SignalR hub.
/// </summary>
public class TranslationService : ITranslationService, IDisposable
{
    private readonly VaaniDbContext _dbContext;
    private readonly ILogger<TranslationService> _logger;
    private readonly IConfiguration _configuration;

    // Active sessions: translationSessionId -> state
    private readonly ConcurrentDictionary<string, TranslationSessionState> _sessions = new();

    // Map connectionId -> translationSessionIds it owns
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionSessions = new();

    // Configuration limits
    private readonly int _maxConcurrentSessions;

    public TranslationService(
        VaaniDbContext dbContext,
        ILogger<TranslationService> logger,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
        _maxConcurrentSessions = _configuration.GetValue<int>("Translation:MaxConcurrentSessions", 100);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<TranslationStartResponse> StartSessionAsync(
        TranslationStartRequest request,
        string jwtToken)
    {
        try
        {
            // Guard: max sessions
            if (_sessions.Count >= _maxConcurrentSessions)
            {
                _logger.LogWarning("Max concurrent translation sessions reached ({Max})", _maxConcurrentSessions);
                return Fail("MAX_SESSIONS", "Maximum number of concurrent translation sessions reached.");
            }

            // Load meeting + Azure subscription from DB
            var meeting = await _dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .FirstOrDefaultAsync(m => m.MeetingId == request.MeetingId.ToUpperInvariant());

            if (meeting == null || !meeting.IsActive)
                return Fail("MEETING_NOT_FOUND", "Meeting not found or inactive.");

            if (meeting.AzureSubscription == null || !meeting.AzureSubscription.IsActive)
                return Fail("NO_SUBSCRIPTION", "No active Azure subscription assigned to this meeting.");

            var azureKey = meeting.AzureSubscription.SubscriptionKey;
            var azureRegion = meeting.AzureSubscription.Region;

            // Build language config from request or DB defaults
            var sourceLanguage = request.SourceLanguage ?? "en-US";
            var targetLanguage = request.TargetLanguage ?? "hi-IN";
            var sourceVoice = request.SourceVoice ?? "en-US-GuyNeural";
            var targetVoice = request.TargetVoice ?? "hi-IN-SwaraNeural";

            // Create session state
            var translationSessionId = Guid.NewGuid().ToString("N");
            var cts = new CancellationTokenSource();

            var state = new TranslationSessionState
            {
                TranslationSessionId = translationSessionId,
                MeetingId = request.MeetingId,
                ConnectionId = request.ConnectionId ?? string.Empty,
                AzureKey = azureKey,
                AzureRegion = azureRegion,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                SourceVoice = sourceVoice,
                TargetVoice = targetVoice,
                Direction = request.Direction,
                Cts = cts,
                StartedAt = DateTime.UtcNow,
                Status = TranslationSessionStatus.Starting
            };

            if (!_sessions.TryAdd(translationSessionId, state))
                return Fail("SESSION_CREATE_FAILED", "Failed to create translation session.");

            // Track by connection
            if (!string.IsNullOrEmpty(request.ConnectionId))
            {
                _connectionSessions.AddOrUpdate(
                    request.ConnectionId,
                    _ => new HashSet<string> { translationSessionId },
                    (_, set) => { lock (set) { set.Add(translationSessionId); } return set; });
            }

            // Initialize Azure pipelines
            await InitializePipelinesAsync(state);

            state.Status = TranslationSessionStatus.Running;

            _logger.LogInformation(
                "Translation session started: {SessionId} | Meeting: {MeetingId} | {Src} -> {Tgt}",
                translationSessionId, request.MeetingId, sourceLanguage, targetLanguage);

            return new TranslationStartResponse
            {
                Success = true,
                TranslationSessionId = translationSessionId,
                HubUrl = "/hubs/translation",
                Message = "Translation session started successfully."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting translation session for meeting {MeetingId}", request.MeetingId);
            return Fail("INTERNAL_ERROR", "An error occurred while starting the translation session.");
        }
    }

    public async Task<bool> StopSessionAsync(string translationSessionId)
    {
        if (!_sessions.TryRemove(translationSessionId, out var state))
        {
            _logger.LogWarning("StopSession: session not found {Id}", translationSessionId);
            return false;
        }

        await TeardownStateAsync(state, "stop requested");
        return true;
    }

    public TranslationStatusResponse? GetStatus(string translationSessionId)
    {
        if (!_sessions.TryGetValue(translationSessionId, out var state))
            return null;

        return new TranslationStatusResponse
        {
            TranslationSessionId = state.TranslationSessionId,
            MeetingId = state.MeetingId,
            Status = state.Status,
            StartedAt = state.StartedAt,
            IsMicrophoneMuted = state.IsMicrophoneMuted,
            IsSpeakerMuted = state.IsSpeakerMuted,
            TotalRecognitions = state.TotalRecognitions,
            TotalTranslations = state.TotalTranslations,
            ErrorCount = state.ErrorCount,
            LastError = state.LastError
        };
    }

    public Task ProcessAudioChunkAsync(string translationSessionId, AudioChunkDto chunk)
    {
        if (!_sessions.TryGetValue(translationSessionId, out var state))
        {
            _logger.LogWarning("ProcessAudioChunk: session not found {Id}", translationSessionId);
            return Task.CompletedTask;
        }

        if (state.Status != TranslationSessionStatus.Running)
            return Task.CompletedTask;

        try
        {
            if (chunk.Pipeline == AudioPipelineDirection.Outgoing && !state.IsMicrophoneMuted)
                state.OutgoingPushStream!.Write(chunk.Data, chunk.Data.Length);
            else if (chunk.Pipeline == AudioPipelineDirection.Incoming && !state.IsSpeakerMuted)
                state.IncomingPushStream!.Write(chunk.Data, chunk.Data.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing audio chunk for session {Id}", translationSessionId);
        }

        return Task.CompletedTask;
    }

    public Task SetMicrophoneMuteAsync(string translationSessionId, bool muted)
    {
        if (_sessions.TryGetValue(translationSessionId, out var state))
            state.IsMicrophoneMuted = muted;
        return Task.CompletedTask;
    }

    public Task SetSpeakerMuteAsync(string translationSessionId, bool muted)
    {
        if (_sessions.TryGetValue(translationSessionId, out var state))
            state.IsSpeakerMuted = muted;
        return Task.CompletedTask;
    }

    public async Task CleanupConnectionAsync(string connectionId)
    {
        if (!_connectionSessions.TryRemove(connectionId, out var sessionIds))
            return;

        List<string> ids;
        lock (sessionIds) { ids = sessionIds.ToList(); }

        foreach (var sid in ids)
        {
            if (_sessions.TryRemove(sid, out var state))
                await TeardownStateAsync(state, "connection disconnected");
        }

        _logger.LogInformation("Cleaned up {Count} session(s) for connection {ConnId}",
            ids.Count, connectionId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pipeline Initialization
    // ─────────────────────────────────────────────────────────────────────────

    private async Task InitializePipelinesAsync(TranslationSessionState state)
    {
        var bufferMs = _configuration.GetValue<int>("Translation:AudioBufferSizeMs", 100);
        var endpoint = $"wss://{state.AzureRegion}.stt.speech.microsoft.com/speech/universal/v2";

        // ── OUTGOING: user mic → translate → synthesize ──────────────────────
        if (state.Direction == TranslationDirection.Outgoing || state.Direction == TranslationDirection.Both)
        {
            var outConfig = SpeechTranslationConfig.FromEndpoint(new Uri(endpoint), state.AzureKey);
            outConfig.SpeechRecognitionLanguage = state.SourceLanguage;
            var outTargetLang = state.TargetLanguage.Split('-')[0];
            outConfig.AddTargetLanguage(outTargetLang);
            outConfig.OutputFormat = OutputFormat.Detailed;
            ApplyCommonProperties(outConfig);

            state.OutgoingPushStream = AudioInputStream.CreatePushStream(
                AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
            var outAudio = AudioConfig.FromStreamInput(state.OutgoingPushStream);
            var outRecognizer = new TranslationRecognizer(outConfig, outAudio);
            state.OutgoingRecognizer = outRecognizer;

            WireOutgoingEvents(state, outRecognizer);

            await outRecognizer.StartContinuousRecognitionAsync();
            _logger.LogInformation("[{Id}] Outgoing pipeline started ({Src} → {Tgt})",
                state.TranslationSessionId, state.SourceLanguage, state.TargetLanguage);
        }

        // ── INCOMING: meeting audio → translate → synthesize ─────────────────
        if (state.Direction == TranslationDirection.Incoming || state.Direction == TranslationDirection.Both)
        {
            var inConfig = SpeechTranslationConfig.FromEndpoint(new Uri(endpoint), state.AzureKey);
            inConfig.SpeechRecognitionLanguage = state.TargetLanguage;
            var inTargetLang = state.SourceLanguage.Split('-')[0];
            inConfig.AddTargetLanguage(inTargetLang);
            inConfig.OutputFormat = OutputFormat.Detailed;
            ApplyCommonProperties(inConfig);

            state.IncomingPushStream = AudioInputStream.CreatePushStream(
                AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
            var inAudio = AudioConfig.FromStreamInput(state.IncomingPushStream);
            var inRecognizer = new TranslationRecognizer(inConfig, inAudio);
            state.IncomingRecognizer = inRecognizer;

            WireIncomingEvents(state, inRecognizer);

            await inRecognizer.StartContinuousRecognitionAsync();
            _logger.LogInformation("[{Id}] Incoming pipeline started ({Src} → {Tgt})",
                state.TranslationSessionId, state.TargetLanguage, state.SourceLanguage);
        }

        // ── Synthesizer for outgoing TTS ──────────────────────────────────────
        if (state.Direction == TranslationDirection.Outgoing || state.Direction == TranslationDirection.Both)
        {
            var synthConfig = SpeechConfig.FromSubscription(state.AzureKey, state.AzureRegion);
            synthConfig.SpeechSynthesisLanguage = state.TargetLanguage;
            synthConfig.SpeechSynthesisVoiceName = state.TargetVoice;
            synthConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
            state.OutgoingSynthesizer = new SpeechSynthesizer(synthConfig, null);
        }

        // ── Synthesizer for incoming TTS ──────────────────────────────────────
        if (state.Direction == TranslationDirection.Incoming || state.Direction == TranslationDirection.Both)
        {
            var synthConfig = SpeechConfig.FromSubscription(state.AzureKey, state.AzureRegion);
            synthConfig.SpeechSynthesisLanguage = state.SourceLanguage;
            synthConfig.SpeechSynthesisVoiceName = state.SourceVoice;
            synthConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
            state.IncomingSynthesizer = new SpeechSynthesizer(synthConfig, null);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event wiring
    // ─────────────────────────────────────────────────────────────────────────

    private void WireOutgoingEvents(TranslationSessionState state, TranslationRecognizer recognizer)
    {
        recognizer.Recognizing += (_, e) =>
        {
            if (state.IsMicrophoneMuted || e.Result.Reason != ResultReason.TranslatingSpeech) return;
            state.FireEvent(new TranslationEventDto
            {
                TranslationSessionId = state.TranslationSessionId,
                EventType = TranslationEventType.Recognizing,
                Pipeline = AudioPipelineDirection.Outgoing,
                OriginalText = e.Result.Text
            });
        };

        recognizer.Recognized += async (_, e) =>
        {
            if (state.IsMicrophoneMuted) return;
            if (e.Result.Reason != ResultReason.TranslatedSpeech || string.IsNullOrWhiteSpace(e.Result.Text)) return;

            var original = e.Result.Text.Trim();
            var targetLang = state.TargetLanguage.Split('-')[0];
            var translated = e.Result.Translations.TryGetValue(targetLang, out var t) ? t.Trim() : string.Empty;

            Interlocked.Increment(ref state._totalRecognitions);

            state.FireEvent(new TranslationEventDto
            {
                TranslationSessionId = state.TranslationSessionId,
                EventType = TranslationEventType.Recognized,
                Pipeline = AudioPipelineDirection.Outgoing,
                OriginalText = original,
                TranslatedText = translated
            });

            if (!string.IsNullOrWhiteSpace(translated) && state.OutgoingSynthesizer != null)
                await SynthesizeAndFireAsync(state, state.OutgoingSynthesizer, translated,
                    original, AudioPipelineDirection.Outgoing);
        };

        recognizer.Canceled += (_, e) =>
        {
            if (e.Reason == CancellationReason.Error)
            {
                Interlocked.Increment(ref state._errorCount);
                state.LastError = $"Outgoing recognition canceled: {e.ErrorDetails}";
                _logger.LogWarning("[{Id}] Outgoing recognition canceled: {Err}",
                    state.TranslationSessionId, e.ErrorDetails);
                state.FireEvent(new TranslationEventDto
                {
                    TranslationSessionId = state.TranslationSessionId,
                    EventType = TranslationEventType.Error,
                    Pipeline = AudioPipelineDirection.Outgoing,
                    SystemMessage = e.ErrorDetails
                });
            }
        };
    }

    private void WireIncomingEvents(TranslationSessionState state, TranslationRecognizer recognizer)
    {
        recognizer.Recognizing += (_, e) =>
        {
            if (state.IsSpeakerMuted || e.Result.Reason != ResultReason.TranslatingSpeech) return;
            state.FireEvent(new TranslationEventDto
            {
                TranslationSessionId = state.TranslationSessionId,
                EventType = TranslationEventType.Recognizing,
                Pipeline = AudioPipelineDirection.Incoming,
                OriginalText = e.Result.Text
            });
        };

        recognizer.Recognized += async (_, e) =>
        {
            if (state.IsSpeakerMuted) return;
            if (e.Result.Reason != ResultReason.TranslatedSpeech || string.IsNullOrWhiteSpace(e.Result.Text)) return;

            var original = e.Result.Text.Trim();
            var sourceLang = state.SourceLanguage.Split('-')[0];
            var translated = e.Result.Translations.TryGetValue(sourceLang, out var t) ? t.Trim() : string.Empty;

            Interlocked.Increment(ref state._totalRecognitions);

            state.FireEvent(new TranslationEventDto
            {
                TranslationSessionId = state.TranslationSessionId,
                EventType = TranslationEventType.Recognized,
                Pipeline = AudioPipelineDirection.Incoming,
                OriginalText = original,
                TranslatedText = translated
            });

            if (!string.IsNullOrWhiteSpace(translated) && state.IncomingSynthesizer != null)
                await SynthesizeAndFireAsync(state, state.IncomingSynthesizer, translated,
                    original, AudioPipelineDirection.Incoming);
        };

        recognizer.Canceled += (_, e) =>
        {
            if (e.Reason == CancellationReason.Error)
            {
                Interlocked.Increment(ref state._errorCount);
                state.LastError = $"Incoming recognition canceled: {e.ErrorDetails}";
                _logger.LogWarning("[{Id}] Incoming recognition canceled: {Err}",
                    state.TranslationSessionId, e.ErrorDetails);
                state.FireEvent(new TranslationEventDto
                {
                    TranslationSessionId = state.TranslationSessionId,
                    EventType = TranslationEventType.Error,
                    Pipeline = AudioPipelineDirection.Incoming,
                    SystemMessage = e.ErrorDetails
                });
            }
        };
    }

    private async Task SynthesizeAndFireAsync(
        TranslationSessionState state,
        SpeechSynthesizer synthesizer,
        string text,
        string originalText,
        AudioPipelineDirection pipeline)
    {
        try
        {
            state.FireEvent(new TranslationEventDto
            {
                TranslationSessionId = state.TranslationSessionId,
                EventType = TranslationEventType.SynthesizingStarted,
                Pipeline = pipeline
            });

            using var result = await synthesizer.SpeakTextAsync(text);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                Interlocked.Increment(ref state._totalTranslations);
                state.FireEvent(new TranslationEventDto
                {
                    TranslationSessionId = state.TranslationSessionId,
                    EventType = TranslationEventType.AudioOutput,
                    Pipeline = pipeline,
                    OriginalText = originalText,
                    TranslatedText = text,
                    AudioData = result.AudioData
                });

                state.FireEvent(new TranslationEventDto
                {
                    TranslationSessionId = state.TranslationSessionId,
                    EventType = TranslationEventType.SynthesizingCompleted,
                    Pipeline = pipeline
                });
            }
            else
            {
                Interlocked.Increment(ref state._errorCount);
                _logger.LogWarning("[{Id}] Synthesis failed: {Reason}", state.TranslationSessionId, result.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Id}] SpeakText error", state.TranslationSessionId);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Teardown
    // ─────────────────────────────────────────────────────────────────────────

    private async Task TeardownStateAsync(TranslationSessionState state, string reason)
    {
        state.Status = TranslationSessionStatus.Stopping;
        _logger.LogInformation("[{Id}] Tearing down session ({Reason})", state.TranslationSessionId, reason);

        try { state.Cts.Cancel(); } catch { /* ignore */ }

        var stopTasks = new List<Task>();
        if (state.OutgoingRecognizer != null)
            stopTasks.Add(Task.Run(async () =>
            {
                try { await state.OutgoingRecognizer.StopContinuousRecognitionAsync(); } catch { }
            }));
        if (state.IncomingRecognizer != null)
            stopTasks.Add(Task.Run(async () =>
            {
                try { await state.IncomingRecognizer.StopContinuousRecognitionAsync(); } catch { }
            }));

        try { await Task.WhenAll(stopTasks).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }

        state.OutgoingPushStream?.Close();
        state.IncomingPushStream?.Close();
        state.OutgoingRecognizer?.Dispose();
        state.IncomingRecognizer?.Dispose();
        state.OutgoingSynthesizer?.Dispose();
        state.IncomingSynthesizer?.Dispose();
        state.Cts.Dispose();

        state.Status = TranslationSessionStatus.Stopped;

        state.FireEvent(new TranslationEventDto
        {
            TranslationSessionId = state.TranslationSessionId,
            EventType = TranslationEventType.SessionStopped,
            SystemMessage = reason
        });

        _logger.LogInformation("[{Id}] Session teardown complete", state.TranslationSessionId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the hub after session start to wire the event callback for a specific connection.
    /// </summary>
    public void RegisterEventCallback(string translationSessionId, Func<TranslationEventDto, Task> callback)
    {
        if (_sessions.TryGetValue(translationSessionId, out var state))
            state.OnEvent = callback;
    }

    /// <summary>
    /// Returns true if the given session was opened by the specified SignalR connectionId.
    /// </summary>
    public bool IsOwnedByConnection(string translationSessionId, string connectionId)
    {
        if (_sessions.TryGetValue(translationSessionId, out var state))
            return state.ConnectionId == connectionId;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────────────────────

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Tear down all active sessions on host shutdown
        var tasks = _sessions.Keys
            .Select(id => _sessions.TryRemove(id, out var s)
                ? TeardownStateAsync(s, "service disposed")
                : Task.CompletedTask)
            .ToList();

        Task.WhenAll(tasks).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private static void ApplyCommonProperties(SpeechTranslationConfig config)
    {
        config.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, "Masked");
        config.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
    }

    private static TranslationStartResponse Fail(string code, string msg) =>
        new() { Success = false, ErrorCode = code, Message = msg };
}

// ─────────────────────────────────────────────────────────────────────────────
// Per-session state (internal)
// ─────────────────────────────────────────────────────────────────────────────

internal class TranslationSessionState
{
    public string TranslationSessionId { get; set; } = string.Empty;
    public string MeetingId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;

    // Azure config
    public string AzureKey { get; set; } = string.Empty;
    public string AzureRegion { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string SourceVoice { get; set; } = string.Empty;
    public string TargetVoice { get; set; } = string.Empty;
    public TranslationDirection Direction { get; set; }

    // Azure resources
    public PushAudioInputStream? OutgoingPushStream { get; set; }
    public PushAudioInputStream? IncomingPushStream { get; set; }
    public TranslationRecognizer? OutgoingRecognizer { get; set; }
    public TranslationRecognizer? IncomingRecognizer { get; set; }
    public SpeechSynthesizer? OutgoingSynthesizer { get; set; }
    public SpeechSynthesizer? IncomingSynthesizer { get; set; }

    // Lifecycle
    public CancellationTokenSource Cts { get; set; } = new();
    public TranslationSessionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }

    // Mute flags
    public volatile bool IsMicrophoneMuted;
    public volatile bool IsSpeakerMuted;

    // Counters (use Interlocked for thread safety)
    public int _totalRecognitions;
    public int _totalTranslations;
    public int _errorCount;

    public int TotalRecognitions => _totalRecognitions;
    public int TotalTranslations => _totalTranslations;
    public int ErrorCount => _errorCount;
    public string? LastError { get; set; }

    // Callback registered by hub to forward events to SignalR client
    public Func<TranslationEventDto, Task>? OnEvent { get; set; }

    public void FireEvent(TranslationEventDto evt)
    {
        _ = OnEvent?.Invoke(evt);
    }
}
