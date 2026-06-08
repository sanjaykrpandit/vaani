using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Security;
using System.Text;
using System.Threading.Channels;
using Vaani.Authentication.Services;
using Vaani.Models;
using AzureAudioConfig = Microsoft.CognitiveServices.Speech.Audio.AudioConfig;

namespace Vaani.Services;

/// <summary>
/// Direct Azure translation service — runs STT + translate + TTS fully on the client.
/// No audio bytes over the wire. Token is fetched from the API and refreshed in-place.
/// Bidirectional: outgoing (mic → target lang → CABLE) and incoming (CABLE loopback → source lang → speaker).
/// </summary>
public class DirectAzureTranslationService : ITranslationService
{
    private const int TtsRatePercent = 10;
    private const int MaxLeadingSilenceTrimMs = 350;
    private const short LeadingSilenceThreshold = 120;
    private const int MaxTtsChunkChars = 120;
    private const int MaxTtsChunkWords = 18;
    private const int MaxTtsQueueAgeMs = 2500;
    private readonly DeviceService _deviceService;
    private readonly MeetingAuthenticationService _authService;
    private readonly int _tokenRefreshLeadSeconds;
    private readonly string _segmentationTimeoutMs;

    private CancellationTokenSource? _cts;
    private Task? _tokenRefreshTask;

    private TranslationRecognizer? _outgoingRecognizer;
    private TranslationRecognizer? _incomingRecognizer;
    private PushAudioInputStream? _incomingPushStream;
    private WasapiCapture? _loopbackCapture;

    private SpeechSynthesizer? _outgoingSynthesizer;
    private SpeechSynthesizer? _incomingSynthesizer;
    private Channel<TtsWorkItem>? _outgoingTtsQueue;
    private Channel<TtsWorkItem>? _incomingTtsQueue;
    private Task? _outgoingTtsWorker;
    private Task? _incomingTtsWorker;

    private string _speechToken = string.Empty;
    private string _region = string.Empty;
    private DateTime _tokenExpiresAtUtc = DateTime.MinValue;
    private TranslationSettings _settings = new();

    private volatile bool _isMicMuted;
    private volatile bool _isSpeakerMuted;
    private int _activeSpeakTasks;  // Interlocked counter — how many SpeakAsync calls are running
    private int _stopInProgress;
    private int _outgoingPendingTts;
    private int _incomingPendingTts;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    private readonly record struct TtsWorkItem(string OriginalText, string TranslatedText, long EnqueuedAtMs);

    public event EventHandler<string>? LogMessage;
    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<TranslationEventArgs>? TranslationReceived;
    public event EventHandler<SystemMessageEventArgs>? SystemMessage;
    public event EventHandler<SynthesizingEventArgs>? SynthesizingStatusChanged;

    public DirectAzureTranslationService()
    {
        _deviceService = DeviceService.Instance;
        _authService = new MeetingAuthenticationService();
        var realtime = ConfigurationService.Instance.Config.Realtime;
        _tokenRefreshLeadSeconds = realtime.DirectTokenRefreshLeadSeconds;
        _segmentationTimeoutMs = realtime.DirectSegmentationSilenceTimeoutMs.ToString();
    }

    public async Task StartTranslationAsync(TranslationSettings settings)
    {
        // Validate configuration before making any API calls
        if (string.IsNullOrWhiteSpace(settings.SourceLanguage) || string.IsNullOrWhiteSpace(settings.TargetLanguage))
        {
            FireSystem("Configuration Error", $"Source or target language not set (source='{settings.SourceLanguage}', target='{settings.TargetLanguage}').", SystemMessageType.Error);
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.SourceVoice) || string.IsNullOrWhiteSpace(settings.TargetVoice))
        {
            FireSystem("Configuration Error", $"Voice not configured (sourceVoice='{settings.SourceVoice}', targetVoice='{settings.TargetVoice}').", SystemMessageType.Error);
            return;
        }

        _settings = settings;
        _cts = new CancellationTokenSource();

        _outgoingTtsQueue = Channel.CreateBounded<TtsWorkItem>(new BoundedChannelOptions(3)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _incomingTtsQueue = Channel.CreateBounded<TtsWorkItem>(new BoundedChannelOptions(3)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _outgoingTtsWorker = RunTtsQueueAsync(_outgoingTtsQueue, isFromMeeting: false, _cts.Token);
        _incomingTtsWorker = RunTtsQueueAsync(_incomingTtsQueue, isFromMeeting: true, _cts.Token);

        // Always reset mute state at start — stale state from previous session causes silent pipelines
        _isMicMuted = false;
        _isSpeakerMuted = false;
        Log("🔑 Fetching Azure speech token...");
        var tokenResponse = await _authService.GetDirectSpeechTokenAsync(
            settings.MeetingId ?? "",
            settings.SessionId ?? "",
            settings.SessionToken ?? "",
            settings.SourceLanguage,
            settings.TargetLanguage);

        if (!tokenResponse.Success || string.IsNullOrWhiteSpace(tokenResponse.SpeechToken))
        {
            FireSystem("Token Error", tokenResponse.Message ?? "Failed to get speech token.", SystemMessageType.Error);
            return;
        }

        _speechToken = tokenResponse.SpeechToken;
        _region = tokenResponse.Region;
        _tokenExpiresAtUtc = tokenResponse.ExpiresAtUtc;
        Log($"✅ Token acquired, region={_region}, expires={_tokenExpiresAtUtc:HH:mm:ss}");
        Log($"🌐 {settings.SourceLanguage} → {settings.TargetLanguage} | {settings.SourceVoice} / {settings.TargetVoice}");

        // Create persistent synthesizers before warmup so warmup primes the same instances used at runtime.
        _outgoingSynthesizer = BuildSynthesizer(_settings.TargetVoice);
        _incomingSynthesizer = BuildSynthesizer(_settings.SourceVoice);

        await WarmUpTtsVoicesAsync(_cts.Token);

        StartOutgoing();
        StartIncoming();
        StartTokenRefreshLoop(_cts.Token);

        FireSystem("Translation Started", "", SystemMessageType.Started);
    }

    public async Task StopTranslationAsync()
    {
        if (Interlocked.Exchange(ref _stopInProgress, 1) == 1)
            return;

        try
        {
        _cts?.Cancel();

        // 1. Stop recognizers first so no new Recognized events fire new SpeakAsync calls.
        //    Run both in parallel with a 2s timeout each.
        await Task.WhenAny(
            Task.WhenAll(StopRecognizer(_outgoingRecognizer), StopRecognizer(_incomingRecognizer)),
            Task.Delay(2000));

        // 2. Complete TTS queues and let workers drain in-order for each direction.
        try { _outgoingTtsQueue?.Writer.TryComplete(); } catch { }
        try { _incomingTtsQueue?.Writer.TryComplete(); } catch { }
        if (_outgoingTtsWorker != null)
            try { await _outgoingTtsWorker.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        if (_incomingTtsWorker != null)
            try { await _incomingTtsWorker.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }

        // 3. Wait for any in-flight SpeakAsync calls to finish naturally.
        var waitStart = DateTime.UtcNow;
        while (Interlocked.CompareExchange(ref _activeSpeakTasks, 0, 0) > 0)
        {
            if ((DateTime.UtcNow - waitStart).TotalSeconds > 3) break; // keep stop responsive
            await Task.Delay(50);
        }

        // 4. Stop synthesizers and dispose everything
        await Task.WhenAny(
            Task.WhenAll(StopSynthesizer(_outgoingSynthesizer), StopSynthesizer(_incomingSynthesizer)),
            Task.Delay(1000));

        _outgoingRecognizer?.Dispose();  _outgoingRecognizer = null;
        _incomingRecognizer?.Dispose();  _incomingRecognizer = null;
        _outgoingSynthesizer?.Dispose(); _outgoingSynthesizer = null;
        _incomingSynthesizer?.Dispose(); _incomingSynthesizer = null;
        _outgoingTtsQueue = null;
        _incomingTtsQueue = null;
        _outgoingTtsWorker = null;
        _incomingTtsWorker = null;

        try { _loopbackCapture?.StopRecording(); } catch { }
        _loopbackCapture?.Dispose();     _loopbackCapture = null;

        _incomingPushStream?.Close();
        _incomingPushStream?.Dispose();  _incomingPushStream = null;

        if (_tokenRefreshTask != null)
            try { await _tokenRefreshTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }

        FireSystem("Translation Stopped", "", SystemMessageType.Stopped);
        }
        finally
        {
            Interlocked.Exchange(ref _stopInProgress, 0);
        }
    }

    public void SetMicrophoneMute(bool muted) => _isMicMuted = muted;
    public void SetSpeakerMute(bool muted) => _isSpeakerMuted = muted;

    // ─────────────────────────────────────────────────────────────────────────
    // Outgoing: mic → STT+translate → TTS → CABLE
    // ─────────────────────────────────────────────────────────────────────────

    private void StartOutgoing()
    {
        try
        {
            // Azure Speech SDK uses its own audio layer — always use system default mic
            var config = BuildTranslationConfig(_settings.SourceLanguage, _settings.TargetLanguage);
            _outgoingRecognizer = new TranslationRecognizer(config, AzureAudioConfig.FromDefaultMicrophoneInput());

            _outgoingRecognizer.Recognizing += (_, e) =>
            {
                if (_isMicMuted || e.Result.Reason != ResultReason.TranslatingSpeech) return;
                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = MessageDirection.Outgoing,
                    Text = e.Result.Text,
                    MessageType = MessageType.Recognizing,
                    IsFromMeeting = false
                });
            };

            _outgoingRecognizer.Recognized += (_, e) =>
            {
                if (_isMicMuted || e.Result.Reason != ResultReason.TranslatedSpeech || string.IsNullOrWhiteSpace(e.Result.Text)) return;

                var original = e.Result.Text;
                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = MessageDirection.Outgoing,
                    Text = original,
                    MessageType = MessageType.Recognized,
                    IsFromMeeting = false
                });

                var targetShort = _settings.TargetLanguage.Split('-')[0];
                if (!e.Result.Translations.TryGetValue(targetShort, out var translated) || string.IsNullOrWhiteSpace(translated))
                {
                    Log($"⚠️ No translation for key '{targetShort}'. Available: {string.Join(", ", e.Result.Translations.Keys)}");
                    return;
                }

                TranslationReceived?.Invoke(this, new TranslationEventArgs
                {
                    Direction = MessageDirection.Outgoing,
                    OriginalText = original,
                    TranslatedText = translated,
                    IsFromMeeting = false
                });

                if (!TryEnqueueTts(isFromMeeting: false, original, translated))
                    Log("⚠️ Outgoing TTS queue unavailable, dropped item");
                _ = LogTranscriptAsync(original, translated);
            };

            _outgoingRecognizer.SessionStarted += (_, e) => Log("🎤 Outgoing session started");
            _outgoingRecognizer.Canceled += OnRecognizerCanceled;

            _ = _outgoingRecognizer.StartContinuousRecognitionAsync();
            Log("🎤 Outgoing pipeline started");
        }
        catch (Exception ex)
        {
            Log($"❌ StartOutgoing failed: {ex.Message}");
            FireSystem("Direct Azure Error", $"Outgoing pipeline failed: {ex.Message}", SystemMessageType.Error);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Incoming: CABLE loopback → STT+translate → TTS → speaker
    // ─────────────────────────────────────────────────────────────────────────

    private void StartIncoming()
    {
        try
        {
            // VB-Audio CABLE doesn't support WASAPI loopback.
            // CABLE-B Output is a real capture device — use WasapiCapture directly on it.
            var cableDevice = FindCableCaptureDevice();
            if (cableDevice == null)
            {
                Log("⚠️ No CABLE-B capture device found — incoming pipeline skipped");
                return;
            }

            _incomingPushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
            var config = BuildTranslationConfig(_settings.TargetLanguage, _settings.SourceLanguage);
            _incomingRecognizer = new TranslationRecognizer(config, AzureAudioConfig.FromStreamInput(_incomingPushStream));

            _incomingRecognizer.Recognizing += (_, e) =>
            {
                if (_isSpeakerMuted || e.Result.Reason != ResultReason.TranslatingSpeech) return;
                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = MessageDirection.Incoming,
                    Text = e.Result.Text,
                    MessageType = MessageType.Recognizing,
                    IsFromMeeting = true
                });
            };

            _incomingRecognizer.Recognized += (_, e) =>
            {
                if (_isSpeakerMuted || e.Result.Reason != ResultReason.TranslatedSpeech || string.IsNullOrWhiteSpace(e.Result.Text)) return;

                var original = e.Result.Text;
                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = MessageDirection.Incoming,
                    Text = original,
                    MessageType = MessageType.Recognized,
                    IsFromMeeting = true
                });

                var sourceShort = _settings.SourceLanguage.Split('-')[0];
                if (!e.Result.Translations.TryGetValue(sourceShort, out var translated) || string.IsNullOrWhiteSpace(translated))
                    return;

                TranslationReceived?.Invoke(this, new TranslationEventArgs
                {
                    Direction = MessageDirection.Incoming,
                    OriginalText = original,
                    TranslatedText = translated,
                    IsFromMeeting = true
                });

                if (!TryEnqueueTts(isFromMeeting: true, original, translated))
                    Log("⚠️ Incoming TTS queue unavailable, dropped item");
            };

            _incomingRecognizer.SessionStarted += (_, e) => Log("🔊 Incoming session started");
            _incomingRecognizer.Canceled += OnRecognizerCanceled;

            _ = _incomingRecognizer.StartContinuousRecognitionAsync();

            // Capture directly from CABLE-B Output (the VB-Audio capture endpoint)
            _loopbackCapture = new WasapiCapture(cableDevice);
            var fmt = _loopbackCapture.WaveFormat;

            _loopbackCapture.DataAvailable += (_, e) =>
            {
                if (_incomingPushStream == null || e.BytesRecorded == 0) return;

                var pcm = AudioPcmConverter.ToTarget16kHz(e.Buffer, e.BytesRecorded, fmt);
                if (pcm.Length > 0)
                    _incomingPushStream.Write(pcm, pcm.Length);
            };
            _loopbackCapture.StartRecording();

            Log("🔊 Incoming pipeline started");
        }
        catch (Exception ex)
        {
            Log($"❌ StartIncoming failed: {ex.Message}");
            FireSystem("Direct Azure Error", $"Incoming pipeline failed: {ex.Message}", SystemMessageType.Error);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TTS — synthesize then play; bubble clears as soon as synthesis completes
    // ─────────────────────────────────────────────────────────────────────────

    private async Task SpeakAsync(string text, string original, SpeechSynthesizer synthesizer, bool isFromMeeting, long enqueuedAtMs, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        Interlocked.Increment(ref _activeSpeakTasks);
        SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs { IsSynthesizing = true, IsFromMeeting = isFromMeeting, OriginalText = original, TranslatedText = text });
        try
        {
            var speakStartMs = Environment.TickCount64;
            var queueDelayMs = Math.Max(0, speakStartMs - enqueuedAtMs);
            var direction = isFromMeeting ? "IN" : "OUT";

            var language = isFromMeeting ? _settings.SourceLanguage : _settings.TargetLanguage;
            var voice = isFromMeeting ? _settings.SourceVoice : _settings.TargetVoice;
            var chunks = SplitForTts(text);
            var synthTotalMs = 0L;
            var playTotalMs = 0L;
            var firstChunkStartLatencyMs = -1L;

            SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs { IsSynthesizing = false, IsFromMeeting = isFromMeeting, OriginalText = original, TranslatedText = text });

            for (var i = 0; i < chunks.Count; i++)
            {
                if (ct.IsCancellationRequested)
                    break;

                var chunk = chunks[i];
                var synthStartMs = Environment.TickCount64;
                using var result = await SynthesizeWithFallbackAsync(synthesizer, chunk, language, voice);
                var synthDoneMs = Environment.TickCount64;
                synthTotalMs += Math.Max(0, synthDoneMs - synthStartMs);

                if (result.Reason != ResultReason.SynthesizingAudioCompleted || result.AudioData.Length == 0)
                    continue;

                var playbackAudio = TrimLeadingSilencePcm16Mono(result.AudioData, MaxLeadingSilenceTrimMs, LeadingSilenceThreshold);
                var playStartMs = Environment.TickCount64;

                if (firstChunkStartLatencyMs < 0)
                    firstChunkStartLatencyMs = Math.Max(0, playStartMs - enqueuedAtMs);

                if (isFromMeeting)
                    await AudioPlaybackManager.PlayAudioToPhysicalSpeakerAsync(playbackAudio, _deviceService.FindPhysicalSpeaker(), ct);
                else
                {
                    var outgoingCable = _deviceService.FindOutgoingCableDevice();
                    await AudioPlaybackManager.PlayAudioToCableDeviceAsync(playbackAudio, outgoingCable, ct);
                }

                var chunkDoneMs = Environment.TickCount64;
                playTotalMs += Math.Max(0, chunkDoneMs - playStartMs);

                if (i < chunks.Count - 1 && HasPendingTts(isFromMeeting))
                {
                    Log($"⏭️ TTS[{direction}] truncated remaining chunks to prioritize newer speech");
                    break;
                }
            }

            var doneMs = Environment.TickCount64;
            Log($"⏱️ TTS[{direction}] queue={queueDelayMs}ms firstStart={Math.Max(0, firstChunkStartLatencyMs)}ms synth={synthTotalMs}ms play={playTotalMs}ms chunks={chunks.Count} total={Math.Max(0, doneMs - enqueuedAtMs)}ms");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Log($"⚠️ TTS error: {ex.Message}"); }
        catch { }
        finally
        {
            SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs { IsSynthesizing = false, IsFromMeeting = isFromMeeting, OriginalText = original, TranslatedText = text });
            Interlocked.Decrement(ref _activeSpeakTasks);
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

    // ─────────────────────────────────────────────────────────────────────────
    // Token refresh — updates running recognizers/synthesizers in-place
    // ─────────────────────────────────────────────────────────────────────────

    private void StartTokenRefreshLoop(CancellationToken ct)
    {
        _tokenRefreshTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var lead = TimeSpan.FromSeconds(Math.Max(15, _tokenRefreshLeadSeconds));
                var delay = _tokenExpiresAtUtc - DateTime.UtcNow - lead;
                if (delay < TimeSpan.FromSeconds(15)) delay = TimeSpan.FromSeconds(15);

                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }

                await RefreshTokenAsync();
            }
        }, ct);
    }

    private async Task RefreshTokenAsync()
    {
        try
        {
            var response = await _authService.GetDirectSpeechTokenAsync(
                _settings.MeetingId ?? "",
                _settings.SessionId ?? "",
                _settings.SessionToken ?? "",
                _settings.SourceLanguage,
                _settings.TargetLanguage);

            if (!response.Success || string.IsNullOrWhiteSpace(response.SpeechToken))
            {
                Log($"⚠️ Token refresh failed: {response.Message}");
                return;
            }

            _speechToken = response.SpeechToken;
            _tokenExpiresAtUtc = response.ExpiresAtUtc;

            // Update in-place — no recognizer restart required
            if (_outgoingRecognizer != null) _outgoingRecognizer.AuthorizationToken = response.SpeechToken;
            if (_incomingRecognizer != null) _incomingRecognizer.AuthorizationToken = response.SpeechToken;
            if (_outgoingSynthesizer != null) _outgoingSynthesizer.AuthorizationToken = response.SpeechToken;
            if (_incomingSynthesizer != null) _incomingSynthesizer.AuthorizationToken = response.SpeechToken;

            Log($"🔑 Token refreshed, next expiry={_tokenExpiresAtUtc:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            Log($"⚠️ Token refresh exception: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private SpeechTranslationConfig BuildTranslationConfig(string sourceLang, string targetLang)
    {
        var config = SpeechTranslationConfig.FromAuthorizationToken(_speechToken, _region);
        config.SpeechRecognitionLanguage = sourceLang;
        config.AddTargetLanguage(targetLang.Split('-')[0]);
        config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, _segmentationTimeoutMs);
        config.OutputFormat = OutputFormat.Detailed;
        return config;
    }

    private SpeechSynthesizer BuildSynthesizer(string voiceName)
    {
        var config = SpeechConfig.FromAuthorizationToken(_speechToken, _region);
        config.SpeechSynthesisVoiceName = voiceName;
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
        return new SpeechSynthesizer(config, audioConfig: null); // null = no auto playback
    }

    private MMDevice? FindCableCaptureDevice()
    {
        // VB-Audio CABLE-B Output is a Capture endpoint — enumerate capture devices
        var enumerator = new MMDeviceEnumerator();
        MMDevice? fallback = null;
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            var name = d.FriendlyName;
            if (name.Contains("CABLE-B", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("CABLE B", StringComparison.OrdinalIgnoreCase))
            {
                Log($"🔊 CABLE capture device: {name}");
                return d;
            }
            if (fallback == null && name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                fallback = d;
        }
        if (fallback != null) Log($"🔊 CABLE capture device (fallback): {fallback.FriendlyName}");
        return fallback;
    }

    private void OnRecognizerCanceled(object? sender, TranslationRecognitionCanceledEventArgs e)
    {
        if (e.Reason != CancellationReason.Error) return;
        Log($"❌ Azure error ({e.ErrorCode}): {e.ErrorDetails}");
        FireSystem("Direct Azure Error", e.ErrorDetails, SystemMessageType.Error);
    }

    private async Task LogTranscriptAsync(string original, string translated)
    {
        if (string.IsNullOrWhiteSpace(_settings.SessionToken)) return;
        try
        {
            await _authService.SubmitTranscriptAsync(
                _settings.MeetingId ?? "",
                _settings.SessionId ?? "",
                _settings.SourceLanguage,
                _settings.TargetLanguage,
                original,
                translated,
                _settings.SessionToken);
        }
        catch (Exception ex)
        {
            Log($"⚠️ Transcript log failed: {ex.Message}");
        }
    }

    private static async Task StopRecognizer(TranslationRecognizer? recognizer)
    {
        if (recognizer == null) return;
        try { await recognizer.StopContinuousRecognitionAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }

    private static async Task StopSynthesizer(SpeechSynthesizer? synthesizer)
    {
        if (synthesizer == null) return;
        try { await synthesizer.StopSpeakingAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }

    private async Task<SpeechSynthesisResult> SynthesizeWithFallbackAsync(SpeechSynthesizer synthesizer, string text, string language, string voice)
    {
        var ssml = BuildFastSsml(text, language, voice);
        var result = await synthesizer.SpeakSsmlAsync(ssml);
        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            return result;

        result.Dispose();
        var ssmlMultiplier = BuildFastSsmlMultiplier(text, language, voice);
        result = await synthesizer.SpeakSsmlAsync(ssmlMultiplier);
        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            return result;

        result.Dispose();
        return await synthesizer.SpeakTextAsync(text);
    }

    private static List<string> SplitForTts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [string.Empty];

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return [text];

        var chunks = new List<string>();
        var sb = new StringBuilder();
        var wordCount = 0;

        foreach (var word in words)
        {
            var nextLen = sb.Length == 0 ? word.Length : sb.Length + 1 + word.Length;
            if (sb.Length > 0 && (nextLen > MaxTtsChunkChars || wordCount >= MaxTtsChunkWords))
            {
                chunks.Add(sb.ToString());
                sb.Clear();
                wordCount = 0;
            }

            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(word);
            wordCount++;
        }

        if (sb.Length > 0)
            chunks.Add(sb.ToString());

        return chunks.Count > 0 ? chunks : [text];
    }

    private async Task WarmUpTtsVoicesAsync(CancellationToken ct)
    {
        try
        {
            await Task.WhenAll(
                WarmUpVoiceAsync(_settings.TargetLanguage, _settings.TargetVoice, "OUT", ct),
                WarmUpVoiceAsync(_settings.SourceLanguage, _settings.SourceVoice, "IN", ct));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"⚠️ TTS warmup skipped: {ex.Message}");
        }
    }

    private async Task WarmUpVoiceAsync(string language, string voiceName, string direction, CancellationToken ct)
    {
        var startMs = Environment.TickCount64;
        using var synth = BuildSynthesizer(voiceName);
        var ssml = BuildWarmupSsml(language, voiceName);
        using var result = await synth.SpeakSsmlAsync(ssml).WaitAsync(ct);
        var elapsedMs = Math.Max(0, Environment.TickCount64 - startMs);
        Log($"🔥 TTS warmup[{direction}] {elapsedMs}ms");
    }

    private static string BuildWarmupSsml(string language, string voiceName)
    {
        var safeLang = string.IsNullOrWhiteSpace(language) ? "en-US" : language;
        var safeVoice = SecurityElement.Escape(voiceName) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(safeVoice))
            return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{safeLang}'><voice name='{safeVoice}'><prosody volume='silent'>.</prosody></voice></speak>";

        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{safeLang}'><prosody volume='silent'>.</prosody></speak>";
    }

    private static byte[] TrimLeadingSilencePcm16Mono(byte[] pcm, int maxTrimMs, short threshold)
    {
        if (pcm.Length < 2)
            return pcm;

        const int sampleRate = 16000;
        const int bytesPerSample = 2;
        var maxTrimBytes = Math.Min(pcm.Length, sampleRate * bytesPerSample * maxTrimMs / 1000);
        if ((maxTrimBytes & 1) == 1)
            maxTrimBytes--;

        var cutAt = 0;
        for (var i = 0; i < maxTrimBytes; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm, i);
            if (Math.Abs(sample) > threshold)
            {
                cutAt = i;
                break;
            }
        }

        if (cutAt <= 0)
            return pcm;

        var trimmed = new byte[pcm.Length - cutAt];
        Buffer.BlockCopy(pcm, cutAt, trimmed, 0, trimmed.Length);
        return trimmed;
    }

    private bool TryEnqueueTts(bool isFromMeeting, string originalText, string translatedText)
    {
        var queue = isFromMeeting ? _incomingTtsQueue : _outgoingTtsQueue;
        if (queue == null)
            return false;

        var accepted = queue.Writer.TryWrite(new TtsWorkItem(originalText, translatedText, Environment.TickCount64));
        if (accepted)
        {
            if (isFromMeeting)
                Interlocked.Increment(ref _incomingPendingTts);
            else
                Interlocked.Increment(ref _outgoingPendingTts);
        }

        return accepted;
    }

    private async Task RunTtsQueueAsync(Channel<TtsWorkItem> queue, bool isFromMeeting, CancellationToken ct)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(ct))
            {
                if (isFromMeeting)
                    Interlocked.Decrement(ref _incomingPendingTts);
                else
                    Interlocked.Decrement(ref _outgoingPendingTts);

                var ageMs = Math.Max(0, Environment.TickCount64 - item.EnqueuedAtMs);
                if (ageMs > MaxTtsQueueAgeMs)
                {
                    Log($"⏭️ TTS[{(isFromMeeting ? "IN" : "OUT")}] dropped stale item aged {ageMs}ms");
                    continue;
                }

                var synthesizer = isFromMeeting ? _incomingSynthesizer : _outgoingSynthesizer;
                if (synthesizer == null)
                    continue;

                await SpeakAsync(item.TranslatedText, item.OriginalText, synthesizer, isFromMeeting, item.EnqueuedAtMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"⚠️ TTS queue worker error: {ex.Message}");
        }
    }

    private void Log(string msg) => LogMessage?.Invoke(this, msg);

    private bool HasPendingTts(bool isFromMeeting)
    {
        return isFromMeeting
            ? Interlocked.CompareExchange(ref _incomingPendingTts, 0, 0) > 0
            : Interlocked.CompareExchange(ref _outgoingPendingTts, 0, 0) > 0;
    }

    private void FireSystem(string message, string detail, SystemMessageType type) =>
        SystemMessage?.Invoke(this, new SystemMessageEventArgs { Message = message, Detail = detail, MessageType = type });

    public void Dispose()
    {
        if (IsRunning)
            _ = StopTranslationAsync();
    }
}
