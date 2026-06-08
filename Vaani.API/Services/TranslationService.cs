using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Vaani.API.Data;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Services;

/// <summary>
/// Backend translation service.
/// Manages per-session Azure Cognitive Services lifecycle: recognizers, synthesizers,
/// push-stream audio ingestion, and event emission back to SignalR hub.
/// </summary>
public class TranslationService : ITranslationService, IDisposable
{
    private const int TtsRatePercent = 10;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<TranslationService> _logger;
    private readonly IConfiguration _configuration;

    // Active sessions: translationSessionId -> state
    private readonly ConcurrentDictionary<string, TranslationSessionState> _sessions = new();

    // Map connectionId -> translationSessionIds it owns
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionSessions = new();

    // Configuration limits
    private readonly int _maxConcurrentSessions;
    private readonly int _maxSessionsPerMeeting;
    private readonly string _contentModerationMode;
    private readonly bool _enableSecondaryContentModeration;
    private readonly List<Regex> _blockedPatterns;
    private readonly string[] _blockedCanonicalTerms;
    private static readonly Regex SecondaryTokenRegex = new(@"[\p{L}\p{M}\p{Nd}_'-]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Terms source is file-only (local dictionaries under Translation:BlockedTermsDirectory)

    public TranslationService(
        IServiceScopeFactory scopeFactory,
        IJwtTokenService jwtTokenService,
        ILogger<TranslationService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
        _configuration = configuration;
        _maxConcurrentSessions = _configuration.GetValue<int>("Translation:MaxConcurrentSessions", 30);
        _maxSessionsPerMeeting = _configuration.GetValue<int>("Translation:MaxSessionsPerMeeting", 5);
        _contentModerationMode = (_configuration.GetValue<string>("Translation:ContentModerationMode", "Remove") ?? "Remove").Trim();
        _enableSecondaryContentModeration = _configuration.GetValue<bool>("Translation:EnableSecondaryContentModeration", true);

        var localTerms = LoadLocalBlockedTerms();
        var mergedTerms = localTerms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _blockedPatterns = mergedTerms
            .Select(BuildBlockedRegex)
            .Where(r => r != null)
            .Cast<Regex>()
            .ToList();

        _blockedCanonicalTerms = mergedTerms
            .Select(CanonicalizeForModeration)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(t => t.Length)
            .ToArray();

        _logger.LogInformation("Content moderation loaded: {Patterns} patterns, mode={Mode}",
            _blockedPatterns.Count, _contentModerationMode);
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
            // Guard: token must be valid and bound to this meeting
            var (tokenValid, tokenMeetingId, tokenDeviceId) = _jwtTokenService.ValidateToken(jwtToken);
            if (!tokenValid || string.IsNullOrWhiteSpace(tokenMeetingId) || string.IsNullOrWhiteSpace(tokenDeviceId))
                return Fail("INVALID_TOKEN", "Invalid or expired access token.");

            if (!tokenMeetingId.Equals(request.MeetingId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Token meeting mismatch. Token={TokenMeetingId}, Request={RequestMeetingId}", tokenMeetingId, request.MeetingId);
                return Fail("TOKEN_MEETING_MISMATCH", "Token does not match requested meeting.");
            }

            // Guard: global max sessions
            if (_sessions.Count >= _maxConcurrentSessions)
            {
                _logger.LogWarning("Max concurrent translation sessions reached ({Max})", _maxConcurrentSessions);
                return Fail("MAX_SESSIONS", "Maximum number of concurrent translation sessions reached.");
            }

            // Guard: per-meeting session cap (prevents one meeting consuming all capacity)
            var meetingSessionCount = _sessions.Values
                .Count(s => s.MeetingId.Equals(request.MeetingId, StringComparison.OrdinalIgnoreCase));
            if (meetingSessionCount >= _maxSessionsPerMeeting)
            {
                _logger.LogWarning("Per-meeting session cap reached for {MeetingId} ({Max})", request.MeetingId, _maxSessionsPerMeeting);
                return Fail("MEETING_SESSION_CAP", "This meeting has reached its maximum number of concurrent sessions.");
            }

            // Session guard: requested SessionId must belong to token's device + meeting
            if (string.IsNullOrWhiteSpace(request.SessionId) || !int.TryParse(request.SessionId, out var sessionId))
                return Fail("INVALID_REQUEST", "SessionId is required.");

            // Load meeting + Azure subscription from DB using a short-lived scope
            Meeting? meeting;
            using (var scope = _scopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<VaaniDbContext>();
                meeting = await dbContext.Meetings
                    .Include(m => m.AzureSubscription)
                    .FirstOrDefaultAsync(m => m.MeetingId == request.MeetingId.ToUpperInvariant());

                var session = await dbContext.Sessions
                    .FirstOrDefaultAsync(s => s.Id == sessionId
                                           && s.MeetingId == request.MeetingId.ToUpperInvariant()
                                           && s.DeviceId == tokenDeviceId
                                           && (s.Status == "Active" || s.Status == "Initial"));

                if (session == null)
                    return Fail("SESSION_ACCESS_DENIED", "Session does not belong to token context.");
            }

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

            var synthesisQueueCapacity = Math.Clamp(
                _configuration.GetValue<int>("Translation:SynthesisQueueCapacity", 2),
                1,
                10);

            var state = new TranslationSessionState(synthesisQueueCapacity)
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
                MeetingValidUntil = meeting.ValidUntil,
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

            // Zero the key from memory now that SDK instances hold their own copy
            state.AzureKey = string.Empty;

            state.Status = TranslationSessionStatus.Running;

            _logger.LogInformation(
                "Translation session started: {SessionId} | Meeting: {MeetingId} | {Src} -> {Tgt}",
                translationSessionId, request.MeetingId, sourceLanguage, targetLanguage);

            return new TranslationStartResponse
            {
                Success = true,
                TranslationSessionId = translationSessionId,
                HubUrl = "/api/hubs/translation",
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
        {
            _logger.LogWarning("[{Id}] Chunk dropped — session status is {Status}", translationSessionId, state.Status);
            return Task.CompletedTask;
        }

        try
        {
            if (chunk.Pipeline == AudioPipelineDirection.Outgoing && !state.IsMicrophoneMuted)
            {
                state.MarkChunkReceived(chunk.Pipeline, chunk.CapturedAt);
                state.OutgoingPushStream!.Write(chunk.Data, chunk.Data.Length);
                if (chunk.SequenceNumber % 50 == 1)
                    _logger.LogDebug("[{Id}] OUT audio write — seq {Seq}, {Bytes}B", translationSessionId, chunk.SequenceNumber, chunk.Data.Length);
            }
            else if (chunk.Pipeline == AudioPipelineDirection.Incoming && !state.IsSpeakerMuted)
            {
                state.MarkChunkReceived(chunk.Pipeline, chunk.CapturedAt);
                state.IncomingPushStream!.Write(chunk.Data, chunk.Data.Length);
                if (chunk.SequenceNumber % 50 == 1)
                    _logger.LogDebug("[{Id}] IN  audio write — seq {Seq}, {Bytes}B", translationSessionId, chunk.SequenceNumber, chunk.Data.Length);
            }
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

    public IReadOnlyList<string> GetStaleSessionIds()
    {
        var now = DateTime.UtcNow;
        return _sessions.Values
            .Where(s => s.Status == TranslationSessionStatus.Running &&
                        s.MeetingValidUntil != default &&
                        now > s.MeetingValidUntil.AddMinutes(15))
            .Select(s => s.TranslationSessionId)
            .ToList();
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
        var endpoint = $"wss://{state.AzureRegion}.stt.speech.microsoft.com/speech/universal/v2";

        // ── OUTGOING: user mic → recognise (→ translate in full mode) ──────────
        if (state.Direction == TranslationDirection.Outgoing ||
            state.Direction == TranslationDirection.Both ||
            state.Direction == TranslationDirection.TranscribeBoth)
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

        // ── INCOMING: meeting audio → recognise (→ translate in full mode) ──────
        if (state.Direction == TranslationDirection.Incoming ||
            state.Direction == TranslationDirection.Both ||
            state.Direction == TranslationDirection.TranscribeBoth)
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

        // ── Synthesizer for outgoing TTS (skipped in TranscribeBoth / bypass mode) ──
        if (state.Direction == TranslationDirection.Outgoing || state.Direction == TranslationDirection.Both)
        {
            var synthConfig = SpeechConfig.FromSubscription(state.AzureKey, state.AzureRegion);
            synthConfig.SpeechSynthesisLanguage = state.TargetLanguage;
            synthConfig.SpeechSynthesisVoiceName = state.TargetVoice;
            synthConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
            state.OutgoingSynthesizer = new SpeechSynthesizer(synthConfig, null);
        }

        // ── Synthesizer for incoming TTS (skipped in TranscribeBoth / bypass mode) ──
        if (state.Direction == TranslationDirection.Incoming || state.Direction == TranslationDirection.Both)
        {
            var synthConfig = SpeechConfig.FromSubscription(state.AzureKey, state.AzureRegion);
            synthConfig.SpeechSynthesisLanguage = state.SourceLanguage;
            synthConfig.SpeechSynthesisVoiceName = state.SourceVoice;
            synthConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
            state.IncomingSynthesizer = new SpeechSynthesizer(synthConfig, null);
        }

        // ── Start per-pipeline synthesis queue consumers ──────────────────────
        // Each consumer serialises synthesis for its pipeline so sentences never overlap.
        if (state.OutgoingSynthesizer != null)
            _ = Task.Run(() => RunSynthesisQueueAsync(
                state, state.OutgoingSynthesisQueue, state.OutgoingSynthesizer,
                AudioPipelineDirection.Outgoing, state.Cts.Token));
        if (state.IncomingSynthesizer != null)
            _ = Task.Run(() => RunSynthesisQueueAsync(
                state, state.IncomingSynthesisQueue, state.IncomingSynthesizer,
                AudioPipelineDirection.Incoming, state.Cts.Token));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event wiring
    // ─────────────────────────────────────────────────────────────────────────

    private void WireOutgoingEvents(TranslationSessionState state, TranslationRecognizer recognizer)
    {
        recognizer.Recognizing += (_, e) =>
        {
            if (state.IsMicrophoneMuted || e.Result.Reason != ResultReason.TranslatingSpeech) return;

            var moderated = ModerateText(e.Result.Text ?? string.Empty);
            if (moderated.Blocked) return; // suppress blocked partials

            if (!string.IsNullOrEmpty(moderated.Text))
                _logger.LogDebug("[{Id}] OUT Recognizing (moderated): {Len} chars", state.TranslationSessionId, moderated.Text.Length);

            state.FireEvent(new TranslationEventDto
            {
                TranslationSessionId = state.TranslationSessionId,
                EventType = TranslationEventType.Recognizing,
                Pipeline = AudioPipelineDirection.Outgoing,
                OriginalText = moderated.Text
            });
        };

        recognizer.Recognized += async (_, e) =>
        {
            try
            {
                var outTrans = e.Result.Translations != null
                    ? string.Join("; ", e.Result.Translations.Select(kv => $"{kv.Key}='{kv.Value}'"))
                    : "none";
                _logger.LogDebug("[{Id}] OUT Recognized event — Reason: {Reason}, Text: '{Text}', Translations: [{Trans}]",
                    state.TranslationSessionId, e.Result.Reason, e.Result.Text, outTrans);

                if (state.IsMicrophoneMuted) return;
                if (e.Result.Reason != ResultReason.TranslatedSpeech || string.IsNullOrWhiteSpace(e.Result.Text)) return;

                var original = e.Result.Text.Trim();
                var targetLang = state.TargetLanguage.Split('-')[0];
                var translated = e.Result.Translations.TryGetValue(targetLang, out var t) ? t.Trim() : string.Empty;

                var moderatedOriginal = ModerateText(original);
                var moderatedTranslated = ModerateText(translated);

                // Block entire utterance if policy says so
                if (moderatedOriginal.Blocked || moderatedTranslated.Blocked)
                {
                    _logger.LogWarning("[{Id}] OUT utterance blocked by content policy", state.TranslationSessionId);
                    state.FireEvent(new TranslationEventDto
                    {
                        TranslationSessionId = state.TranslationSessionId,
                        EventType = TranslationEventType.Error,
                        Pipeline = AudioPipelineDirection.Outgoing,
                        SystemMessage = "A phrase was filtered due to meeting content policy."
                    });
                    return;
                }

                original = moderatedOriginal.Text;
                translated = moderatedTranslated.Text;

                Interlocked.Increment(ref state._totalRecognitions);
                _logger.LogInformation("[{Id}] OUT recognized (moderated): {OrigLen} chars → {TransLen} chars",
                    state.TranslationSessionId, original.Length, translated.Length);

                state.FireEvent(new TranslationEventDto
                {
                    TranslationSessionId = state.TranslationSessionId,
                    EventType = TranslationEventType.Recognized,
                    Pipeline = AudioPipelineDirection.Outgoing,
                    OriginalText = original,
                    TranslatedText = translated
                });

                if (!string.IsNullOrWhiteSpace(translated) &&
                    state.Direction != TranslationDirection.TranscribeBoth)
                    EnqueueLatestSynthesis(state, AudioPipelineDirection.Outgoing, translated, original);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Id}] Outgoing recognition handler error", state.TranslationSessionId);
            }
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

            var moderated = ModerateText(e.Result.Text ?? string.Empty);
            if (moderated.Blocked) return; // suppress blocked partials

            if (!string.IsNullOrEmpty(moderated.Text))
                _logger.LogDebug("[{Id}] IN  Recognizing (moderated): {Len} chars", state.TranslationSessionId, moderated.Text.Length);

            state.FireEvent(new TranslationEventDto
            {
                TranslationSessionId = state.TranslationSessionId,
                EventType = TranslationEventType.Recognizing,
                Pipeline = AudioPipelineDirection.Incoming,
                OriginalText = moderated.Text
            });
        };

        recognizer.Recognized += async (_, e) =>
        {
            try
            {
                var inTrans = e.Result.Translations != null
                    ? string.Join("; ", e.Result.Translations.Select(kv => $"{kv.Key}='{kv.Value}'"))
                    : "none";
                _logger.LogDebug("[{Id}] IN  Recognized event — Reason: {Reason}, Text: '{Text}', Translations: [{Trans}]",
                    state.TranslationSessionId, e.Result.Reason, e.Result.Text, inTrans);

                if (state.IsSpeakerMuted) return;
                if (e.Result.Reason != ResultReason.TranslatedSpeech || string.IsNullOrWhiteSpace(e.Result.Text)) return;

                var original = e.Result.Text.Trim();
                var sourceLang = state.SourceLanguage.Split('-')[0];
                var translated = e.Result.Translations.TryGetValue(sourceLang, out var t) ? t.Trim() : string.Empty;

                var moderatedOriginal = ModerateText(original);
                var moderatedTranslated = ModerateText(translated);

                // Block entire utterance if policy says so
                if (moderatedOriginal.Blocked || moderatedTranslated.Blocked)
                {
                    _logger.LogWarning("[{Id}] IN utterance blocked by content policy", state.TranslationSessionId);
                    state.FireEvent(new TranslationEventDto
                    {
                        TranslationSessionId = state.TranslationSessionId,
                        EventType = TranslationEventType.Error,
                        Pipeline = AudioPipelineDirection.Incoming,
                        SystemMessage = "A phrase was filtered due to meeting content policy."
                    });
                    return;
                }

                original = moderatedOriginal.Text;
                translated = moderatedTranslated.Text;

                Interlocked.Increment(ref state._totalRecognitions);
                _logger.LogInformation("[{Id}] IN recognized (moderated): {OrigLen} chars → {TransLen} chars",
                    state.TranslationSessionId, original.Length, translated.Length);

                state.FireEvent(new TranslationEventDto
                {
                    TranslationSessionId = state.TranslationSessionId,
                    EventType = TranslationEventType.Recognized,
                    Pipeline = AudioPipelineDirection.Incoming,
                    OriginalText = original,
                    TranslatedText = translated
                });

                if (!string.IsNullOrWhiteSpace(translated) &&
                    state.Direction != TranslationDirection.TranscribeBoth)
                    EnqueueLatestSynthesis(state, AudioPipelineDirection.Incoming, translated, original);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Id}] Incoming recognition handler error", state.TranslationSessionId);
            }
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
        SynthesisWorkItem workItem,
        AudioPipelineDirection pipeline)
    {
        if (!state.TryBeginSynthesis(pipeline, workItem.Version))
        {
            _logger.LogDebug("[{Id}] {Pipeline} skipped stale synthesis work item {Version}",
                state.TranslationSessionId,
                pipeline,
                workItem.Version);
            return;
        }

        var synthesisStartedAt = DateTime.UtcNow;

        state.FireEvent(new TranslationEventDto
        {
            TranslationSessionId = state.TranslationSessionId,
            EventType = TranslationEventType.SynthesizingStarted,
            Pipeline = pipeline,
            OriginalText = workItem.OriginalText,
            TranslatedText = workItem.Text,
            CapturedAtUtc = workItem.CapturedAtUtc,
            RecognizedAtUtc = workItem.RecognizedAtUtc,
            SynthesisStartedAtUtc = synthesisStartedAt,
            CaptureToRecognizedMs = GetDurationMs(workItem.CapturedAtUtc, workItem.RecognizedAtUtc),
            RecognitionToSynthesisStartMs = GetDurationMs(workItem.RecognizedAtUtc, synthesisStartedAt)
        });

        try
        {
            var synthesisLanguage = pipeline == AudioPipelineDirection.Incoming
                ? state.SourceLanguage
                : state.TargetLanguage;
            var voiceName = pipeline == AudioPipelineDirection.Incoming
                ? state.SourceVoice
                : state.TargetVoice;
            var ssml = BuildFastSsml(workItem.Text, synthesisLanguage, voiceName);
            var result = await synthesizer.SpeakSsmlAsync(ssml);
            if (result.Reason != ResultReason.SynthesizingAudioCompleted)
            {
                result.Dispose();

                var ssmlMultiplier = BuildFastSsmlMultiplier(workItem.Text, synthesisLanguage, voiceName);
                result = await synthesizer.SpeakSsmlAsync(ssmlMultiplier);
                if (result.Reason != ResultReason.SynthesizingAudioCompleted)
                {
                    result.Dispose();
                    result = await synthesizer.SpeakTextAsync(workItem.Text);
                }
            }

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                Interlocked.Increment(ref state._totalTranslations);

                var audioGeneratedAt = DateTime.UtcNow;
                var captureToRecognizedMs = GetDurationMs(workItem.CapturedAtUtc, workItem.RecognizedAtUtc);
                var recognitionToSynthesisStartMs = GetDurationMs(workItem.RecognizedAtUtc, synthesisStartedAt);
                var synthesisDurationMs = GetDurationMs(synthesisStartedAt, audioGeneratedAt);
                var endToEndLatencyMs = GetDurationMs(workItem.CapturedAtUtc, audioGeneratedAt);

                _logger.LogInformation(
                    "[{Id}] {Pipeline} latency — capture→recognized: {CaptureToRecognizedMs} ms, recognized→synthesis: {RecognitionToSynthesisStartMs} ms, synthesis: {SynthesisDurationMs} ms, end-to-end: {EndToEndLatencyMs} ms",
                    state.TranslationSessionId,
                    pipeline,
                    captureToRecognizedMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a",
                    recognitionToSynthesisStartMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a",
                    synthesisDurationMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a",
                    endToEndLatencyMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a");

                state.FireEvent(new TranslationEventDto
                {
                    TranslationSessionId = state.TranslationSessionId,
                    EventType = TranslationEventType.AudioOutput,
                    Pipeline = pipeline,
                    OriginalText = workItem.OriginalText,
                    TranslatedText = workItem.Text,
                    AudioData = result.AudioData,
                    CapturedAtUtc = workItem.CapturedAtUtc,
                    RecognizedAtUtc = workItem.RecognizedAtUtc,
                    SynthesisStartedAtUtc = synthesisStartedAt,
                    AudioGeneratedAtUtc = audioGeneratedAt,
                    CaptureToRecognizedMs = captureToRecognizedMs,
                    RecognitionToSynthesisStartMs = recognitionToSynthesisStartMs,
                    SynthesisDurationMs = synthesisDurationMs,
                    EndToEndLatencyMs = endToEndLatencyMs
                });

                result.Dispose();
            }
            else
            {
                Interlocked.Increment(ref state._errorCount);
                _logger.LogInformation("[{Id}] {Pipeline} synthesis ended without audio output: {Reason}",
                    state.TranslationSessionId,
                    pipeline,
                    result.Reason);
                result.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Id}] SpeakText error", state.TranslationSessionId);
        }
        finally
        {
            state.EndSynthesis(pipeline, workItem.Version);

            // Always fire SynthesizingCompleted so the client UI is never left stuck.
            state.FireEvent(new TranslationEventDto
            {
                TranslationSessionId = state.TranslationSessionId,
                EventType = TranslationEventType.SynthesizingCompleted,
                Pipeline = pipeline,
                OriginalText = workItem.OriginalText,
                TranslatedText = workItem.Text,
                CapturedAtUtc = workItem.CapturedAtUtc,
                RecognizedAtUtc = workItem.RecognizedAtUtc,
                SynthesisStartedAtUtc = synthesisStartedAt
            });
        }
    }

    private static string BuildFastSsml(string text, string language, string voiceName)
    {
        var safeText = SecurityElement.Escape(text) ?? string.Empty;
        var safeLang = string.IsNullOrWhiteSpace(language) ? "en-US" : language;
        var safeVoice = SecurityElement.Escape(voiceName) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(safeVoice))
            return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{safeLang}'><voice name='{safeVoice}'><prosody rate='+{TtsRatePercent}%'>{safeText}</prosody></voice></speak>";

        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{safeLang}'><prosody rate='+{TtsRatePercent}%'>{safeText}</prosody></speak>";
    }

    private static string BuildFastSsmlMultiplier(string text, string language, string voiceName)
    {
        var safeText = SecurityElement.Escape(text) ?? string.Empty;
        var safeLang = string.IsNullOrWhiteSpace(language) ? "en-US" : language;
        var safeVoice = SecurityElement.Escape(voiceName) ?? string.Empty;
        var speed = 1.0 + (TtsRatePercent / 100.0);
        if (!string.IsNullOrWhiteSpace(safeVoice))
            return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{safeLang}'><voice name='{safeVoice}'><prosody rate='{speed:0.##}'>{safeText}</prosody></voice></speak>";

        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{safeLang}'><prosody rate='{speed:0.##}'>{safeText}</prosody></speak>";
    }

    /// <summary>
    /// Reads from a per-pipeline synthesis channel and processes each sentence sequentially,
    /// ensuring translated audio is never overlapped on the same output device.
    /// </summary>
    private async Task RunSynthesisQueueAsync(
        TranslationSessionState state,
        Channel<SynthesisWorkItem> queue,
        SpeechSynthesizer synthesizer,
        AudioPipelineDirection pipeline,
        CancellationToken ct)
    {
        try
        {
            await foreach (var workItem in queue.Reader.ReadAllAsync(ct))
                await SynthesizeAndFireAsync(state, synthesizer, workItem, pipeline);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Id}] Synthesis queue consumer error", state.TranslationSessionId);
        }
    }

    private void EnqueueLatestSynthesis(
        TranslationSessionState state,
        AudioPipelineDirection pipeline,
        string translatedText,
        string originalText)
    {
        var queue = pipeline == AudioPipelineDirection.Outgoing
            ? state.OutgoingSynthesisQueue
            : state.IncomingSynthesisQueue;

        var version = state.RegisterSynthesisRequest(pipeline);

        var workItem = new SynthesisWorkItem(
            translatedText,
            originalText,
            DateTime.UtcNow,
            state.GetLastCapturedAtUtc(pipeline),
            version);

        var dropped = 0;
        while (queue.Reader.TryRead(out _))
            dropped++;

        if (!queue.Writer.TryWrite(workItem))
        {
            Interlocked.Increment(ref state._errorCount);
            _logger.LogWarning("[{Id}] {Pipeline} synthesis work dropped because the queue is unavailable", state.TranslationSessionId, pipeline);
            return;
        }

        if (dropped > 0)
        {
            _logger.LogInformation("[{Id}] {Pipeline} dropped {Count} stale synthesis item(s) to keep audio current", state.TranslationSessionId, pipeline, dropped);
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

        // Signal synthesis queue consumers to stop
        state.OutgoingSynthesisQueue.Writer.TryComplete();
        state.IncomingSynthesisQueue.Writer.TryComplete();

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

    private void ApplyCommonProperties(SpeechTranslationConfig config)
    {
        config.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, "Removed");
        config.SetProperty(
            "Speech_SegmentationSilenceTimeoutMs",
            Math.Clamp(_configuration.GetValue<int>("Translation:SegmentationSilenceTimeoutMs", 350), 150, 2000)
                .ToString(CultureInfo.InvariantCulture));
        config.SetProperty(
            "SpeechServiceResponse_StablePartialResultThreshold",
            Math.Clamp(_configuration.GetValue<int>("Translation:StablePartialResultThreshold", 1), 1, 5)
                .ToString(CultureInfo.InvariantCulture));
        // TrueText post-processing suppresses low-confidence results and returns
        // TranslatedSpeech with empty Text, which breaks the recognition pipeline.
    }

    private static double? GetDurationMs(DateTime? startedAtUtc, DateTime? finishedAtUtc)
    {
        if (!startedAtUtc.HasValue || !finishedAtUtc.HasValue)
            return null;

        return Math.Max(0, (finishedAtUtc.Value - startedAtUtc.Value).TotalMilliseconds);
    }

    internal static DateTime NormalizeUtc(DateTime value)
    {
        if (value == default)
            return DateTime.UtcNow;

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private (string Text, bool Flagged, bool Blocked) ModerateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (text, false, false);

        var output = text;
        var flagged = false;

        foreach (var regex in _blockedPatterns)
        {
            if (!regex.IsMatch(output))
                continue;

            flagged = true;

            if (_contentModerationMode.Equals("Block", StringComparison.OrdinalIgnoreCase))
                return (string.Empty, true, true);

            if (_contentModerationMode.Equals("Remove", StringComparison.OrdinalIgnoreCase))
            {
                output = regex.Replace(output, string.Empty);
            }
            else if (_contentModerationMode.Equals("Mask", StringComparison.OrdinalIgnoreCase))
            {
                output = regex.Replace(output, "***");
            }
            else // default: Remove
            {
                output = regex.Replace(output, string.Empty);
            }
        }

        if (_enableSecondaryContentModeration && ContainsBlockedCanonicalTerm(output))
        {
            flagged = true;

            if (_contentModerationMode.Equals("Block", StringComparison.OrdinalIgnoreCase))
                return (string.Empty, true, true);

            output = ApplySecondaryTokenModeration(output);
        }

        // normalize spaces after removals
        output = Regex.Replace(output, "\\s+", " ").Trim();
        return (output, flagged, false);
    }

    private string ApplySecondaryTokenModeration(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _blockedCanonicalTerms.Length == 0)
            return text;

        var remove = !_contentModerationMode.Equals("Mask", StringComparison.OrdinalIgnoreCase);
        return SecondaryTokenRegex.Replace(text, m =>
        {
            if (!ContainsBlockedCanonicalTerm(m.Value))
                return m.Value;

            return remove ? string.Empty : "***";
        });
    }

    private bool ContainsBlockedCanonicalTerm(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || _blockedCanonicalTerms.Length == 0)
            return false;

        var canonical = CanonicalizeForModeration(value);
        if (string.IsNullOrWhiteSpace(canonical))
            return false;

        foreach (var term in _blockedCanonicalTerms)
        {
            if (canonical.Contains(term, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string CanonicalizeForModeration(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalized = input.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;

            var mapped = MapConfusableOrLeet(ch);
            if (char.IsLetterOrDigit(mapped))
                sb.Append(char.ToLowerInvariant(mapped));
        }

        return sb.ToString();
    }

    private static char MapConfusableOrLeet(char c)
    {
        return char.ToLowerInvariant(c) switch
        {
            '0' => 'o',
            '1' => 'i',
            '3' => 'e',
            '4' => 'a',
            '5' => 's',
            '7' => 't',
            '@' => 'a',
            '$' => 's',
            _ => c
        };
    }

    private static Regex? BuildBlockedRegex(string term)
    {
        // Keep alphanumeric only; supports terms with punctuation/spaces in source list.
        var canonical = new string(term
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        if (string.IsNullOrWhiteSpace(canonical))
            return null;

        // Build pattern that tolerates separators between letters:
        // e.g. "f*u-c_k", "s e x", "madar-chod".
        var letters = canonical
            .Select(c => Regex.Escape(c.ToString()));
        var fuzzyCore = string.Join("[\\W_]*", letters);

        // allow suffix letters for inflections: sexuality, masturbating, etc.
        var pattern = $@"\b{fuzzyCore}[a-z]*\b";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private IEnumerable<string> LoadLocalBlockedTerms()
    {
        try
        {
            var folder = (_configuration.GetValue<string>("Translation:BlockedTermsDirectory", "Moderation/BlockedTerms")
                ?? "Moderation/BlockedTerms")
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            var basePath = AppContext.BaseDirectory;
            var fullPath = Path.GetFullPath(Path.Combine(basePath, folder));

            if (!Directory.Exists(fullPath))
            {
                _logger.LogWarning("Blocked terms directory not found: {Path}", fullPath);
                return [];
            }

            var files = Directory.GetFiles(fullPath, "*.txt", SearchOption.TopDirectoryOnly);
            var terms = new List<string>();

            foreach (var file in files)
            {
                foreach (var line in File.ReadLines(file))
                {
                    var value = line.Trim();
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    if (value.StartsWith("#", StringComparison.Ordinal)) continue;
                    terms.Add(value);
                }
            }

            _logger.LogInformation("Loaded {Count} local blocked terms from {Files} files", terms.Count, files.Length);
            return terms;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load local blocked terms");
            return [];
        }
    }

    private static TranslationStartResponse Fail(string code, string msg) =>
        new() { Success = false, ErrorCode = code, Message = msg };
}

// ─────────────────────────────────────────────────────────────────────────────
// Per-session state (internal)
// ─────────────────────────────────────────────────────────────────────────────

internal class TranslationSessionState
{
    public TranslationSessionState(int synthesisQueueCapacity)
    {
        OutgoingSynthesisQueue = Channel.CreateBounded<SynthesisWorkItem>(new BoundedChannelOptions(synthesisQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        IncomingSynthesisQueue = Channel.CreateBounded<SynthesisWorkItem>(new BoundedChannelOptions(synthesisQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

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

    // Per-pipeline synthesis queues — ensures sentences are spoken sequentially.
    // Bounded with DropOldest so a fast speaker never causes unbounded memory growth.
    public Channel<SynthesisWorkItem> OutgoingSynthesisQueue { get; }
    public Channel<SynthesisWorkItem> IncomingSynthesisQueue { get; }

    // Lifecycle
    public CancellationTokenSource Cts { get; set; } = new();
    public TranslationSessionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime MeetingValidUntil { get; set; }

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

    private long _lastOutgoingCapturedAtUtcTicks;
    private long _lastIncomingCapturedAtUtcTicks;
    private long _outgoingSynthesisRequestVersion;
    private long _incomingSynthesisRequestVersion;
    public void MarkChunkReceived(AudioPipelineDirection pipeline, DateTime capturedAt)
    {
        var normalized = TranslationService.NormalizeUtc(capturedAt).Ticks;
        if (pipeline == AudioPipelineDirection.Outgoing)
            Interlocked.Exchange(ref _lastOutgoingCapturedAtUtcTicks, normalized);
        else
            Interlocked.Exchange(ref _lastIncomingCapturedAtUtcTicks, normalized);
    }

    public DateTime? GetLastCapturedAtUtc(AudioPipelineDirection pipeline)
    {
        var ticks = pipeline == AudioPipelineDirection.Outgoing
            ? Interlocked.Read(ref _lastOutgoingCapturedAtUtcTicks)
            : Interlocked.Read(ref _lastIncomingCapturedAtUtcTicks);

        return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
    }

    public long RegisterSynthesisRequest(AudioPipelineDirection pipeline)
    {
        return pipeline == AudioPipelineDirection.Outgoing
            ? Interlocked.Increment(ref _outgoingSynthesisRequestVersion)
            : Interlocked.Increment(ref _incomingSynthesisRequestVersion);
    }

    public bool TryBeginSynthesis(AudioPipelineDirection pipeline, long version)
    {
        return IsLatestSynthesisRequest(pipeline, version);
    }

    public void EndSynthesis(AudioPipelineDirection pipeline, long version)
    {
        _ = pipeline;
        _ = version;
    }

    public bool IsLatestSynthesisRequest(AudioPipelineDirection pipeline, long version)
    {
        var latestVersion = pipeline == AudioPipelineDirection.Outgoing
            ? Interlocked.Read(ref _outgoingSynthesisRequestVersion)
            : Interlocked.Read(ref _incomingSynthesisRequestVersion);

        return latestVersion == version;
    }

    public void FireEvent(TranslationEventDto evt)
    {
        var cb = OnEvent;
        if (cb == null) return;
        _ = cb(evt).ContinueWith(
            t => { /* exception logged by caller or swallowed on disconnect */ },
            TaskContinuationOptions.OnlyOnFaulted);
    }
}

internal sealed record SynthesisWorkItem(
    string Text,
    string OriginalText,
    DateTime RecognizedAtUtc,
    DateTime? CapturedAtUtc,
    long Version);
