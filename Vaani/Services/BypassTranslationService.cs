using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vaani.Models;
using Avalonia.Threading;
using vconsole.Services.Logger;

namespace Vaani.Services;

/// <summary>
/// Bypass translation service - Direct audio passthrough with transcription only.
/// No translation or synthesis - just pure audio routing with Azure STT for transcript display.
/// </summary>
public class BypassTranslationService : ITranslationService
{
    // Audio format constants
    private static readonly WaveFormat[] AudioFormatsToTry =
    [
        new WaveFormat(48000, 16, 1),  // 48kHz mono (most common)
        new WaveFormat(48000, 16, 2),  // 48kHz stereo
        new WaveFormat(44100, 16, 1),  // 44.1kHz mono
        new WaveFormat(16000, 16, 1)   // 16kHz mono (Azure STT)
    ];
    
    // Buffer and timing constants
    private const int BufferMilliseconds = 20;
    private const int OutgoingBufferSeconds = 3;
    private const int IncomingBufferSeconds = 2;
    private const int SilencePreFillMilliseconds = 100;
    private const int AudioWatchdogDelaySeconds = 3;
    private const int StopTimeoutSeconds = 3;
    private const int AudioLevelLogInterval = 50; // Log every 50 buffers (~1 second)
    private const int InitialLogEventCount = 5;
    
    private readonly DeviceService _deviceService;
    private readonly TranslationLogger _logger;
    private CancellationTokenSource? _cts;
    
    // Outgoing (Your voice → Meeting)
    private WaveInEvent? _outgoingMicCapture;
    private BufferedWaveProvider? _outgoingBuffer;
    private WasapiOut? _outgoingPlayer;
    private SpeechRecognizer? _outgoingRecognizer;
    
    // Incoming (Meeting audio → You)
    private WaveInEvent? _incomingCableCapture;
    private BufferedWaveProvider? _incomingBuffer;
    private WasapiOut? _incomingPlayer;
    private PushAudioInputStream? _incomingAudioStream;
    private SpeechRecognizer? _incomingRecognizer;
    
    // Mute states
    private volatile bool _isMicrophoneMuted;
    private volatile bool _isSpeakerMuted;
    
    // Duplicate detection
    private TranscriptDuplicateManager _outgoingTranscriptManager;
    private TranscriptDuplicateManager _incomingTranscriptManager;
    
    private bool _disposed;
    private Task? _runningTask;

    #region ITranslationService Implementation

    public bool IsRunning => _cts != null && !_cts.Token.IsCancellationRequested;

    public event EventHandler<string>? LogMessage;
    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<TranslationEventArgs>? TranslationReceived;
    public event EventHandler<SystemMessageEventArgs>? SystemMessage;
    public event EventHandler<SynthesizingEventArgs>? SynthesizingStatusChanged;

    public BypassTranslationService()
    {
        _deviceService = new DeviceService();
        _logger = new TranslationLogger(LogRaw, LogLevel.Info);
        _outgoingTranscriptManager = new TranscriptDuplicateManager(msg => _logger.Debug(LogCategory.Duplicate, msg));
        _incomingTranscriptManager = new TranscriptDuplicateManager(msg => _logger.Debug(LogCategory.Duplicate, msg));
    }

    public async Task StartTranslationAsync(TranslationSettings settings)
    {
        ThrowIfDisposed();
        
        if (IsRunning)
        {
            _logger.Warning(LogCategory.System, "Bypass service already running!");
            return;
        }

        _logger.Info(LogCategory.System, $"Starting Bypass Mode - Language: {settings.SourceLanguage}");

        _cts = new CancellationTokenSource();
        
        await _outgoingTranscriptManager.ClearAsync().ConfigureAwait(false);
        await _incomingTranscriptManager.ClearAsync().ConfigureAwait(false);

        var outgoingTask = Task.Run(() => OutgoingFlow(settings, _cts.Token));
        var incomingTask = Task.Run(() => IncomingFlow(settings, _cts.Token));
        _runningTask = Task.WhenAll(outgoingTask, incomingTask);

        try
        {
            await _runningTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) 
        {
            _logger.Info(LogCategory.System, "Bypass service cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.System, $"Bypass service error: {ex.Message}");
        }
        finally
        {
            await CleanupAsync().ConfigureAwait(false);
        }
    }

    public async Task StopTranslationAsync()
    {
        ThrowIfDisposed();
        
        if (_cts == null || _cts.Token.IsCancellationRequested)
        {
            _logger.Info(LogCategory.System, "Bypass service not running");
            return;
        }

        _logger.Info(LogCategory.System, "Stopping Bypass Mode");

        // Cancel token
        _cts.Cancel();

        // Wait for flows to stop
        var running = _runningTask;
        if (running != null)
        {
            try
            {
                await running.WaitAsync(TimeSpan.FromSeconds(StopTimeoutSeconds)).ConfigureAwait(false);
                _logger.Debug(LogCategory.System, "Service stopped gracefully!");
            }
            catch (TimeoutException)
            {
                _logger.Warning(LogCategory.System, "Stop timeout - forcing termination");
            }
        }
    }

    public void SetMicrophoneMute(bool muted)
    {
        ThrowIfDisposed();
        _isMicrophoneMuted = muted;
        _logger.Info(LogCategory.System, muted ? "🔇 Microphone MUTED" : "🔊 Microphone UNMUTED");
    }

    public void SetSpeakerMute(bool muted)
    {
        ThrowIfDisposed();
        _isSpeakerMuted = muted;
        _logger.Info(LogCategory.System, muted ? "🔇 Speaker MUTED" : "🔊 Speaker UNMUTED");
    }

    #endregion

    #region Outgoing Flow (Your Voice → Meeting)

    private async Task OutgoingFlow(TranslationSettings settings, CancellationToken ct)
    {
        try
        {
            _logger.Info(LogCategory.Outgoing, "[BYPASS OUT] Initializing...");

            // Find devices
            var physicalMic = _deviceService.FindPhysicalMicrophone();
            var cableOutput = _deviceService.FindOutgoingCableDevice();

            if (!ValidateDevice(physicalMic, "Physical microphone", "[BYPASS OUT]") ||
                !ValidateDevice(cableOutput, "CABLE output device", "[BYPASS OUT]"))
            {
                return;
            }
          

            _logger.Debug(LogCategory.Device, $"[BYPASS OUT] Mic: {physicalMic?.FriendlyName}");           
            _logger.Debug(LogCategory.Device, $"[BYPASS OUT] CABLE: {cableOutput?.FriendlyName}");

            // Find and test microphone device
            var deviceNumber = FindPhysicalMicrophoneDeviceNumber(physicalMic.FriendlyName);
            var workingFormat = FindWorkingAudioFormat(deviceNumber, "[BYPASS OUT]");
            
            _outgoingMicCapture = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = workingFormat,
                BufferMilliseconds = BufferMilliseconds
            };
            
            _logger.Debug(LogCategory.Device, $"[BYPASS OUT] Format: {workingFormat.SampleRate}Hz, {workingFormat.BitsPerSample}-bit, {workingFormat.Channels}ch");

            // Setup playback buffer to CABLE
            _outgoingBuffer = new BufferedWaveProvider(workingFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(OutgoingBufferSeconds),
                DiscardOnBufferOverflow = true
            };

            // Initialize CABLE output
            _logger.Debug(LogCategory.Device, "[BYPASS OUT] Initializing CABLE output player...");
            _outgoingPlayer = new WasapiOut(cableOutput, AudioClientShareMode.Shared, false, BufferMilliseconds);
            _outgoingPlayer.Init(_outgoingBuffer);
            
            // Pre-fill buffer with silence
            PreFillBufferWithSilence(_outgoingBuffer, workingFormat);

            // Setup Azure STT - Let Azure capture from microphone directly
            var azureAudioConfig = AudioConfig.FromMicrophoneInput(physicalMic.ID);
            _outgoingRecognizer = CreateSpeechRecognizer(settings, settings.SourceLanguage, azureAudioConfig);
            
            _logger.Debug(LogCategory.Device, $"[BYPASS OUT] Azure using direct microphone input");

            // Setup event handlers
            var dataAvailableCounter = new AudioEventCounter();
            SetupOutgoingAudioDataHandler(ct, dataAvailableCounter);
            SetupOutgoingRecognitionHandlers();

            // Start everything in correct order
            _logger.Debug(LogCategory.Outgoing, "[BYPASS OUT] Starting speech recognizer...");
            await _outgoingRecognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
            
            _logger.Debug(LogCategory.Outgoing, "[BYPASS OUT] Starting microphone recording...");
            try
            {
                _outgoingMicCapture.StartRecording();
                _logger.Debug(LogCategory.Outgoing, "[BYPASS OUT] Microphone recording started");
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategory.Outgoing, $"[BYPASS OUT] Failed to start microphone: {ex.Message}");
                _logger.Error(LogCategory.Outgoing, $"[BYPASS OUT] Microphone may be in use by another app");
                throw;
            }
            
            _logger.Debug(LogCategory.Outgoing, "[BYPASS OUT] Starting CABLE playback...");
            try
            {
                _outgoingPlayer.Play();
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategory.Outgoing, $"[BYPASS OUT] Failed to start CABLE player: {ex.Message}");
                throw;
            }
            
            _logger.Info(LogCategory.Outgoing, "[BYPASS OUT] Audio routing active - speaking to meeting");

            // Watchdog: Verify DataAvailable events are actually firing
            await VerifyAudioCaptureAsync(dataAvailableCounter, ct).ConfigureAwait(false);

            // Wait until cancelled
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Info(LogCategory.Outgoing, "[BYPASS OUT] Cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.Outgoing, $"[BYPASS OUT] Error: {ex.Message}");
        }
        finally
        {
            // Cleanup
            await CleanupOutgoingResourcesAsync().ConfigureAwait(false);
            _logger.Info(LogCategory.Outgoing, "[BYPASS OUT] Stopped");
        }
    }

    #endregion

    #region Incoming Flow (Meeting Audio → You)

    private async Task IncomingFlow(TranslationSettings settings, CancellationToken ct)
    {
        try
        {
            _logger.Info(LogCategory.Incoming, "[BYPASS IN] Initializing...");

            // Find devices
            var cableInput = _deviceService.FindIncomingCableDevice();
            var physicalSpeaker = _deviceService.FindPhysicalSpeaker();

            if (!ValidateDevice(cableInput, "CABLE input device", "[BYPASS IN]") ||
                !ValidateDevice(physicalSpeaker, "Physical speaker", "[BYPASS IN]"))
            {
                return;
            }

            _logger.Debug(LogCategory.Device, $"[BYPASS IN] CABLE: {cableInput.FriendlyName}");
            _logger.Debug(LogCategory.Device, $"[BYPASS IN] Speaker: {physicalSpeaker.FriendlyName}");

            // Setup audio capture from CABLE
            var deviceNumber = AudioDeviceHelper.GetWaveInDeviceNumber(cableInput.FriendlyName);
            _incomingCableCapture = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = BufferMilliseconds
            };

            // Setup playback buffer to physical speaker
            _incomingBuffer = new BufferedWaveProvider(_incomingCableCapture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(IncomingBufferSeconds),
                DiscardOnBufferOverflow = true
            };

            _incomingPlayer = new WasapiOut(physicalSpeaker, AudioClientShareMode.Shared, false, BufferMilliseconds);
            _incomingPlayer.Init(_incomingBuffer);

            // Setup Azure STT
            _incomingAudioStream = AudioInputStream.CreatePushStream() as PushAudioInputStream;
            var audioConfig = AudioConfig.FromStreamInput(_incomingAudioStream);
            _incomingRecognizer = CreateSpeechRecognizer(settings, settings.TargetLanguage, audioConfig);

            // Setup event handlers
            SetupIncomingAudioDataHandler(ct);
            SetupIncomingRecognitionHandlers();

            // Start everything
            await _incomingRecognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
            _incomingCableCapture.StartRecording();
            _incomingPlayer.Play();

            _logger.Info(LogCategory.Incoming, "[BYPASS IN] Audio routing active - hearing meeting");

            // Wait until cancelled
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Info(LogCategory.Incoming, "[BYPASS IN] Cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.Incoming, $"[BYPASS IN] Error: {ex.Message}");
        }
        finally
        {
            // Cleanup
            await CleanupIncomingResourcesAsync().ConfigureAwait(false);
            _logger.Info(LogCategory.Incoming, "[BYPASS IN] Stopped");
        }
    }

    #endregion

    #region Helper Classes

    private class AudioEventCounter
    {
        private int _count;
        
        public int Count => _count;
        
        public void Increment() => Interlocked.Increment(ref _count);
    }

    #endregion

    #region Helper Methods

    private void LogRaw(string message)
    {
        LogMessage?.Invoke(this, message);
    }

    private int FindPhysicalMicrophoneDeviceNumber(string micName)
    {
        _logger.Debug(LogCategory.Device, $"[BYPASS OUT] Looking for WaveIn device: {micName}");
        _logger.Debug(LogCategory.Device, $"[BYPASS OUT] Available devices: {WaveInEvent.DeviceCount}");
        
        // First pass: strict match for physical microphone
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            var capName = caps.ProductName;
            _logger.Debug(LogCategory.Device, $"[BYPASS OUT]   Device {i}: {capName}");
            
            if (capName.Contains("Microphone", StringComparison.OrdinalIgnoreCase) &&
                !capName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) &&
                !capName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug(LogCategory.Device, $"[BYPASS OUT] Matched microphone - device {i}: {capName}");
                return i;
            }
        }
        
        // Second pass: find first non-CABLE device
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            if (!caps.ProductName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) &&
                !caps.ProductName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning(LogCategory.Device, $"[BYPASS OUT] ⚠️ Using first non-CABLE device {i}: {caps.ProductName}");
                return i;
            }
        }
        
        throw new Exception("Could not find physical microphone in WaveIn device list!");
    }

    private WaveFormat FindWorkingAudioFormat(int deviceNumber, string logPrefix)
    {
        foreach (var format in AudioFormatsToTry)
        {
            try
            {
                _logger.Debug(LogCategory.Device, $"{logPrefix} Testing format: {format.SampleRate}Hz");
                
                using var testCapture = new WaveInEvent
                {
                    DeviceNumber = deviceNumber,
                    WaveFormat = format,
                    BufferMilliseconds = BufferMilliseconds
                };
                
                testCapture.StartRecording();
                testCapture.StopRecording();
                
                _logger.Debug(LogCategory.Device, $"{logPrefix} Using format: {format.SampleRate}Hz");
                return format;
            }
            catch (Exception ex)
            {
                _logger.Debug(LogCategory.Device, $"{logPrefix} Format {format.SampleRate}Hz failed: {ex.Message}");
            }
        }
        
        throw new Exception("No supported audio format found for microphone!");
    }

    private void PreFillBufferWithSilence(BufferedWaveProvider buffer, WaveFormat format)
    {
        var bytesForPreFill = format.AverageBytesPerSecond * SilencePreFillMilliseconds / 1000;
        var silenceBuffer = new byte[bytesForPreFill];
        buffer.AddSamples(silenceBuffer, 0, silenceBuffer.Length);
    }

    private void SetupOutgoingAudioDataHandler(CancellationToken ct, AudioEventCounter counter)
    {
        var sampleCount = 0;
        
        _outgoingMicCapture!.DataAvailable += (s, e) =>
        {
            if (ct.IsCancellationRequested) return;

            counter.Increment();
            sampleCount++;
            
            if (counter.Count <= InitialLogEventCount)
            {
                //_logger.Info(LogCategory.AudioCapture, $"[BYPASS OUT] 🎤 DataAvailable event #{counter.Count} - {e.BytesRecorded} bytes received");
            }
            
            if (sampleCount % AudioLevelLogInterval == 0)
            {
                var maxLevel = CalculateMaxAudioLevel(e.Buffer, e.BytesRecorded);
                //_logger.Info(LogCategory.AudioCapture, $"[BYPASS OUT] Audio level: {maxLevel}/32768 ({e.BytesRecorded} bytes, {counter.Count} events)");
            }

            if (!_isMicrophoneMuted)
            {
                _outgoingBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
        };
    }

    private void SetupIncomingAudioDataHandler(CancellationToken ct)
    {
        _incomingCableCapture!.DataAvailable += (s, e) =>
        {
            if (ct.IsCancellationRequested) return;

            if (!_isSpeakerMuted)
            {
                _incomingBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
                _incomingAudioStream?.Write(e.Buffer, e.BytesRecorded);
            }
        };
    }

    private static int CalculateMaxAudioLevel(byte[] buffer, int bytesRecorded)
    {
        var maxLevel = 0;
        for (int i = 0; i < bytesRecorded - 1; i += 2)
        {
            short sample = BitConverter.ToInt16(buffer, i);
            maxLevel = Math.Max(maxLevel, Math.Abs(sample));
        }
        return maxLevel;
    }

    private void SetupOutgoingRecognitionHandlers()
    {
        _outgoingRecognizer!.Recognizing += (s, e) =>
        {
            if (_isMicrophoneMuted) return; // Skip if muted
            
            if (!string.IsNullOrEmpty(e.Result.Text))
            {
                _logger.Debug(LogCategory.Recognition, $"[BYPASS OUT] Recognizing: {e.Result.Text}");
                InvokeMessageReceived(MessageDirection.Outgoing, e.Result.Text, MessageType.Recognizing, false);
            }
        };

        _outgoingRecognizer.Recognized += async (s, e) =>
        {
            if (_isMicrophoneMuted) return; // Skip if muted
            
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
            {
                var original = e.Result.Text.Trim();

                if (await _outgoingTranscriptManager.TryAddTranscriptAsync(original))
                {
                    _logger.Info(LogCategory.Recognition, $"[BYPASS OUT] You said: {original}");
                    InvokeMessageReceived(MessageDirection.Outgoing, original, MessageType.Recognized, false);
                    //InvokeTranslationReceived(MessageDirection.Outgoing, original, $"[BYPASS] {original}", false);
                }
            }
        };

        _outgoingRecognizer.SessionStarted += (s, e) =>
        {
            _logger.Info(LogCategory.System, "[BYPASS OUT] Session started");
            InvokeSystemMessage("Translation Started", "", SystemMessageType.Started);
        };
    }

    private void SetupIncomingRecognitionHandlers()
    {
        _incomingRecognizer!.Recognizing += (s, e) =>
        {
            if (_isSpeakerMuted) return; // Skip if muted
            
            if (!string.IsNullOrEmpty(e.Result.Text))
            {
                _logger.Debug(LogCategory.Recognition, $"[BYPASS IN] Recognizing: {e.Result.Text}");
                InvokeMessageReceived(MessageDirection.Incoming, e.Result.Text, MessageType.Recognizing, true);
            }
        };

        _incomingRecognizer.Recognized += async (s, e) =>
        {
            if (_isSpeakerMuted) return; // Skip if muted
            
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
            {
                var original = e.Result.Text.Trim();

                if (await _incomingTranscriptManager.TryAddTranscriptAsync(original))
                {
                    _logger.Info(LogCategory.Recognition, $"[BYPASS IN] Meeting said: {original}");
                    InvokeMessageReceived(MessageDirection.Incoming, original, MessageType.Recognized, true);
                    //InvokeTranslationReceived(MessageDirection.Incoming, original, $"[BYPASS] {original}", true);
                }
            }
        };

        _incomingRecognizer.SessionStarted += (s, e) =>
        {
            _logger.Info(LogCategory.System, "[BYPASS IN] Session started");
        };
    }

    private void InvokeMessageReceived(MessageDirection direction, string text, MessageType messageType, bool isFromMeeting)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            MessageReceived?.Invoke(this, new MessageEventArgs
            {
                Direction = direction,
                Text = text,
                MessageType = messageType,
                IsFromMeeting = isFromMeeting
            });
        });
    }

    private void InvokeSystemMessage(string message, string detail, SystemMessageType messageType)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            SystemMessage?.Invoke(this, new SystemMessageEventArgs
            {
                Message = message,
                Detail = detail,
                MessageType = messageType
            });
        });
    }

    private async Task VerifyAudioCaptureAsync(AudioEventCounter counter, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(AudioWatchdogDelaySeconds), ct).ConfigureAwait(false);
        
        if (counter.Count == 0)
        {
            _logger.Error(LogCategory.Outgoing, $"[BYPASS OUT] No audio data received after {AudioWatchdogDelaySeconds}s");
            _logger.Error(LogCategory.Outgoing, $"[BYPASS OUT] Check: Microphone in use, privacy settings, or device access");
            throw new Exception("Microphone not capturing audio");
        }
        
        _logger.Debug(LogCategory.Outgoing, $"[BYPASS OUT] Audio verified: {counter.Count} events in {AudioWatchdogDelaySeconds}s");
    }

    private async Task CleanupAsync()
    {
        _logger.Info(LogCategory.System, "Bypass service stopped");
        
        InvokeSystemMessage("Translation Stopped", "", SystemMessageType.Stopped);

        _runningTask = null;
    }

    private async Task CleanupOutgoingResourcesAsync()
    {
        if (_outgoingRecognizer != null)
        {
            try { await _outgoingRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false); }
            catch { /* Ignore cleanup errors */ }
            _outgoingRecognizer.Dispose();
            _outgoingRecognizer = null;
        }

        _outgoingMicCapture?.StopRecording();
        _outgoingMicCapture?.Dispose();
        _outgoingMicCapture = null;

        _outgoingPlayer?.Stop();
        _outgoingPlayer?.Dispose();
        _outgoingPlayer = null;
    }

    private async Task CleanupIncomingResourcesAsync()
    {
        if (_incomingRecognizer != null)
        {
            try { await _incomingRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false); }
            catch { /* Ignore cleanup errors */ }
            _incomingRecognizer.Dispose();
            _incomingRecognizer = null;
        }

        _incomingCableCapture?.StopRecording();
        _incomingCableCapture?.Dispose();
        _incomingCableCapture = null;

        _incomingPlayer?.Stop();
        _incomingPlayer?.Dispose();
        _incomingPlayer = null;

        _incomingAudioStream?.Dispose();
        _incomingAudioStream = null;
    }

    private bool ValidateDevice(MMDevice? device, string deviceName, string logPrefix)
    {
        if (device == null)
        {
            _logger.Error(logPrefix.Contains("OUT") ? LogCategory.Outgoing : LogCategory.Incoming, 
                         $"{logPrefix} {deviceName} not found!");
            return false;
        }
        return true;
    }

    private SpeechRecognizer CreateSpeechRecognizer(TranslationSettings settings, string language, AudioConfig audioConfig)
    {
        var speechConfig = SpeechConfig.FromSubscription(settings.AzureSubscriptionKey, settings.AzureRegion);
        speechConfig.SpeechRecognitionLanguage = language;
        speechConfig.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
        return new SpeechRecognizer(speechConfig, audioConfig);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BypassTranslationService));
        }
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _logger?.Info(LogCategory.System, "🧹 Disposing BypassTranslationService...");

            try
            {
                _cts?.Cancel();
            }
            catch { /* Ignore cancellation errors during dispose */ }
            
            _cts?.Dispose();
            _outgoingTranscriptManager?.Dispose();
            _incomingTranscriptManager?.Dispose();
            _outgoingMicCapture?.Dispose();
            _outgoingPlayer?.Dispose();
            _outgoingRecognizer?.Dispose();
            _incomingCableCapture?.Dispose();
            _incomingPlayer?.Dispose();
            _incomingRecognizer?.Dispose();
            _incomingAudioStream?.Dispose();

            _logger?.Info(LogCategory.System, "✅ BypassTranslationService disposed");
        }

        _disposed = true;
    }

    #endregion
}
