using System.Collections.Concurrent;
using System.Text;
using System.Globalization;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Services;

public class LipiTranslationService : ILipiTranslationService, IDisposable
{
    private static readonly TimeSpan TranscriptFlushInterval = TimeSpan.FromSeconds(3);
    private const int TranscriptFlushBatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<LipiTranslationService> _logger;
    private readonly TimeSpan _recognizingEventMinInterval;
    private readonly int _recognizingMinTextLength;
    private readonly int _recognizingMinDeltaLength;
    private readonly int _recognizingPreviewMaxChars;
    private readonly bool _includeRecognizingTranslations;
    private readonly bool _dropOutOfOrderAudioChunks;
    private readonly int _lipiSegmentationSilenceTimeoutMs;
    private readonly TextModerationEngine _moderation;

    private readonly ConcurrentDictionary<string, LipiSessionState> _sessions = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionSessions = new();

    public LipiTranslationService(
        IServiceScopeFactory scopeFactory,
        IJwtTokenService jwtTokenService,
        ILogger<LipiTranslationService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
        _recognizingEventMinInterval = TimeSpan.FromMilliseconds(
            Math.Max(0, configuration.GetValue<int>("Translation:LipiRecognizingMinIntervalMs", 400)));
        _recognizingMinTextLength = Math.Max(0, configuration.GetValue<int>("Translation:LipiRecognizingMinTextLength", 4));
        _recognizingMinDeltaLength = Math.Max(0, configuration.GetValue<int>("Translation:LipiRecognizingMinDeltaLength", 3));
        _recognizingPreviewMaxChars = Math.Max(16, configuration.GetValue<int>("Translation:LipiRecognizingPreviewMaxChars", 72));
        _includeRecognizingTranslations = configuration.GetValue<bool>("Translation:LipiIncludeRecognizingTranslations", true);
        _dropOutOfOrderAudioChunks = configuration.GetValue<bool>("Translation:LipiDropOutOfOrderAudioChunks", true);
        _lipiSegmentationSilenceTimeoutMs = Math.Clamp(
            configuration.GetValue<int>("Translation:LipiSegmentationSilenceTimeoutMs", 700),
            300,
            3000);
        _moderation = new TextModerationEngine(configuration, _logger);
    }

    public async Task<LipiStartResponse> StartSessionAsync(LipiStartRequest request, string jwtToken)
    {
        try
        {
            var (tokenValid, tokenMeetingId, tokenDeviceId) = _jwtTokenService.ValidateToken(jwtToken);
            if (!tokenValid || string.IsNullOrWhiteSpace(tokenMeetingId) || string.IsNullOrWhiteSpace(tokenDeviceId))
                return Fail("INVALID_TOKEN", "Invalid or expired access token.");

            if (!tokenMeetingId.Equals(request.MeetingId, StringComparison.OrdinalIgnoreCase))
                return Fail("TOKEN_MEETING_MISMATCH", "Token does not match requested meeting.");

            if (string.IsNullOrWhiteSpace(request.SessionId) || !int.TryParse(request.SessionId, out var sessionId))
                return Fail("INVALID_REQUEST", "SessionId is required.");

            if (request.TargetLanguages == null || request.TargetLanguages.Count == 0)
                return Fail("INVALID_REQUEST", "At least one target language is required.");

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

            var sourceLanguage = string.IsNullOrWhiteSpace(request.SourceLanguage) ? "en-US" : request.SourceLanguage.Trim();
            var targetLanguages = request.TargetLanguages
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var lipiSessionId = Guid.NewGuid().ToString("N");
            var state = new LipiSessionState
            {
                LipiSessionId = lipiSessionId,
                SessionId = sessionId,
                MeetingId = request.MeetingId,
                ConnectionId = request.ConnectionId ?? string.Empty,
                AzureRegion = meeting.AzureSubscription.Region,
                AzureKey = meeting.AzureSubscription.SubscriptionKey,
                SourceLanguage = sourceLanguage,
                TargetLanguages = targetLanguages,
                StartedAt = DateTime.UtcNow,
                Status = LipiSessionStatus.Starting,
                MeetingValidUntil = meeting.ValidUntil,
                LastTranscriptFlushAt = DateTime.UtcNow
            };

            if (!_sessions.TryAdd(lipiSessionId, state))
                return Fail("SESSION_CREATE_FAILED", "Failed to create Lipi session.");

            if (!string.IsNullOrEmpty(request.ConnectionId))
            {
                _connectionSessions.AddOrUpdate(
                    request.ConnectionId,
                    _ => new HashSet<string> { lipiSessionId },
                    (_, set) => { lock (set) { set.Add(lipiSessionId); } return set; });
            }

            await InitializeRecognizerAsync(state);
            state.AzureKey = string.Empty;
            state.Status = LipiSessionStatus.Running;

            state.FireEvent(new LipiEventDto
            {
                LipiSessionId = state.LipiSessionId,
                EventType = LipiEventType.SessionStarted,
                SystemMessage = "Lipi transcription started"
            });

            _logger.LogInformation("Lipi session started {Id} | {MeetingId} | {Src} -> {Targets}",
                state.LipiSessionId,
                state.MeetingId,
                state.SourceLanguage,
                string.Join(",", state.TargetLanguages));

            return new LipiStartResponse
            {
                Success = true,
                LipiSessionId = lipiSessionId,
                Message = "Lipi session started successfully."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Lipi session for meeting {MeetingId}", request.MeetingId);
            return Fail("INTERNAL_ERROR", "An error occurred while starting Lipi session.");
        }
    }

    public async Task<bool> StopSessionAsync(string lipiSessionId)
    {
        if (!_sessions.TryRemove(lipiSessionId, out var state))
            return false;

        await TeardownStateAsync(state, "stop requested");
        return true;
    }

    public LipiStatusResponse? GetStatus(string lipiSessionId)
    {
        if (!_sessions.TryGetValue(lipiSessionId, out var state))
            return null;

        return new LipiStatusResponse
        {
            LipiSessionId = state.LipiSessionId,
            MeetingId = state.MeetingId,
            SourceLanguage = state.SourceLanguage,
            TargetLanguages = state.TargetLanguages,
            IsRunning = state.Status == LipiSessionStatus.Running,
            StartedAt = state.StartedAt,
            TotalRecognitions = state.TotalRecognitions,
            ErrorCount = state.ErrorCount,
            LastError = state.LastError
        };
    }

    public Task ProcessAudioChunkAsync(string lipiSessionId, LipiAudioChunkDto chunk)
    {
        if (!_sessions.TryGetValue(lipiSessionId, out var state))
            return Task.CompletedTask;

        if (state.Status != LipiSessionStatus.Running || state.PushStream == null)
            return Task.CompletedTask;

        try
        {
            if (!ShouldAcceptChunkSequence(state, chunk.SequenceNumber))
                return Task.CompletedTask;

            state.PushStream.Write(chunk.Data, chunk.Data.Length);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref state._errorCount);
            state.LastError = ex.Message;
            _logger.LogError(ex, "Lipi chunk write failed for session {Id}", lipiSessionId);
        }

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
    }

    public void RegisterEventCallback(string lipiSessionId, Func<LipiEventDto, Task> callback)
    {
        if (_sessions.TryGetValue(lipiSessionId, out var state))
            state.OnEvent = callback;
    }

    public bool IsOwnedByConnection(string lipiSessionId, string connectionId)
    {
        if (_sessions.TryGetValue(lipiSessionId, out var state))
            return state.ConnectionId == connectionId;
        return false;
    }

    private async Task InitializeRecognizerAsync(LipiSessionState state)
    {
        var endpoint = $"wss://{state.AzureRegion}.stt.speech.microsoft.com/speech/universal/v2";
        var config = SpeechTranslationConfig.FromEndpoint(new Uri(endpoint), state.AzureKey);
        config.SpeechRecognitionLanguage = state.SourceLanguage;
        config.OutputFormat = OutputFormat.Detailed;
        config.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, "Removed");
        config.SetProperty(
            PropertyId.Speech_SegmentationSilenceTimeoutMs,
            _lipiSegmentationSilenceTimeoutMs.ToString(CultureInfo.InvariantCulture));
        config.SetProperty(
            PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs,
            _lipiSegmentationSilenceTimeoutMs.ToString(CultureInfo.InvariantCulture));

        var targetMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fullCode in state.TargetLanguages)
        {
            var shortCode = fullCode.Split('-')[0];
            targetMap[fullCode] = shortCode;
            config.AddTargetLanguage(shortCode);
        }

        state.PushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
        var audioConfig = AudioConfig.FromStreamInput(state.PushStream);
        state.Recognizer = new TranslationRecognizer(config, audioConfig);

        state.Recognizer.Recognizing += (_, e) =>
        {
            if (e.Result.Reason != ResultReason.TranslatingSpeech)
                return;

            var originalText = e.Result.Text?.Trim();
            if (string.IsNullOrWhiteSpace(originalText))
                return;

            var moderatedOriginal = _moderation.ModerateText(originalText);
            if (moderatedOriginal.Blocked || string.IsNullOrWhiteSpace(moderatedOriginal.Text))
                return;

            originalText = moderatedOriginal.Text;

            if (originalText.Length < _recognizingMinTextLength)
                return;

            if (!string.IsNullOrEmpty(state.LastRecognizingText) &&
                originalText.StartsWith(state.LastRecognizingText, StringComparison.Ordinal) &&
                originalText.Length - state.LastRecognizingText.Length < _recognizingMinDeltaLength)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (originalText.Equals(state.LastRecognizingText, StringComparison.Ordinal) &&
                now - state.LastRecognizingEventAt < _recognizingEventMinInterval)
            {
                return;
            }

            if (now - state.LastRecognizingEventAt < _recognizingEventMinInterval)
                return;

            state.LastRecognizingEventAt = now;
            state.LastRecognizingText = originalText;

            Dictionary<string, string>? recognizingTranslations = null;
            if (_includeRecognizingTranslations)
            {
                recognizingTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in targetMap)
                {
                    if (e.Result.Translations.TryGetValue(pair.Value, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        var moderatedValue = _moderation.ModerateText(value.Trim());
                        if (!moderatedValue.Blocked && !string.IsNullOrWhiteSpace(moderatedValue.Text))
                            recognizingTranslations[pair.Key] = moderatedValue.Text;
                    }
                }
            }

            state.FireEvent(new LipiEventDto
            {
                LipiSessionId = state.LipiSessionId,
                EventType = LipiEventType.Recognizing,
                OriginalText = originalText,
                Translations = recognizingTranslations,
                PreviewTranslations = BuildPreviewTranslations(recognizingTranslations),
                IsFinal = false
            });
        };

        state.Recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason != ResultReason.TranslatedSpeech || string.IsNullOrWhiteSpace(e.Result.Text))
                return;

            var moderatedOriginal = _moderation.ModerateText(e.Result.Text.Trim());
            if (moderatedOriginal.Blocked || string.IsNullOrWhiteSpace(moderatedOriginal.Text))
            {
                state.FireEvent(new LipiEventDto
                {
                    LipiSessionId = state.LipiSessionId,
                    EventType = LipiEventType.Error,
                    SystemMessage = "A phrase was filtered due to meeting content policy."
                });
                return;
            }

            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in targetMap)
            {
                if (e.Result.Translations.TryGetValue(pair.Value, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    var moderatedTranslation = _moderation.ModerateText(value.Trim());
                    if (moderatedTranslation.Blocked)
                    {
                        state.FireEvent(new LipiEventDto
                        {
                            LipiSessionId = state.LipiSessionId,
                            EventType = LipiEventType.Error,
                            SystemMessage = "A phrase was filtered due to meeting content policy."
                        });
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(moderatedTranslation.Text))
                        translations[pair.Key] = moderatedTranslation.Text;
                }
            }

            Interlocked.Increment(ref state._totalRecognitions);

            QueueTranscriptEntry(
                state,
                moderatedOriginal.Text,
                translations,
                DateTime.UtcNow);

            state.FireEvent(new LipiEventDto
            {
                LipiSessionId = state.LipiSessionId,
                EventType = LipiEventType.Recognized,
                OriginalText = moderatedOriginal.Text,
                Translations = translations,
                PreviewTranslations = translations.Count > 0 ? new Dictionary<string, string>(translations, StringComparer.OrdinalIgnoreCase) : null,
                IsFinal = true
            });

            state.LastRecognizingText = string.Empty;
        };

        state.Recognizer.Canceled += (_, e) =>
        {
            if (e.Reason != CancellationReason.Error)
                return;

            Interlocked.Increment(ref state._errorCount);
            state.LastError = e.ErrorDetails;
            state.FireEvent(new LipiEventDto
            {
                LipiSessionId = state.LipiSessionId,
                EventType = LipiEventType.Error,
                SystemMessage = e.ErrorDetails
            });
        };

        await state.Recognizer.StartContinuousRecognitionAsync();

        state.TranscriptFlushLoopTask = RunTranscriptFlushLoopAsync(state);
    }

    private Dictionary<string, string>? BuildPreviewTranslations(Dictionary<string, string>? translations)
    {
        if (translations == null || translations.Count == 0)
            return null;

        var preview = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in translations)
        {
            var compact = CompactPreviewText(item.Value);
            if (!string.IsNullOrWhiteSpace(compact))
                preview[item.Key] = compact;
        }

        return preview.Count > 0 ? preview : null;
    }

    private string CompactPreviewText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = string.Join(' ', text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= _recognizingPreviewMaxChars)
            return normalized;

        var truncated = normalized[.._recognizingPreviewMaxChars];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace >= _recognizingPreviewMaxChars / 2)
            truncated = truncated[..lastSpace];

        return truncated.TrimEnd(',', ';', ':', '-', ' ') + "…";
    }

    private async Task TeardownStateAsync(LipiSessionState state, string reason)
    {
        state.Status = LipiSessionStatus.Stopping;

        await FlushPendingTranscriptsAsync(state, force: true);

        try { state.Cts.Cancel(); } catch { }

        if (state.TranscriptFlushLoopTask != null)
        {
            try { await state.TranscriptFlushLoopTask; } catch { }
            state.TranscriptFlushLoopTask = null;
        }

        if (state.Recognizer != null)
        {
            try { await state.Recognizer.StopContinuousRecognitionAsync(); } catch { }
            state.Recognizer.Dispose();
            state.Recognizer = null;
        }

        state.PushStream?.Close();
        state.PushStream = null;
        state.Cts.Dispose();

        state.Status = LipiSessionStatus.Stopped;
        state.FireEvent(new LipiEventDto
        {
            LipiSessionId = state.LipiSessionId,
            EventType = LipiEventType.SessionStopped,
            SystemMessage = reason
        });
    }

    private bool ShouldAcceptChunkSequence(LipiSessionState state, long sequenceNumber)
    {
        lock (state.AudioSequenceSync)
        {
            if (!state.HasReceivedAudioChunk)
            {
                state.HasReceivedAudioChunk = true;
                state.LastReceivedSequenceNumber = sequenceNumber;
                return true;
            }

            var expectedSequence = state.LastReceivedSequenceNumber + 1;
            if (sequenceNumber > expectedSequence)
            {
                _logger.LogWarning(
                    "[{Id}] Lipi audio sequence gap detected. Expected {Expected} but received {Actual}.",
                    state.LipiSessionId,
                    expectedSequence,
                    sequenceNumber);

                state.LastReceivedSequenceNumber = sequenceNumber;
                return true;
            }

            if (sequenceNumber <= state.LastReceivedSequenceNumber)
            {
                _logger.LogDebug(
                    "[{Id}] Lipi audio sequence out of order or duplicate. Last {Last}, received {Actual}.",
                    state.LipiSessionId,
                    state.LastReceivedSequenceNumber,
                    sequenceNumber);

                return !_dropOutOfOrderAudioChunks;
            }

            state.LastReceivedSequenceNumber = sequenceNumber;
            return true;
        }
    }

    private static LipiStartResponse Fail(string code, string msg) =>
        new() { Success = false, ErrorCode = code, Message = msg };

    public void Dispose()
    {
        foreach (var id in _sessions.Keys.ToList())
        {
            if (_sessions.TryRemove(id, out var state))
                TeardownStateAsync(state, "service disposed").GetAwaiter().GetResult();
        }

        GC.SuppressFinalize(this);
    }

    private void QueueTranscriptEntry(
        LipiSessionState state,
        string transcript,
        Dictionary<string, string> translations,
        DateTime recognizedAtUtc)
    {
        var entryText = BuildTranscriptEntryText(transcript, translations, recognizedAtUtc);

        var shouldFlush = false;
        lock (state.PendingTranscriptLock)
        {
            state.PendingTranscripts.Add(entryText);
            shouldFlush = state.PendingTranscripts.Count >= TranscriptFlushBatchSize;
        }

        if (shouldFlush)
            _ = FlushPendingTranscriptsAsync(state, force: false);
    }

    private async Task RunTranscriptFlushLoopAsync(LipiSessionState state)
    {
        using var timer = new PeriodicTimer(TranscriptFlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(state.Cts.Token))
            {
                await FlushPendingTranscriptsAsync(state, force: false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task FlushPendingTranscriptsAsync(LipiSessionState state, bool force)
    {
        if (force)
        {
            await state.TranscriptFlushGate.WaitAsync();
        }
        else
        {
            var acquired = await state.TranscriptFlushGate.WaitAsync(0);
            if (!acquired)
                return;
        }

        List<string>? entriesToPersist = null;
        try
        {
            lock (state.PendingTranscriptLock)
            {
                if (state.PendingTranscripts.Count == 0)
                    return;

                entriesToPersist = [.. state.PendingTranscripts];
                state.PendingTranscripts.Clear();
            }

            await PersistTranscriptBatchAsync(state, entriesToPersist);
            state.LastTranscriptFlushAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed persisting transcript batch for Lipi session {LipiSessionId}", state.LipiSessionId);

            if (entriesToPersist is { Count: > 0 })
            {
                lock (state.PendingTranscriptLock)
                {
                    state.PendingTranscripts.InsertRange(0, entriesToPersist);
                }
            }
        }
        finally
        {
            state.TranscriptFlushGate.Release();
        }

        if (force)
        {
            bool hasMore;
            lock (state.PendingTranscriptLock)
                hasMore = state.PendingTranscripts.Count > 0;

            if (hasMore)
                await FlushPendingTranscriptsAsync(state, force: true);
        }
    }

    private async Task PersistTranscriptBatchAsync(LipiSessionState state, List<string> entries)
    {
        if (entries.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VaaniDbContext>();

        var session = await dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == state.SessionId);
        if (session == null)
        {
            _logger.LogWarning(
                "Skipping transcript persistence. Session {SessionId} not found for Lipi session {LipiSessionId}",
                state.SessionId,
                state.LipiSessionId);
            return;
        }

        var payload = string.Join(Environment.NewLine, entries);
        session.SessionTrascript = string.IsNullOrWhiteSpace(session.SessionTrascript)
            ? payload
            : $"{session.SessionTrascript}{Environment.NewLine}{payload}";

        await dbContext.SaveChangesAsync();
    }

    private static string BuildTranscriptEntryText(
        string transcript,
        Dictionary<string, string> translations,
        DateTime recognizedAtUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{recognizedAtUtc:yyyy-MM-dd HH:mm:ss} UTC]");
        sb.AppendLine(transcript);

        if (translations.Count > 0)
        {
            sb.AppendLine("Translations:");
            foreach (var item in translations.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"{item.Key}: {item.Value}");
        }

        return sb.ToString().TrimEnd();
    }
}

internal enum LipiSessionStatus
{
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4
}

internal class LipiSessionState
{
    public string LipiSessionId { get; set; } = string.Empty;
    public int SessionId { get; set; }
    public string MeetingId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;

    public string AzureKey { get; set; } = string.Empty;
    public string AzureRegion { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public List<string> TargetLanguages { get; set; } = [];

    public PushAudioInputStream? PushStream { get; set; }
    public TranslationRecognizer? Recognizer { get; set; }

    public CancellationTokenSource Cts { get; set; } = new();
    public LipiSessionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime MeetingValidUntil { get; set; }
    public DateTime LastTranscriptFlushAt { get; set; }
    public DateTime LastRecognizingEventAt { get; set; }
    public string LastRecognizingText { get; set; } = string.Empty;
    public object AudioSequenceSync { get; } = new();
    public bool HasReceivedAudioChunk { get; set; }
    public long LastReceivedSequenceNumber { get; set; }

    public object PendingTranscriptLock { get; } = new();
    public List<string> PendingTranscripts { get; } = [];
    public SemaphoreSlim TranscriptFlushGate { get; } = new(1, 1);
    public Task? TranscriptFlushLoopTask { get; set; }

    public int _totalRecognitions;
    public int _errorCount;
    public int TotalRecognitions => _totalRecognitions;
    public int ErrorCount => _errorCount;
    public string? LastError { get; set; }

    public Func<LipiEventDto, Task>? OnEvent { get; set; }

    public void FireEvent(LipiEventDto evt)
    {
        var cb = OnEvent;
        if (cb == null) return;
        _ = cb(evt).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
    }
}

