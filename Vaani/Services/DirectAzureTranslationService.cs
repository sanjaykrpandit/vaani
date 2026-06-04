using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Security;
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

    private string _speechToken = string.Empty;
    private string _region = string.Empty;
    private DateTime _tokenExpiresAtUtc = DateTime.MinValue;
    private TranslationSettings _settings = new();

    private volatile bool _isMicMuted;
    private volatile bool _isSpeakerMuted;
    private int _activeSpeakTasks;  // Interlocked counter — how many SpeakAsync calls are running
    private int _stopInProgress;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

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

        // 2. Wait for any in-flight SpeakAsync calls to finish naturally (they hold their own audio).
        //    They will exit quickly because _cts is cancelled — playback stops via CancellationToken.
        var waitStart = DateTime.UtcNow;
        while (Interlocked.CompareExchange(ref _activeSpeakTasks, 0, 0) > 0)
        {
            if ((DateTime.UtcNow - waitStart).TotalSeconds > 3) break; // keep stop responsive
            await Task.Delay(50);
        }

        // 3. Stop synthesizers and dispose everything
        await Task.WhenAny(
            Task.WhenAll(StopSynthesizer(_outgoingSynthesizer), StopSynthesizer(_incomingSynthesizer)),
            Task.Delay(1000));

        _outgoingRecognizer?.Dispose();  _outgoingRecognizer = null;
        _incomingRecognizer?.Dispose();  _incomingRecognizer = null;
        _outgoingSynthesizer?.Dispose(); _outgoingSynthesizer = null;
        _incomingSynthesizer?.Dispose(); _incomingSynthesizer = null;

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
            _outgoingSynthesizer = BuildSynthesizer(_settings.TargetVoice);

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
                var targetShort = _settings.TargetLanguage.Split('-')[0];
                if (!e.Result.Translations.TryGetValue(targetShort, out var translated) || string.IsNullOrWhiteSpace(translated))
                {
                    Log($"⚠️ No translation for key '{targetShort}'. Available: {string.Join(", ", e.Result.Translations.Keys)}");
                    return;
                }

                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = MessageDirection.Outgoing,
                    Text = original,
                    MessageType = MessageType.Recognized,
                    IsFromMeeting = false
                });
                TranslationReceived?.Invoke(this, new TranslationEventArgs
                {
                    Direction = MessageDirection.Outgoing,
                    OriginalText = original,
                    TranslatedText = translated,
                    IsFromMeeting = false
                });

                _ = SpeakAsync(translated, original, _outgoingSynthesizer!, isFromMeeting: false, _cts!.Token);
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
            _incomingSynthesizer = BuildSynthesizer(_settings.SourceVoice);
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
                var sourceShort = _settings.SourceLanguage.Split('-')[0];
                if (!e.Result.Translations.TryGetValue(sourceShort, out var translated) || string.IsNullOrWhiteSpace(translated))
                    return;

                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = MessageDirection.Incoming,
                    Text = original,
                    MessageType = MessageType.Recognized,
                    IsFromMeeting = true
                });
                TranslationReceived?.Invoke(this, new TranslationEventArgs
                {
                    Direction = MessageDirection.Incoming,
                    OriginalText = original,
                    TranslatedText = translated,
                    IsFromMeeting = true
                });

                _ = SpeakAsync(translated, original, _incomingSynthesizer!, isFromMeeting: true, _cts!.Token);
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

    private async Task SpeakAsync(string text, string original, SpeechSynthesizer synthesizer, bool isFromMeeting, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        Interlocked.Increment(ref _activeSpeakTasks);
        SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs { IsSynthesizing = true, IsFromMeeting = isFromMeeting, OriginalText = original, TranslatedText = text });
        try
        {
            var language = isFromMeeting ? _settings.SourceLanguage : _settings.TargetLanguage;
            var ssml = BuildFastSsml(text, language);
            var result = await synthesizer.SpeakSsmlAsync(ssml);
            if (result.Reason != ResultReason.SynthesizingAudioCompleted)
            {
                Log($"⚠️ Fast SSML synthesis fallback triggered. Reason: {result.Reason}");
                result.Dispose();
                result = await synthesizer.SpeakTextAsync(text);
            }

            SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs { IsSynthesizing = false, IsFromMeeting = isFromMeeting, OriginalText = original, TranslatedText = text });

            if (ct.IsCancellationRequested || result.Reason != ResultReason.SynthesizingAudioCompleted || result.AudioData.Length == 0)
            {
                result.Dispose();
                return;
            }

            if (isFromMeeting)
                await AudioPlaybackManager.PlayAudioToPhysicalSpeakerAsync(result.AudioData, _deviceService.FindPhysicalSpeaker(), ct);
            else
                await AudioPlaybackManager.PlayAudioToCableDeviceAsync(result.AudioData, _deviceService.FindOutgoingCableDevice(), ct);

            result.Dispose();
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

    private static string BuildFastSsml(string text, string language)
    {
        var safeText = SecurityElement.Escape(text) ?? string.Empty;
        var safeLang = string.IsNullOrWhiteSpace(language) ? "en-US" : language;
        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{safeLang}'><prosody rate='+{TtsRatePercent}%'>{safeText}</prosody></speak>";
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

    private void Log(string msg) => LogMessage?.Invoke(this, msg);

    private void FireSystem(string message, string detail, SystemMessageType type) =>
        SystemMessage?.Invoke(this, new SystemMessageEventArgs { Message = message, Detail = detail, MessageType = type });

    public void Dispose()
    {
        if (IsRunning)
            _ = StopTranslationAsync();
    }
}
