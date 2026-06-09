using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Lipi.Models;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Lipi.Services;

public class DirectAzureLipiRealtimeClient : ILipiRealtimeClient
{
    private readonly MeetingAuthenticationService _authService;
    private readonly AudioCaptureConfig _audioCaptureConfig;
    private readonly RealtimeConfig _realtimeConfig;
    private readonly LocalTextModerationEngine _moderationEngine;
    private readonly Queue<byte[]> _preSpeechBuffer = new();

    // Pull stream infrastructure (Plan B)
    private MicPullStreamCallback? _pullCallback;
    private PullAudioInputStream? _pullStream;

    // Active recognizer and config (reused across restarts)
    private TranslationRecognizer? _recognizer;
    private SpeechTranslationConfig? _speechConfig;

    // Restart coordination (Plan A safety net)
    private CancellationTokenSource? _restartCts;
    private int _restartCount;
    private bool _isStopping;

    private WaveInEvent? _waveIn;
    private WasapiLoopbackCapture? _loopbackCapture;
    private MMDevice? _loopbackDevice;
    private CancellationTokenSource? _tokenRefreshCts;
    private Task? _tokenRefreshTask;
    private string _sessionToken = string.Empty;
    private string _meetingId = string.Empty;
    private string _sessionId = string.Empty;
    private string _sourceLanguage = string.Empty;
    private string _region = string.Empty;
    private int? _currentInputDeviceNumber;
    private string? _currentOutputDeviceId;
    private AudioSourceMode _currentSourceMode = AudioSourceMode.Microphone;
    private AudioCaptureSelection _currentCaptureSelection;
    private List<string> _targetLanguages = [];
    private DateTime _tokenExpiresAtUtc = DateTime.MinValue;
    private int _remainingTrailingSilenceChunks;
    private bool _speechDetected;
    private Dictionary<string, string> _targetMap = new(StringComparer.OrdinalIgnoreCase);
    private long _capturedChunkCount;
    private long _sentChunkCount;
    private long _silenceDroppedChunkCount;
    private long _queueDroppedChunkCount;
    private long _preRollFlushedChunkCount;
    private long _persistedRecognitionCount;
    private long _tokenRefreshCount;
    private long _tokenRefreshFailureCount;
    private long _rewriteHitCount;
    private long _rewriteMissCount;
    private long _recognizerRestartCount;
    private int _quotaRestartCount;

    // Client-side conversational dictionary: locale ? ordered rules
    private sealed record ConversationalRule(string Formal, string Conversational, string MatchMode, string Domain);

    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforePunctuationRegex = new(@"\s+([,.;:!?])", RegexOptions.Compiled);
    private static readonly Regex RepeatedPunctuationRegex = new(@"([,.;:!?])\1+", RegexOptions.Compiled);

    private Dictionary<string, List<ConversationalRule>> _conversationalDictionary = new(StringComparer.OrdinalIgnoreCase);
    private string _dictionaryVersion = string.Empty;

    // Recognizing throttle state
    private string _lastRecognizingText = string.Empty;
    private DateTime _lastRecognizingRewriteAt = DateTime.MinValue;

    public DirectAzureLipiRealtimeClient(MeetingAuthenticationService authService)
    {
        _authService = authService;
        _audioCaptureConfig = ConfigurationService.Instance.Config.AudioCapture;
        _realtimeConfig = ConfigurationService.Instance.Config.Realtime;
        _moderationEngine = new LocalTextModerationEngine();
    }

    public event Action<string, Dictionary<string, string>>? RecognizingReceived;
    public event Action<string, Dictionary<string, string>>? RecognizedReceived;
    public event Action<string>? ErrorReceived;
    public event Action<bool>? RunningStateChanged;

    public async Task StartAsync(
        string hubUrl,
        string sessionToken,
        string meetingId,
        string sessionId,
        string sourceLanguage,
        IEnumerable<string> targetLanguages,
        AudioCaptureSelection captureSelection)
    {
        await StopAsync();

        _isStopping = false;
        _restartCount = 0;
        _quotaRestartCount = 0;
        _sessionToken = sessionToken;
        _meetingId = meetingId;
        _sessionId = sessionId;
        _sourceLanguage = sourceLanguage;
        _currentCaptureSelection = captureSelection;
        _targetLanguages = targetLanguages
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var directAccess = await _authService.GetDirectSpeechTokenAsync(
            meetingId,
            sessionId,
            sourceLanguage,
            _targetLanguages,
            sessionToken);

        if (!directAccess.Success || string.IsNullOrWhiteSpace(directAccess.SpeechToken) || string.IsNullOrWhiteSpace(directAccess.Region))
        {
            ErrorReceived?.Invoke(directAccess.Message ?? "Failed to acquire Azure Speech token.");
            RunningStateChanged?.Invoke(false);
            return;
        }

        _region = directAccess.Region;
        _tokenExpiresAtUtc = directAccess.ExpiresAtUtc;
        ResetDiagnostics();

        await LoadConversationalDictionaryAsync();

        _speechConfig = BuildSpeechTranslationConfig(directAccess.SpeechToken, directAccess.Region);

        _targetMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fullCode in directAccess.TargetLanguages)
        {
            var shortCode = fullCode.Split('-')[0];
            _targetMap[fullCode] = shortCode;
            _speechConfig.AddTargetLanguage(shortCode);
        }

        // Plan B: create pull stream + start capture before recognizer
        var queueCapacityBytes = ComputePullQueueCapacityBytes();
        _pullCallback = new MicPullStreamCallback(queueCapacityBytes, OnQueueDropped);
        _pullStream = AudioInputStream.CreatePullStream(_pullCallback, AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));

        StartCapture(captureSelection);

        await StartRecognizerAsync(_speechConfig, _pullStream);

        EnsureTokenRefreshStarted();
        LogDiagnostics("Direct Azure realtime started");
        RunningStateChanged?.Invoke(true);
    }

    public async Task StopAsync()
    {
        _isStopping = true;

        // Cancel any pending auto-restart
        var restartCts = _restartCts;
        _restartCts = null;
        try { restartCts?.Cancel(); } catch { }

        StopCapture();
        await StopTokenRefreshAsync();
        await TeardownRecognizerAsync();

        // Tear down pull stream after recognizer is gone
        _pullStream?.Dispose();
        _pullStream = null;
        _pullCallback?.Dispose();
        _pullCallback = null;
        _speechConfig = null;

        ResetSilenceFilterState();
        LogDiagnostics("Direct Azure realtime stopped");
        RunningStateChanged?.Invoke(false);
    }

    public Task SwitchCaptureDeviceAsync(AudioCaptureSelection captureSelection)
    {
        var sameSelection = _currentSourceMode == captureSelection.SourceMode
            && _currentInputDeviceNumber == captureSelection.InputDeviceNumber
            && string.Equals(_currentOutputDeviceId, captureSelection.OutputDeviceId, StringComparison.OrdinalIgnoreCase);

        if (sameSelection && ((_waveIn != null && captureSelection.SourceMode == AudioSourceMode.Microphone)
            || (_loopbackCapture != null && captureSelection.SourceMode == AudioSourceMode.Speaker)))
            return Task.CompletedTask;

        _currentCaptureSelection = captureSelection;

        if (_pullCallback != null)
            StartCapture(captureSelection);

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Recognizer event handlers
    // -------------------------------------------------------------------------

    private void OnRecognizing(object? sender, TranslationRecognitionEventArgs e)
    {
        if (e.Result.Reason != ResultReason.TranslatingSpeech)
            return;

        var originalText = e.Result.Text?.Trim();
        if (string.IsNullOrWhiteSpace(originalText))
            return;

        var moderatedOriginal = _moderationEngine.Moderate(originalText, [_sourceLanguage]);
        if (string.IsNullOrWhiteSpace(moderatedOriginal.Text))
            return;

        var translations = _moderationEngine.ModerateTranslations(BuildTranslations(e.Result.Translations));

        if (_realtimeConfig.DirectEnableConversationalRewrite &&
            _realtimeConfig.DirectRecognizingRewriteEnabled &&
            originalText.Length >= _realtimeConfig.DirectRecognizingRewriteMinTextLength &&
            !originalText.Equals(_lastRecognizingText, StringComparison.Ordinal) &&
            (DateTime.UtcNow - _lastRecognizingRewriteAt).TotalMilliseconds >= _realtimeConfig.DirectRecognizingRewriteMinIntervalMs)
        {
            _lastRecognizingText = originalText;
            _lastRecognizingRewriteAt = DateTime.UtcNow;
            translations = ApplyConversationalRewrite(translations, out _);
        }

        RecognizingReceived?.Invoke(moderatedOriginal.Text, translations);
    }

    private void OnRecognized(object? sender, TranslationRecognitionEventArgs e)
    {
        if (e.Result.Reason != ResultReason.TranslatedSpeech || string.IsNullOrWhiteSpace(e.Result.Text))
            return;

        var transcript = e.Result.Text.Trim();
        var moderatedTranscript = _moderationEngine.Moderate(transcript, [_sourceLanguage]);
        if (string.IsNullOrWhiteSpace(moderatedTranscript.Text))
            return;

        var baseTranslations = BuildTranslations(e.Result.Translations);

        _ = Task.Run(async () =>
        {
            var anyRuleMatched = false;
            var translations = _realtimeConfig.DirectEnableConversationalRewrite
                ? ApplyConversationalRewrite(baseTranslations, out anyRuleMatched)
                : baseTranslations;

            if (_realtimeConfig.DirectAiFallbackEnabled && !anyRuleMatched)
            {
                Interlocked.Increment(ref _rewriteMissCount);
                translations = await ApplyFallbackRewriteAsync(moderatedTranscript.Text, translations);
            }

            var moderatedTranslations = _moderationEngine.ModerateTranslations(translations);
            RecognizedReceived?.Invoke(moderatedTranscript.Text, moderatedTranslations);
            await PersistRecognizedAsync(moderatedTranscript.Text, moderatedTranslations, DateTime.UtcNow);
        });
    }



    private async Task StartRecognizerAsync(SpeechTranslationConfig config, PullAudioInputStream pullStream)
    {
        var audioConfig = AudioConfig.FromStreamInput(pullStream);
        var recognizer = new TranslationRecognizer(config, audioConfig);

        recognizer.Recognizing += OnRecognizing;
        recognizer.Recognized += OnRecognized;
        recognizer.Canceled += OnCanceled;

        await recognizer.StartContinuousRecognitionAsync();
        _recognizer = recognizer;
        ResetSilenceFilterState();
    }

    private async Task TeardownRecognizerAsync()
    {
        var recognizer = _recognizer;
        _recognizer = null;

        if (recognizer == null)
            return;

        recognizer.Recognizing -= OnRecognizing;
        recognizer.Recognized -= OnRecognized;
        recognizer.Canceled -= OnCanceled;

        try { await recognizer.StopContinuousRecognitionAsync(); } catch { }
        recognizer.Dispose();
    }

    // -------------------------------------------------------------------------
    // Plan A: Auto-recovery on Canceled
    // -------------------------------------------------------------------------

    private enum CanceledErrorKind { BufferOverflow, QuotaExceeded, ConnectionClosed, NonRecoverable }

    private static CanceledErrorKind ClassifyError(string details, int errorCode)
    {
        // Buffer overflow — our pull-stream queue should prevent this, but keep as safety net
        if (details.Contains("client buffer", StringComparison.OrdinalIgnoreCase)
            || details.Contains("maximum size", StringComparison.OrdinalIgnoreCase)
            || details.Contains("Resetting the buffer", StringComparison.OrdinalIgnoreCase))
            return CanceledErrorKind.BufferOverflow;

        // Azure quota / concurrent session limit (error code 1007 or 4429)
        if (errorCode == 1007 || errorCode == 4429
            || details.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || details.Contains("Quota exceeded", StringComparison.OrdinalIgnoreCase)
            || details.Contains("session limit", StringComparison.OrdinalIgnoreCase))
            return CanceledErrorKind.QuotaExceeded;

        // Transient connection drop — remote closed WebSocket but not a quota issue
        if (errorCode is 1000 or 1001 or 1006 or 1011
            || details.Contains("Connection was closed", StringComparison.OrdinalIgnoreCase)
            || details.Contains("remote host", StringComparison.OrdinalIgnoreCase))
            return CanceledErrorKind.ConnectionClosed;

        return CanceledErrorKind.NonRecoverable;
    }

    private void OnCanceled(object? sender, TranslationRecognitionCanceledEventArgs e)
    {
        if (e.Reason != CancellationReason.Error)
            return;

        var details = e.ErrorDetails ?? string.Empty;
        var errorCode = (int)e.ErrorCode;
        var kind = ClassifyError(details, errorCode);

        LogDiagnostics($"Recognizer canceled kind={kind} code={errorCode}: {details}");

        if (_isStopping)
            return;

        switch (kind)
        {
            case CanceledErrorKind.BufferOverflow:
            case CanceledErrorKind.ConnectionClosed:
            {
                var maxRestarts = Math.Max(1, _realtimeConfig.DirectMaxRecognizerRestarts);
                if (_restartCount < maxRestarts)
                {
                    Interlocked.Increment(ref _restartCount);
                    Interlocked.Increment(ref _recognizerRestartCount);
                    var delayMs = Math.Max(100, _realtimeConfig.DirectRecognizerRestartDelayMs);
                    LogDiagnostics($"{kind} — scheduling restart #{_restartCount} in {delayMs}ms");
                    ScheduleRecognizerRestart(delayMs);
                }
                else
                {
                    LogDiagnostics($"{kind} — restart limit reached, surfacing error");
                    ErrorReceived?.Invoke($"Direct Azure speech error: {details}");
                }
                break;
            }

            case CanceledErrorKind.QuotaExceeded:
            {
                var maxQuotaRestarts = Math.Max(1, _realtimeConfig.DirectMaxQuotaRestarts);
                if (_quotaRestartCount < maxQuotaRestarts)
                {
                    // Exponential backoff: 3s ? 6s ? 12s
                    var baseMs = Math.Max(1000, _realtimeConfig.DirectQuotaRetryBaseDelayMs);
                    var delayMs = baseMs * (int)Math.Pow(2, _quotaRestartCount);
                    _quotaRestartCount++;
                    Interlocked.Increment(ref _recognizerRestartCount);
                    LogDiagnostics($"Quota exceeded — scheduling restart #{_quotaRestartCount} with {delayMs}ms backoff");
                    ScheduleRecognizerRestart(delayMs);
                }
                else
                {
                    LogDiagnostics("Quota exceeded — retry limit reached, surfacing error");
                    ErrorReceived?.Invoke($"Azure Speech quota exceeded. Check active session limits, pricing tier, or usage quota. Direct Azure speech error: {details}");
                }
                break;
            }

            case CanceledErrorKind.NonRecoverable:
            default:
                LogDiagnostics($"Recognizer canceled (non-recoverable): {details}");
                ErrorReceived?.Invoke($"Direct Azure speech error: {details}");
                break;
        }
    }

    private void ScheduleRecognizerRestart(int delayMs)
    {
        // Cancel any previous pending restart
        var previousCts = _restartCts;
        _restartCts = null;
        try { previousCts?.Cancel(); } catch { }
        previousCts?.Dispose();

        var cts = new CancellationTokenSource();
        _restartCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, cts.Token);

                if (cts.Token.IsCancellationRequested || _isStopping)
                    return;

                await RestartRecognizerAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogDiagnostics($"Recognizer restart task threw: {ex.Message}");
            }
        }, cts.Token);
    }

    private async Task RestartRecognizerAsync()
    {
        if (_isStopping || _speechConfig == null || _pullStream == null)
            return;

        LogDiagnostics("Restarting recognizer (pull stream retained, capture continues)");

        // Tear down the dead recognizer only — capture keeps running into the pull queue
        await TeardownRecognizerAsync();

        // Always refresh token before restart — quota errors often mean the old token is tainted
        await RefreshSpeechTokenAsync(CancellationToken.None);

        if (_isStopping)
            return;

        // Wait for Azure to release the server-side WebSocket slot from the previous session.
        // Azure's release is async on their side — opening a new recognizer too quickly causes
        // two sessions to overlap briefly, which triggers quota errors on the same key.
        // 2s is sufficient for most regions; quota retries already add their own backoff on top.
        await Task.Delay(2000);

        if (_isStopping)
            return;

        // Re-wire a new recognizer to the same pull stream — no audio is lost
        await StartRecognizerAsync(_speechConfig, _pullStream);
        ResetSilenceFilterState();
        LogDiagnostics($"Recognizer restarted successfully (total restarts: {Interlocked.Read(ref _recognizerRestartCount)})");
    }

    private async Task PersistRecognizedAsync(string transcript, Dictionary<string, string> translations, DateTime recognizedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(_sessionToken) || string.IsNullOrWhiteSpace(_meetingId) || string.IsNullOrWhiteSpace(_sessionId))
            return;

        try
        {
            var response = await _authService.SubmitDirectTranscriptBatchAsync(new LipiDirectTranscriptBatchRequest
            {
                MeetingId = _meetingId,
                SessionId = _sessionId,
                SourceLanguage = _sourceLanguage,
                TargetLanguages = [.. _targetLanguages],
                Entries =
                [
                    new LipiDirectTranscriptEntry
                    {
                        OriginalText = transcript,
                        Translations = new Dictionary<string, string>(translations, StringComparer.OrdinalIgnoreCase),
                        RecognizedAtUtc = recognizedAtUtc
                    }
                ]
            }, _sessionToken);

            if (response.Success)
            {
                var persisted = Interlocked.Increment(ref _persistedRecognitionCount);
                if (_audioCaptureConfig.EnableDiagnostics && persisted % 25 == 0)
                    LogDiagnostics($"Persisted {persisted} direct recognition batches");
            }
            else
            {
                LogDiagnostics($"Direct transcript persistence failed: {response.Message}");
            }
        }
        catch
        {
        }
    }

    private Dictionary<string, string> BuildTranslations(IReadOnlyDictionary<string, string> translations)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _targetMap)
        {
            if (translations.TryGetValue(pair.Value, out var value) && !string.IsNullOrWhiteSpace(value))
                result[pair.Key] = value.Trim();
        }

        return result;
    }

    private Dictionary<string, string> ApplyConversationalRewrite(Dictionary<string, string> translations, out bool anyRuleMatched)
    {
        anyRuleMatched = false;
        if (_conversationalDictionary.Count == 0)
            return ApplyGenericCleanup(translations);

        var result = new Dictionary<string, string>(translations, StringComparer.OrdinalIgnoreCase);
        foreach (var locale in result.Keys.ToList())
        {
            if (!_conversationalDictionary.TryGetValue(locale, out var rules))
            {
                result[locale] = ApplyGenericCleanup(result[locale]);
                continue;
            }

            var text = ApplyGenericCleanup(result[locale]);
            foreach (var rule in rules)
            {
                var replaced = rule.MatchMode switch
                {
                    "Exact" => text.Equals(rule.Formal, StringComparison.OrdinalIgnoreCase)
                        ? rule.Conversational
                        : text,
                    "StartsWith" => text.StartsWith(rule.Formal, StringComparison.OrdinalIgnoreCase)
                        ? rule.Conversational + text[rule.Formal.Length..]
                        : text,
                    _ => text.Replace(rule.Formal, rule.Conversational, StringComparison.OrdinalIgnoreCase)
                };

                if (!replaced.Equals(text, StringComparison.Ordinal))
                {
                    anyRuleMatched = true;
                    Interlocked.Increment(ref _rewriteHitCount);
                    text = replaced;
                }
            }

            result[locale] = ApplyGenericCleanup(text);
        }

        return result;
    }

    private Dictionary<string, string> ApplyGenericCleanup(Dictionary<string, string> translations)
    {
        if (!_realtimeConfig.DirectGenericCleanupEnabled || translations.Count == 0)
            return translations;

        return translations.ToDictionary(
            pair => pair.Key,
            pair => ApplyGenericCleanup(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private string ApplyGenericCleanup(string text)
    {
        if (!_realtimeConfig.DirectGenericCleanupEnabled || string.IsNullOrWhiteSpace(text))
            return text;

        var cleaned = text.Trim();
        cleaned = MultiWhitespaceRegex.Replace(cleaned, " ");
        cleaned = SpaceBeforePunctuationRegex.Replace(cleaned, "$1");
        cleaned = RepeatedPunctuationRegex.Replace(cleaned, "$1");
        cleaned = CollapseRepeatedWords(cleaned);
        return cleaned.Trim();
    }

    private static string CollapseRepeatedWords(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return text;

        var collapsed = new List<string>(parts.Length);
        string? previous = null;
        foreach (var part in parts)
        {
            if (previous != null && previous.Equals(part, StringComparison.OrdinalIgnoreCase))
                continue;

            collapsed.Add(part);
            previous = part;
        }

        return string.Join(' ', collapsed);
    }

    private async Task<Dictionary<string, string>> ApplyFallbackRewriteAsync(string originalText, Dictionary<string, string> translations)
    {
        if (!_realtimeConfig.DirectAiFallbackEnabled ||
            string.IsNullOrWhiteSpace(_sessionToken) ||
            string.IsNullOrWhiteSpace(_meetingId) ||
            string.IsNullOrWhiteSpace(_sessionId) ||
            translations.Count == 0)
        {
            return translations;
        }

        try
        {
            var response = await _authService.RewriteTranslationsAsync(new LipiDirectConversationalRewriteRequest
            {
                MeetingId = _meetingId,
                SessionId = _sessionId,
                SourceLanguage = _sourceLanguage,
                Domain = _realtimeConfig.DirectConversationalRewriteDomain,
                OriginalText = originalText,
                Translations = new Dictionary<string, string>(translations, StringComparer.OrdinalIgnoreCase)
            }, _sessionToken);

            if (!response.Success || !response.Applied || response.Translations.Count == 0)
                return translations;

            LogDiagnostics("Applied AI fallback conversational rewrite");
            return ApplyGenericCleanup(response.Translations);
        }
        catch (Exception ex)
        {
            LogDiagnostics($"AI fallback rewrite failed: {ex.Message}");
            return translations;
        }
    }

    private async Task LoadConversationalDictionaryAsync()
    {
        if (!_realtimeConfig.DirectEnableConversationalRewrite || _targetLanguages.Count == 0)
            return;

        try
        {
            var response = await _authService.GetConversationalDictionaryAsync(_targetLanguages, _realtimeConfig.DirectConversationalRewriteDomain, _sessionToken);
            if (!response.Success || response.Entries.Count == 0)
            {
                LogDiagnostics("Conversational dictionary not loaded — server returned empty or failure");
                return;
            }

            var dict = new Dictionary<string, List<ConversationalRule>>(StringComparer.OrdinalIgnoreCase);
            foreach (var locale in response.Entries)
            {
                dict[locale.Key] = locale.Value
                    .Where(e => !string.IsNullOrWhiteSpace(e.FormalText) && !string.IsNullOrWhiteSpace(e.ConversationalText))
                    .Select(e => new ConversationalRule(
                        e.FormalText.Trim(),
                        e.ConversationalText.Trim(),
                        e.MatchMode?.Trim() ?? "Contains",
                        string.IsNullOrWhiteSpace(e.Domain) ? "general" : e.Domain.Trim()))
                    .OrderBy(e => e.MatchMode.Equals("Exact", StringComparison.OrdinalIgnoreCase) ? 0 : e.MatchMode.Equals("StartsWith", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                    .ThenByDescending(e => e.Formal.Length)
                    .ToList();
            }

            _conversationalDictionary = dict;
            _dictionaryVersion = response.DictionaryVersion;
            LogDiagnostics($"Conversational dictionary loaded v={_dictionaryVersion} locales={_conversationalDictionary.Count} entries={_conversationalDictionary.Values.Sum(l => l.Count)}");
        }
        catch (Exception ex)
        {
            LogDiagnostics($"Conversational dictionary load failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Capture
    // -------------------------------------------------------------------------

    private void StartCapture(AudioCaptureSelection captureSelection)
    {
        StopCapture();

        _currentSourceMode = captureSelection.SourceMode;
        _currentInputDeviceNumber = captureSelection.InputDeviceNumber;
        _currentOutputDeviceId = captureSelection.OutputDeviceId;

        if (captureSelection.SourceMode == AudioSourceMode.Speaker)
        {
            StartLoopbackCapture(captureSelection.OutputDeviceId);
            return;
        }

        if (!captureSelection.InputDeviceNumber.HasValue)
            throw new InvalidOperationException("No microphone device selected.");

        _waveIn = new WaveInEvent
        {
            DeviceNumber = captureSelection.InputDeviceNumber.Value,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = Math.Clamp(_realtimeConfig.DirectAudioBufferMilliseconds, 20, 100)
        };

        _waveIn.DataAvailable += (_, e) =>
        {
            if (_pullCallback == null)
                return;

            var bytes = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, bytes, 0, e.BytesRecorded);

            Interlocked.Increment(ref _capturedChunkCount);

            var chunks = GetChunksToSend(bytes);
            if (chunks.Count == 0)
            {
                Interlocked.Increment(ref _silenceDroppedChunkCount);
                return;
            }

            foreach (var chunk in chunks)
            {
                var sent = _pullCallback.Enqueue(chunk);
                if (sent)
                    Interlocked.Increment(ref _sentChunkCount);
            }

            if (_audioCaptureConfig.EnableDiagnostics)
            {
                var sent = Interlocked.Read(ref _sentChunkCount);
                if (sent > 0 && sent % 100 == 0)
                    LogDiagnostics($"Enqueued {sent} chunks into pull queue");
            }
        };

        _waveIn.StartRecording();
    }

    private void StartLoopbackCapture(string? outputDeviceId)
    {
        if (_pullCallback == null)
            return;

        using var enumerator = new MMDeviceEnumerator();
        _loopbackDevice = string.IsNullOrWhiteSpace(outputDeviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
            : enumerator.GetDevice(outputDeviceId);

        _currentOutputDeviceId = _loopbackDevice.ID;
        _loopbackCapture = new WasapiLoopbackCapture(_loopbackDevice);
        var captureFormat = _loopbackCapture.WaveFormat;

        _loopbackCapture.DataAvailable += (_, e) =>
        {
            if (_pullCallback == null)
                return;

            var pcm = AudioPcmConverter.ToTarget16kHz(e.Buffer, e.BytesRecorded, captureFormat);
            if (pcm.Length == 0)
                return;

            Interlocked.Increment(ref _capturedChunkCount);

            var chunks = GetChunksToSend(pcm);
            if (chunks.Count == 0)
            {
                Interlocked.Increment(ref _silenceDroppedChunkCount);
                return;
            }

            foreach (var chunk in chunks)
            {
                var sent = _pullCallback.Enqueue(chunk);
                if (sent)
                    Interlocked.Increment(ref _sentChunkCount);
            }

            if (_audioCaptureConfig.EnableDiagnostics)
            {
                var sent = Interlocked.Read(ref _sentChunkCount);
                if (sent > 0 && sent % 100 == 0)
                    LogDiagnostics($"Enqueued {sent} loopback chunks into pull queue");
            }
        };

        _loopbackCapture.StartRecording();
    }

    private void StopCapture()
    {
        try { _waveIn?.StopRecording(); } catch { }
        _waveIn?.Dispose();
        _waveIn = null;

        try { _loopbackCapture?.StopRecording(); } catch { }
        _loopbackCapture?.Dispose();
        _loopbackCapture = null;
        _loopbackDevice?.Dispose();
        _loopbackDevice = null;

        _currentInputDeviceNumber = null;
        _currentOutputDeviceId = null;
        _currentSourceMode = AudioSourceMode.Microphone;
        ResetSilenceFilterState();
    }

    private void OnQueueDropped()
    {
        Interlocked.Increment(ref _queueDroppedChunkCount);
        LogDiagnostics($"Pull queue full — dropped oldest chunk (total drops: {Interlocked.Read(ref _queueDroppedChunkCount)})");
    }

    private IReadOnlyList<byte[]> GetChunksToSend(byte[] bytes)
    {
        if (_realtimeConfig.DirectDisableClientSilenceFilter)
            return [bytes];

        if (!_audioCaptureConfig.EnableSilenceFiltering)
            return [bytes];

        var rmsLevel = CalculateRootMeanSquareLevel(bytes);
        var threshold = Math.Max(0, _audioCaptureConfig.SilenceThresholdLevel);
        if (rmsLevel >= threshold)
        {
            var chunksToSend = new List<byte[]>(_preSpeechBuffer.Count + 1);
            if (!_speechDetected)
            {
                while (_preSpeechBuffer.Count > 0)
                {
                    chunksToSend.Add(_preSpeechBuffer.Dequeue());
                    Interlocked.Increment(ref _preRollFlushedChunkCount);
                }
            }

            _speechDetected = true;
            _remainingTrailingSilenceChunks = Math.Max(0, _audioCaptureConfig.TrailingSilenceChunks);
            chunksToSend.Add(bytes);
            return chunksToSend;
        }

        if (_speechDetected && _remainingTrailingSilenceChunks > 0)
        {
            _remainingTrailingSilenceChunks--;
            return [bytes];
        }

        if (_speechDetected)
            _speechDetected = false;

        BufferPreSpeechChunk(bytes);
        return [];
    }

    private void BufferPreSpeechChunk(byte[] bytes)
    {
        var maxChunks = Math.Max(0, _audioCaptureConfig.PreRollChunks);
        if (maxChunks == 0)
        {
            _preSpeechBuffer.Clear();
            return;
        }

        _preSpeechBuffer.Enqueue(bytes);
        while (_preSpeechBuffer.Count > maxChunks)
            _preSpeechBuffer.Dequeue();
    }

    private void ResetSilenceFilterState()
    {
        _remainingTrailingSilenceChunks = 0;
        _speechDetected = false;
        _preSpeechBuffer.Clear();
    }

    private void ResetDiagnostics()
    {
        Interlocked.Exchange(ref _capturedChunkCount, 0);
        Interlocked.Exchange(ref _sentChunkCount, 0);
        Interlocked.Exchange(ref _silenceDroppedChunkCount, 0);
        Interlocked.Exchange(ref _queueDroppedChunkCount, 0);
        Interlocked.Exchange(ref _preRollFlushedChunkCount, 0);
        Interlocked.Exchange(ref _persistedRecognitionCount, 0);
        Interlocked.Exchange(ref _tokenRefreshCount, 0);
        Interlocked.Exchange(ref _tokenRefreshFailureCount, 0);
        Interlocked.Exchange(ref _rewriteHitCount, 0);
        Interlocked.Exchange(ref _rewriteMissCount, 0);
        Interlocked.Exchange(ref _recognizerRestartCount, 0);
    }

    private void EnsureTokenRefreshStarted()
    {
        if (_tokenRefreshTask is { IsCompleted: false })
            return;

        _tokenRefreshCts?.Dispose();
        _tokenRefreshCts = new CancellationTokenSource();
        _tokenRefreshTask = Task.Run(() => RunTokenRefreshLoopAsync(_tokenRefreshCts.Token));
    }

    private async Task StopTokenRefreshAsync()
    {
        var tokenRefreshCts = _tokenRefreshCts;
        var tokenRefreshTask = _tokenRefreshTask;

        if (tokenRefreshCts == null)
            return;

        _tokenRefreshCts = null;
        _tokenRefreshTask = null;

        try { tokenRefreshCts.Cancel(); } catch { }

        if (tokenRefreshTask != null)
        {
            try { await tokenRefreshTask; } catch (OperationCanceledException) { } catch { }
        }

        tokenRefreshCts.Dispose();
    }

    private async Task RunTokenRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var refreshLead = TimeSpan.FromSeconds(Math.Max(15, _realtimeConfig.DirectTokenRefreshLeadSeconds));
            var dueIn = _tokenExpiresAtUtc - DateTime.UtcNow - refreshLead;
            if (dueIn < TimeSpan.FromSeconds(15))
                dueIn = TimeSpan.FromSeconds(15);

            try
            {
                await Task.Delay(dueIn, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RefreshSpeechTokenAsync(cancellationToken);
        }
    }

    private async Task RefreshSpeechTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authService.GetDirectSpeechTokenAsync(
                _meetingId,
                _sessionId,
                _sourceLanguage,
                _targetLanguages,
                _sessionToken);

            if (!response.Success || string.IsNullOrWhiteSpace(response.SpeechToken))
            {
                Interlocked.Increment(ref _tokenRefreshFailureCount);
                LogDiagnostics($"Direct Azure token refresh failed: {response.Message}");
                return;
            }

            _tokenExpiresAtUtc = response.ExpiresAtUtc;
            if (_recognizer != null)
                _recognizer.AuthorizationToken = response.SpeechToken;

            var refreshes = Interlocked.Increment(ref _tokenRefreshCount);
            LogDiagnostics($"Direct Azure token refreshed ({refreshes})");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _tokenRefreshFailureCount);
            LogDiagnostics($"Direct Azure token refresh threw: {ex.Message}");
        }
    }

    private void LogDiagnostics(string message)
    {
        if (!_audioCaptureConfig.EnableDiagnostics)
            return;

        var queueUsed = _pullCallback?.QueuedBytes ?? 0;
        var payload = $"[DirectAzureLipiRealtimeClient] {message} | captured={Interlocked.Read(ref _capturedChunkCount)} sent={Interlocked.Read(ref _sentChunkCount)} silenceDropped={Interlocked.Read(ref _silenceDroppedChunkCount)} queueDropped={Interlocked.Read(ref _queueDroppedChunkCount)} preRollFlushed={Interlocked.Read(ref _preRollFlushedChunkCount)} persisted={Interlocked.Read(ref _persistedRecognitionCount)} tokenRefreshes={Interlocked.Read(ref _tokenRefreshCount)} tokenRefreshFailures={Interlocked.Read(ref _tokenRefreshFailureCount)} rewriteHits={Interlocked.Read(ref _rewriteHitCount)} restarts={Interlocked.Read(ref _recognizerRestartCount)} queueBytes={queueUsed} dictVersion={_dictionaryVersion}";
        Debug.WriteLine(payload);
    }

    private SpeechTranslationConfig BuildSpeechTranslationConfig(string speechToken, string region)
    {
        var config = SpeechTranslationConfig.FromAuthorizationToken(speechToken, region);
        config.SpeechRecognitionLanguage = _sourceLanguage;
        config.OutputFormat = OutputFormat.Detailed;
        config.SetProperty(
            PropertyId.SpeechServiceResponse_ProfanityOption,
            NormalizeProfanityOption(_realtimeConfig.DirectProfanityOption));
        var segmentationSilenceTimeoutMs = Math.Clamp(_realtimeConfig.DirectSegmentationSilenceTimeoutMs, 300, 3000);
        config.SetProperty(
            PropertyId.Speech_SegmentationSilenceTimeoutMs,
            segmentationSilenceTimeoutMs.ToString(CultureInfo.InvariantCulture));
        config.SetProperty(
            PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs,
            segmentationSilenceTimeoutMs.ToString(CultureInfo.InvariantCulture));
        return config;
    }

    private int ComputePullQueueCapacityBytes()
    {
        // 16kHz, 16-bit mono = 32000 bytes/second
        const int bytesPerSecond = 32000;
        var capacitySeconds = Math.Clamp(_realtimeConfig.DirectPullQueueCapacitySeconds, 2, 30);
        return bytesPerSecond * capacitySeconds;
    }

    private static string NormalizeProfanityOption(string? profanityOption)
    {
        if (string.Equals(profanityOption, "Raw", StringComparison.OrdinalIgnoreCase))
            return "Raw";

        if (string.Equals(profanityOption, "Removed", StringComparison.OrdinalIgnoreCase))
            return "Removed";

        return "Masked";
    }

    private static int CalculateRootMeanSquareLevel(byte[] bytes)
    {
        if (bytes.Length < 2)
            return 0;

        double totalSquares = 0;
        var sampleCount = 0;
        for (var i = 0; i <= bytes.Length - 2; i += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i, 2));
            totalSquares += (double)sample * sample;
            sampleCount++;
        }

        return sampleCount == 0 ? 0 : (int)Math.Sqrt(totalSquares / sampleCount);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}