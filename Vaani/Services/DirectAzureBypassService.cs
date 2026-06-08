using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vaani.Authentication.Services;
using Vaani.Models;
using AzureAudioConfig = Microsoft.CognitiveServices.Speech.Audio.AudioConfig;

namespace Vaani.Services;

/// <summary>
/// Direct Azure bypass service: transcript + audio passthrough only.
/// No translation TTS playback.
/// </summary>
public class DirectAzureBypassService : ITranslationService
{
    private static readonly TimeSpan BypassBufferDuration = TimeSpan.FromSeconds(4);
    private const int BypassOutputLatencyMs = 40;
    private static readonly WaveFormat[] OutgoingFormatsToTry =
    [
        new WaveFormat(48000, 16, 1),
        new WaveFormat(44100, 16, 1),
        new WaveFormat(16000, 16, 1)
    ];

    private readonly DeviceService _deviceService;
    private readonly MeetingAuthenticationService _authService;
    private readonly int _tokenRefreshLeadSeconds;
    private readonly string _segmentationTimeoutMs;

    private CancellationTokenSource? _cts;
    private Task? _tokenRefreshTask;

    private SpeechRecognizer? _outgoingRecognizer;
    private SpeechRecognizer? _incomingRecognizer;

    private IWaveIn? _micCapture;
    private WasapiCapture? _loopbackCapture;
    private PushAudioInputStream? _outgoingAudioStream;

    private WasapiOut? _outgoingBypassPlayer;
    private BufferedWaveProvider? _outgoingBypassBuffer;
    private WasapiOut? _incomingBypassPlayer;
    private BufferedWaveProvider? _incomingBypassBuffer;

    private string _speechToken = string.Empty;
    private string _region = string.Empty;
    private DateTime _tokenExpiresAtUtc = DateTime.MinValue;
    private TranslationSettings _settings = new();

    private volatile bool _isMicMuted;
    private volatile bool _isSpeakerMuted;
    private int _stopInProgress;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    public event EventHandler<string>? LogMessage;
    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<TranslationEventArgs>? TranslationReceived;
    public event EventHandler<SystemMessageEventArgs>? SystemMessage;
    public event EventHandler<SynthesizingEventArgs>? SynthesizingStatusChanged;

    public DirectAzureBypassService()
    {
        _deviceService = DeviceService.Instance;
        _authService = new MeetingAuthenticationService();
        var realtime = ConfigurationService.Instance.Config.Realtime;
        _tokenRefreshLeadSeconds = realtime.DirectTokenRefreshLeadSeconds;
        _segmentationTimeoutMs = realtime.DirectSegmentationSilenceTimeoutMs.ToString();
    }

    public async Task StartTranslationAsync(TranslationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SourceLanguage) || string.IsNullOrWhiteSpace(settings.TargetLanguage))
        {
            FireSystem("Configuration Error", "Source or target language not set.", SystemMessageType.Error);
            return;
        }

        _settings = settings;
        _cts = new CancellationTokenSource();
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

        // Ensure latest plug/unplug topology is reflected before opening devices.
        _deviceService.RefreshDeviceCache();

        StartOutgoingBypass();
        StartIncomingBypass();
        StartTokenRefreshLoop(_cts.Token);

        FireSystem("Translation Started", "", SystemMessageType.Started);
        Log("⚡ Direct bypass mode started (passthrough + transcript, no TTS)");
    }

    public async Task StopTranslationAsync()
    {
        if (Interlocked.Exchange(ref _stopInProgress, 1) == 1)
            return;

        try
        {
            _cts?.Cancel();

            await Task.WhenAny(
                Task.WhenAll(StopRecognizer(_outgoingRecognizer), StopRecognizer(_incomingRecognizer)),
                Task.Delay(2000));

            CleanupAudio();

            _outgoingRecognizer?.Dispose(); _outgoingRecognizer = null;
            _incomingRecognizer?.Dispose(); _incomingRecognizer = null;

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

    private void StartOutgoingBypass()
    {
        try
        {
            var micDevice = _deviceService.FindPhysicalMicrophone();
            var cableDevice = _deviceService.FindOutgoingCableDevice();
            if (cableDevice == null)
            {
                Log("⚠️ Outgoing bypass CABLE device not found.");
                return;
            }

            var config = BuildSpeechConfig(_settings.SourceLanguage);

            var micDeviceNumber = ResolveWaveInDeviceNumber(micDevice);
            var micFormat = ResolveWorkingWaveInFormat(micDeviceNumber);
            var micName = micDevice?.FriendlyName ?? "Default Microphone";
            Log($"🎛️ Outgoing bypass capture: mic='{micName}', waveInDevice={micDeviceNumber}, format={micFormat.SampleRate}Hz/{micFormat.BitsPerSample}bit/{micFormat.Channels}ch, cable='{cableDevice.FriendlyName}'");
            _outgoingAudioStream = AudioInputStream.CreatePushStream(
                AudioStreamFormat.GetWaveFormatPCM((uint)micFormat.SampleRate, (byte)micFormat.BitsPerSample, (byte)micFormat.Channels));
            _outgoingRecognizer = new SpeechRecognizer(config, AzureAudioConfig.FromStreamInput(_outgoingAudioStream));

            _outgoingRecognizer.Recognizing += (_, e) =>
            {
                if (_isMicMuted || e.Result.Reason != ResultReason.RecognizingSpeech) return;
                var text = e.Result.Text;
                if (string.IsNullOrWhiteSpace(text)) return;

                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = MessageDirection.Outgoing,
                    Text = text,
                    MessageType = MessageType.Recognizing,
                    IsFromMeeting = false
                });
            };

            _outgoingRecognizer.Recognized += (_, e) =>
            {
                if (_isMicMuted || e.Result.Reason != ResultReason.RecognizedSpeech || string.IsNullOrWhiteSpace(e.Result.Text)) return;
                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = MessageDirection.Outgoing,
                    Text = e.Result.Text,
                    MessageType = MessageType.Recognized,
                    IsFromMeeting = false
                });
            };

            _ = _outgoingRecognizer.StartContinuousRecognitionAsync();

            _micCapture = new WaveInEvent
            {
                DeviceNumber = micDeviceNumber,
                WaveFormat = micFormat,
                BufferMilliseconds = 20
            };
            _outgoingBypassBuffer = new BufferedWaveProvider(micFormat)
            {
                BufferDuration = BypassBufferDuration,
                ReadFully = true,
                DiscardOnBufferOverflow = true
            };
            _outgoingBypassPlayer = new WasapiOut(cableDevice, AudioClientShareMode.Shared, false, BypassOutputLatencyMs);
            _outgoingBypassPlayer.Init(_outgoingBypassBuffer);
            PreFillBufferWithSilence(_outgoingBypassBuffer, micFormat);
            _outgoingBypassPlayer.Play();

            _micCapture.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0) return;

                if (!_isMicMuted)
                {
                    _outgoingBypassBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    _outgoingAudioStream?.Write(e.Buffer, e.BytesRecorded);
                }
            };
            _micCapture.StartRecording();

            Log("🎤 Outgoing bypass pipeline started (mic → CABLE-A + transcript)");
        }
        catch (Exception ex)
        {
            Log($"❌ Outgoing bypass start failed: {ex.Message}");
        }
    }

    private void StartIncomingBypass()
    {
        try
        {
            var cableCapture = FindCableCaptureDevice();
            if (cableCapture == null)
            {
                Log("⚠️ No CABLE-B capture device found — incoming bypass skipped.");
                return;
            }

            var config = BuildSpeechConfig(_settings.TargetLanguage);
            _incomingRecognizer = new SpeechRecognizer(config, AzureAudioConfig.FromMicrophoneInput(cableCapture.ID));

            _incomingRecognizer.Recognizing += (_, e) =>
            {
                if (_isSpeakerMuted || e.Result.Reason != ResultReason.RecognizingSpeech) return;
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
                if (_isSpeakerMuted || e.Result.Reason != ResultReason.RecognizedSpeech || string.IsNullOrWhiteSpace(e.Result.Text)) return;
                MessageReceived?.Invoke(this, new MessageEventArgs
                {
                    Direction = MessageDirection.Incoming,
                    Text = e.Result.Text,
                    MessageType = MessageType.Recognized,
                    IsFromMeeting = true
                });
            };

            _ = _incomingRecognizer.StartContinuousRecognitionAsync();

            _loopbackCapture = new WasapiCapture(cableCapture);
            var speaker = _deviceService.FindPhysicalSpeaker();
            _incomingBypassBuffer = new BufferedWaveProvider(_loopbackCapture.WaveFormat)
            {
                BufferDuration = BypassBufferDuration,
                ReadFully = true,
                DiscardOnBufferOverflow = true
            };
            _incomingBypassPlayer = speaker != null
                ? new WasapiOut(speaker, AudioClientShareMode.Shared, false, BypassOutputLatencyMs)
                : new WasapiOut(AudioClientShareMode.Shared, BypassOutputLatencyMs);
            _incomingBypassPlayer.Init(_incomingBypassBuffer);
            PreFillBufferWithSilence(_incomingBypassBuffer, _loopbackCapture.WaveFormat);
            _incomingBypassPlayer.Play();

            _loopbackCapture.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded <= 0) return;

                if (!_isSpeakerMuted)
                    _incomingBypassBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };
            _loopbackCapture.StartRecording();

            Log("🔊 Incoming bypass pipeline started (CABLE-B → speaker + transcript)");
        }
        catch (Exception ex)
        {
            Log($"❌ Incoming bypass start failed: {ex.Message}");
        }
    }

    private void CleanupAudio()
    {
        try { _micCapture?.StopRecording(); } catch { }
        _micCapture?.Dispose(); _micCapture = null;

        try { _outgoingAudioStream?.Close(); } catch { }
        _outgoingAudioStream?.Dispose(); _outgoingAudioStream = null;

        try { _loopbackCapture?.StopRecording(); } catch { }
        _loopbackCapture?.Dispose(); _loopbackCapture = null;

        try { _outgoingBypassPlayer?.Stop(); } catch { }
        _outgoingBypassPlayer?.Dispose(); _outgoingBypassPlayer = null;
        _outgoingBypassBuffer = null;

        try { _incomingBypassPlayer?.Stop(); } catch { }
        _incomingBypassPlayer?.Dispose(); _incomingBypassPlayer = null;
        _incomingBypassBuffer = null;
    }

    private static void PreFillBufferWithSilence(BufferedWaveProvider buffer, WaveFormat format)
    {
        var bytesPer250Ms = format.AverageBytesPerSecond / 4;
        if (bytesPer250Ms <= 0)
            return;

        var silence = new byte[bytesPer250Ms];
        buffer.AddSamples(silence, 0, silence.Length);
    }

    private SpeechConfig BuildSpeechConfig(string language)
    {
        var config = SpeechConfig.FromAuthorizationToken(_speechToken, _region);
        config.SpeechRecognitionLanguage = language;
        config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, _segmentationTimeoutMs);
        return config;
    }

    private MMDevice? FindCableCaptureDevice()
    {
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
        return fallback;
    }

    private static int ResolveWaveInDeviceNumber(MMDevice? preferredMic)
    {
        if (preferredMic == null)
            return 0;

        var target = preferredMic.FriendlyName;
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            var name = caps.ProductName ?? string.Empty;
            if (name.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                target.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static WaveFormat ResolveWorkingWaveInFormat(int deviceNumber)
    {
        foreach (var format in OutgoingFormatsToTry)
        {
            try
            {
                using var testCapture = new WaveInEvent
                {
                    DeviceNumber = deviceNumber,
                    WaveFormat = format,
                    BufferMilliseconds = 20
                };
                testCapture.StartRecording();
                testCapture.StopRecording();
                return format;
            }
            catch
            {
            }
        }

        return new WaveFormat(16000, 16, 1);
    }

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

            if (_outgoingRecognizer != null) _outgoingRecognizer.AuthorizationToken = response.SpeechToken;
            if (_incomingRecognizer != null) _incomingRecognizer.AuthorizationToken = response.SpeechToken;

            Log($"🔑 Token refreshed, next expiry={_tokenExpiresAtUtc:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            Log($"⚠️ Token refresh exception: {ex.Message}");
        }
    }

    private static async Task StopRecognizer(SpeechRecognizer? recognizer)
    {
        if (recognizer == null) return;
        try { await recognizer.StopContinuousRecognitionAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
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
