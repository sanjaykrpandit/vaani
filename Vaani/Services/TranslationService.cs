using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vaani.Models;
using Avalonia.Threading;
using AzureAudioConfig = Microsoft.CognitiveServices.Speech.Audio.AudioConfig;
using System.Threading.Channels;
using vconsole.Services.Logger;

namespace Vaani.Services;

/// <summary>
/// Manages bidirectional real-time translation between two languages using Azure Cognitive Services.
/// Supports parallel voice processing with Channel-based async iteration (no blocking loops).
/// Optimized for low latency with smart microphone pause/resume and enhanced loopback prevention.
/// </summary>
public class TranslationService : ITranslationService
{
    private DeviceService _deviceService;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _outgoingPlaybackLock = new(1, 1);
    private readonly SemaphoreSlim _incomingPlaybackLock = new(1, 1);

    private volatile bool _isPlayingIncomingAudio;
    private Task? _runningTask;

    private Task? _incomingPlaybackTask;
    private Task? _outgoingPlaybackTask;

    private volatile bool _isMicrophoneMuted = false;
    private volatile bool _isSpeakerMuted = false;

    private TranslationRecognizer? _outgoingRecognizer;
    private TranslationRecognizer? _incomingRecognizer;
    private WaveInEvent? _incomingWaveIn;
    private readonly SemaphoreSlim _muteLock = new(1, 1);

    private TranscriptDuplicateManager _outgoingTranscriptManager;
    private TranscriptDuplicateManager _incomingTranscriptManager;

    private int _outgoingMessagesSkipped = 0;
    private int _incomingMessagesSkipped = 0;
    private int _outgoingQueuePeak = 0;
    private int _incomingQueuePeak = 0;

    private bool _disposed;
    private TranslationLogger _logger;

    private Channel<(string original, string translated, SpeechSynthesisResult result)>? _outgoingPlaybackChannel;
    private Channel<(string original, string translated, SpeechSynthesisResult result)>? _incomingPlaybackChannel;

    public event EventHandler<string>? LogMessage;
    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<TranslationEventArgs>? TranslationReceived;
    public event EventHandler<SystemMessageEventArgs>? SystemMessage;
    public event EventHandler<SynthesizingEventArgs>? SynthesizingStatusChanged;

    private int _lowConfidenceCount = 0;
    private int _totalRecognitions = 0;

    private volatile bool _outgoingSessionNotified = false;
    private volatile bool _incomingSessionNotified = false;

    // ✅ OPTIMIZATION #1: Smart microphone pause - track user speaking state
    private volatile bool _isUserSpeaking = false;

    // ✅ LOOPBACK PREVENTION: Enhanced protection flag and lock
    private volatile bool _microphonePausedForPlayback = false;
    private readonly SemaphoreSlim _microphoneControlLock = new(1, 1);

    private volatile bool _isOutgoingSynthesizing = false;
    private volatile bool _isIncomingSynthesizing = false;

    public TranslationService()
    {
        _deviceService = new DeviceService();
        _logger = new TranslationLogger(LogRaw, LogLevel.Info);
        _outgoingTranscriptManager = new TranscriptDuplicateManager(msg => _logger.Debug(LogCategory.Duplicate, msg));
        _incomingTranscriptManager = new TranscriptDuplicateManager(msg => _logger.Debug(LogCategory.Duplicate, msg));
    }

    public void SetLogLevel(LogLevel level)
    {
        _logger.SetMinLogLevel(level);
        _logger.Info(LogCategory.System, $"Log level set to: {level}");
    }

    public void SetLogCategories(params LogCategory[]? categories)
    {
        _logger.SetEnabledCategories(categories);
        if (categories == null)
            _logger.Info(LogCategory.System, "All log categories enabled");
        else
            _logger.Info(LogCategory.System, $"Enabled categories: {string.Join(", ", categories)}");
    }

    public bool IsRunning => _cts != null && !_cts.Token.IsCancellationRequested;

    public async void SetMicrophoneMute(bool muted)
    {
        if (_isMicrophoneMuted == muted) return;
        _isMicrophoneMuted = muted;

        await _muteLock.WaitAsync();
        try
        {
            if (muted)
            {
                _logger.Info(LogCategory.System, "Microphone MUTING - Stopping your microphone...");

                if (_outgoingRecognizer != null)
                {
                    try
                    {
                        await _outgoingRecognizer.StopContinuousRecognitionAsync();
                        _logger.Info(LogCategory.Outgoing, "Your microphone paused");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(LogCategory.Outgoing, $"Error pausing microphone: {ex.Message}");
                    }
                }

                _logger.Info(LogCategory.System, "🔇 Microphone MUTED (Meeting audio continues)");
            }
            else
            {
                _logger.Info(LogCategory.System, "Microphone UNMUTING - Resuming your microphone...");

                if (_outgoingRecognizer != null)
                {
                    try
                    {
                        await _outgoingRecognizer.StartContinuousRecognitionAsync();
                        _logger.Info(LogCategory.Outgoing, "Your microphone resumed");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(LogCategory.Outgoing, $"Error resuming microphone: {ex.Message}");
                    }
                }
                else
                {
                    _logger.Warning(LogCategory.Outgoing, "Cannot resume - microphone recognizer is null");
                }

                _logger.Info(LogCategory.System, "🔊 Microphone UNMUTED - Speak now!");
            }
        }
        finally
        {
            _muteLock.Release();
        }
    }

    public async void SetSpeakerMute(bool muted)
    {
        if (_isSpeakerMuted == muted) return;
        _isSpeakerMuted = muted;

        await _muteLock.WaitAsync();
        try
        {
            if (muted)
            {
                _logger.Info(LogCategory.System, "Speaker MUTING - Stopping meeting audio capture...");

                if (_incomingWaveIn != null)
                {
                    try
                    {
                        _incomingWaveIn.StopRecording();
                        _logger.Info(LogCategory.Incoming, "Meeting audio capture paused");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(LogCategory.Incoming, $"Error pausing audio capture: {ex.Message}");
                    }
                }

                if (_incomingRecognizer != null)
                {
                    try
                    {
                        await _incomingRecognizer.StopContinuousRecognitionAsync();
                        _logger.Info(LogCategory.Incoming, "Meeting recognition paused");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(LogCategory.Incoming, $"Error pausing recognition: {ex.Message}");
                    }
                }

                _logger.Info(LogCategory.System, "🔇 Speaker MUTED (Your microphone continues)");
            }
            else
            {
                _logger.Info(LogCategory.System, "Speaker UNMUTING - Resuming meeting audio capture...");

                if (_incomingRecognizer != null)
                {
                    try
                    {
                        await _incomingRecognizer.StartContinuousRecognitionAsync();
                        _logger.Info(LogCategory.Incoming, "Meeting recognition resumed");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(LogCategory.Incoming, $"Error resuming recognition: {ex.Message}");
                    }
                }
                else
                {
                    _logger.Warning(LogCategory.Incoming, "Cannot resume - meeting recognizer is null");
                }

                if (_incomingWaveIn != null)
                {
                    try
                    {
                        _incomingWaveIn.StartRecording();
                        _logger.Info(LogCategory.Incoming, "Meeting audio capture resumed");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(LogCategory.Incoming, $"Error resuming audio capture: {ex.Message}");
                    }
                }
                else
                {
                    _logger.Warning(LogCategory.Incoming, "Cannot resume - WaveIn is null");
                }

                _logger.Info(LogCategory.System, "🔊 Speaker UNMUTED - Meeting audio playing!");
            }
        }
        finally
        {
            _muteLock.Release();
        }
    }

    public async Task StartTranslationAsync(TranslationSettings settings)
    {
        if (IsRunning)
        {
            _logger.Warning(LogCategory.System, "Translation is already running!");
            return;
        }

        if (settings == null || string.IsNullOrWhiteSpace(settings.AzureSubscriptionKey) || string.IsNullOrWhiteSpace(settings.AzureRegion))
        {
            _logger.Error(LogCategory.System, "Invalid translation settings!");
            return;
        }

        _logger.Info(LogCategory.System, "═════════════════════════════════════════");
        _logger.Info(LogCategory.System, "║  Starting Bidirectional Translation    ║");
        _logger.Info(LogCategory.System, "═════════════════════════════════════════");
        _logger.Info(LogCategory.System, $"Source: {settings.SourceLanguage} ↔ Target: {settings.TargetLanguage}");
        _logger.Info(LogCategory.System, $"Azure Region: {settings.AzureRegion}");
        _logger.Debug(LogCategory.System, $"Azure Key: {TextProcessingHelper.MaskKey(settings.AzureSubscriptionKey)}");
        _logger.Info(LogCategory.System, $"Source Voice: {settings.SourceVoice}");
        _logger.Info(LogCategory.System, $"Target Voice: {settings.TargetVoice}");

        _cts = new CancellationTokenSource();

        _outgoingSessionNotified = false;
        _incomingSessionNotified = false;

      
        await _outgoingTranscriptManager.ClearAsync();
        await _incomingTranscriptManager.ClearAsync();

        _outgoingMessagesSkipped = 0;
        _incomingMessagesSkipped = 0;
        _outgoingQueuePeak = 0;
        _incomingQueuePeak = 0;

        // ✅ Reset optimization flags
        _isUserSpeaking = false;
        _microphonePausedForPlayback = false;

        _outgoingPlaybackChannel = Channel.CreateUnbounded<(string, string, SpeechSynthesisResult)>(new UnboundedChannelOptions { SingleReader = true });
        _incomingPlaybackChannel = Channel.CreateUnbounded<(string, string, SpeechSynthesisResult)>(new UnboundedChannelOptions { SingleReader = true });

        var outgoingTask = Task.Run(() => OutgoingFlow(settings, _cts.Token));
        var incomingTask = Task.Run(() => IncomingFlow(settings, _cts.Token));
        _runningTask = Task.WhenAll(outgoingTask, incomingTask);

        try
        {
            await _runningTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.System, $"Translation error: {ex.Message}");
        }
        finally
        {
            _logger.Info(LogCategory.System, "Translation has stopped.");
            LogMetrics();

            SystemMessage?.Invoke(this, new SystemMessageEventArgs
            {
                Message = "Translation Stopped",
                Detail = "",
                MessageType = SystemMessageType.Stopped
            });

            _runningTask = null;

            _outgoingPlaybackChannel?.Writer.Complete();
            _incomingPlaybackChannel?.Writer.Complete();
            _outgoingPlaybackChannel = null;
            _incomingPlaybackChannel = null;
        }
    }

    public async Task StopTranslationAsync()
    {
        if (_cts == null || _cts.Token.IsCancellationRequested)
        {
            _logger.Info(LogCategory.System, "Translation is not running or already stopping");
            return;
        }

        _logger.Info(LogCategory.System, "╔══════════════════════════════════════════════════════════╗");
        _logger.Info(LogCategory.System, "║           STOPPING TRANSLATION SERVICE                   ║");
        _logger.Info(LogCategory.System, "╚══════════════════════════════════════════════════════════╝");

        // 1. Log synthesis status (but don't wait - ViewModel already did)
        if (_isOutgoingSynthesizing || _isIncomingSynthesizing)
        {
            _logger.Info(LogCategory.System, "ℹ️ Synthesis status:");
            _logger.Info(LogCategory.System, $"   Outgoing: {(_isOutgoingSynthesizing ? "active" : "idle")}");
            _logger.Info(LogCategory.System, $"   Incoming: {(_isIncomingSynthesizing ? "active" : "idle")}");
        }

        // 2. Stop Azure recognizers FIRST (before cancelling token)
        var stopTasks = new List<Task>();

        if (_outgoingRecognizer != null)
        {
            _logger.Info(LogCategory.Outgoing, "🎤 Stopping outgoing recognizer...");
            stopTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await _outgoingRecognizer.StopContinuousRecognitionAsync();
                    _logger.Info(LogCategory.Outgoing, "✅ Outgoing recognizer stopped");
                }
                catch (Exception ex)
                {
                    _logger.Warning(LogCategory.Outgoing, $"⚠️ Error stopping: {ex.Message}");
                }
            }));
        }

        if (_incomingRecognizer != null)
        {
            _logger.Info(LogCategory.Incoming, "🎤 Stopping incoming recognizer...");
            stopTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await _incomingRecognizer.StopContinuousRecognitionAsync();
                    _logger.Info(LogCategory.Incoming, "✅ Incoming recognizer stopped");
                }
                catch (Exception ex)
                {
                    _logger.Warning(LogCategory.Incoming, $"⚠️ Error stopping: {ex.Message}");
                }
            }));
        }

        if (_incomingWaveIn != null)
        {
            _logger.Info(LogCategory.Incoming, "🎙️ Stopping audio capture...");
            stopTasks.Add(Task.Run(() =>
            {
                try
                {
                    _incomingWaveIn.StopRecording();
                    _logger.Info(LogCategory.Incoming, "✅ Audio capture stopped");
                }
                catch (Exception ex)
                {
                    _logger.Warning(LogCategory.Incoming, $"⚠️ Error stopping: {ex.Message}");
                }
            }));
        }

        // Wait for recognizers to stop (with timeout)
        if (stopTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(stopTasks).WaitAsync(TimeSpan.FromSeconds(3));
                _logger.Info(LogCategory.System, "✅ All recognizers stopped");
            }
            catch (TimeoutException)
            {
                _logger.Warning(LogCategory.System, "⚠️ Recognizer stop timed out");
            }
        }

        // 3. Clear and close playback channels
        try
        {
            // Clear outgoing channel
            if (_outgoingPlaybackChannel != null)
            {
                _logger.Info(LogCategory.Outgoing, "🧹 Clearing outgoing playback queue...");
                _outgoingPlaybackChannel.Writer.TryComplete();

                int outgoingCleared = 0;
                while (_outgoingPlaybackChannel.Reader.TryRead(out _))
                {
                    outgoingCleared++;
                }

                if (outgoingCleared > 0)
                {
                    _logger.Info(LogCategory.Outgoing, $"🗑️ Cleared {outgoingCleared} queued item(s)");
                }
            }

            // Clear incoming channel
            if (_incomingPlaybackChannel != null)
            {
                _logger.Info(LogCategory.Incoming, "🧹 Clearing incoming playback queue...");
                _incomingPlaybackChannel.Writer.TryComplete();

                int incomingCleared = 0;
                while (_incomingPlaybackChannel.Reader.TryRead(out _))
                {
                    incomingCleared++;
                }

                if (incomingCleared > 0)
                {
                    _logger.Info(LogCategory.Incoming, $"🗑️ Cleared {incomingCleared} queued item(s)");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.System, $"Error clearing channels: {ex.Message}");
        }

        // 4. Cancel the cancellation token
        _logger.Info(LogCategory.System, "🛑 Cancelling all operations...");
        try
        {
            _cts.Cancel();
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.System, $"Error cancelling: {ex.Message}");
        }

        // 5. Wait for background tasks
        var running = _runningTask;
        if (running != null)
        {
            _logger.Info(LogCategory.System, "⏳ Waiting for flows to terminate (timeout: 5s)...");

            try
            {
                await running.WaitAsync(TimeSpan.FromSeconds(5));
                _logger.Info(LogCategory.System, "✅ All flows terminated gracefully");
            }
            catch (OperationCanceledException)
            {
                _logger.Info(LogCategory.System, "✅ Flows cancelled");
            }
            catch (TimeoutException)
            {
                _logger.Warning(LogCategory.System, "⚠️ Shutdown timed out - forcing termination");
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategory.System, $"❌ Shutdown error: {ex.Message}");
            }
        }

        // 6. Reset flags
        _isOutgoingSynthesizing = false;
        _isIncomingSynthesizing = false;
        _microphonePausedForPlayback = false;

        _logger.Info(LogCategory.System, "╔══════════════════════════════════════════════════════════╗");
        _logger.Info(LogCategory.System, "║      TRANSLATION SERVICE STOPPED SUCCESSFULLY            ║");
        _logger.Info(LogCategory.System, "╚══════════════════════════════════════════════════════════╝");
    }
    public async Task StopTranslationAsyncoooo()
    {
        if (_cts == null || _cts.Token.IsCancellationRequested)
        {
            _logger.Info(LogCategory.System, "Translation is not running or already stopping");
            return;
        }

        _logger.Info(LogCategory.System, "╔══════════════════════════════════════════════════════════╗");
        _logger.Info(LogCategory.System, "║           STOPPING TRANSLATION SERVICE                   ║");
        _logger.Info(LogCategory.System, "╚══════════════════════════════════════════════════════════╝");

        try
        {
            // 1. ✅ CRITICAL: Close channels FIRST - this breaks the await foreach loops
            _logger.Info(LogCategory.System, "🔇 Closing playback channels...");
            try
            {
                _outgoingPlaybackChannel?.Writer.TryComplete();
                _incomingPlaybackChannel?.Writer.TryComplete();
                _logger.Info(LogCategory.System, "✅ Channels closed");
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategory.System, $"Error closing channels: {ex.Message}");
            }

            // 2. Cancel the master token - interrupts audio playback
            _logger.Info(LogCategory.System, "🛑 Triggering cancellation...");
            _cts.Cancel();
            _logger.Info(LogCategory.System, "✅ Cancellation triggered");

            // 3. Reset synthesis flags immediately for UI responsiveness
            _isOutgoingSynthesizing = false;
            _isIncomingSynthesizing = false;
            _microphonePausedForPlayback = false;

            // 4. ✅ NEW: Give a brief moment for immediate cleanup, then return
            //    The finally blocks will complete in background
            _logger.Info(LogCategory.System, "⏱️ Allowing 100ms for immediate cleanup...");
            await Task.Delay(100);

            _logger.Info(LogCategory.System, "✅ Service stop initiated - cleanup continuing in background");
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.System, $"Error during stop: {ex.Message}");
        }

        _logger.Info(LogCategory.System, "╔══════════════════════════════════════════════════════════╗");
        _logger.Info(LogCategory.System, "║      TRANSLATION SERVICE STOPPED                         ║");
        _logger.Info(LogCategory.System, "╚══════════════════════════════════════════════════════════╝");
    }
    public async Task StopTranslationAsyncOld()
    {

        //check is synthesis is ongoing     

        if (_cts != null && !_cts.Token.IsCancellationRequested)
        {
            _logger.Info(LogCategory.System, "Stopping translation...");
            _cts.Cancel();

            var running = _runningTask;
            if (running != null)
            {
                try
                {
                    await running.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException)
                {
                    _logger.Warning(LogCategory.System, "Translation shutdown timed out.");
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategory.System, $"Translation shutdown error: {ex.Message}");
                }
            }
        }
    }

    private async Task OutgoingFlow(TranslationSettings settings, CancellationToken ct)
    {
        SpeechSynthesizer? synthesizer = null;
        AzureAudioConfig? audioConfig = null;

        try
        {
            _logger.Info(LogCategory.Outgoing, "Initializing...");

            string endpoint = $"wss://{settings.AzureRegion}.stt.speech.microsoft.com/speech/universal/v2";
            var config = SpeechTranslationConfig.FromEndpoint(new Uri(endpoint), settings.AzureSubscriptionKey);


            //var endpoint = new Uri("wss://az-ci-speech-vaani-poc-01.cognitiveservices.azure.com/speech/universal/v2");
            //var config = SpeechTranslationConfig.FromEndpoint(endpoint, settings.AzureSubscriptionKey);

            //            var config = SpeechTranslationConfig.FromEndpoint(
            //    new Uri("https://az-ci-speech-vaani-poc-01.cognitiveservices.azure.com/"),
            //    settings.AzureSubscriptionKey
            //);


            //            var config = SpeechTranslationConfig.FromSubscription(
            //    "6eYPCd1m96DyqcZApSLkHAOsvrlUdXhD3Wqxepu3txpkdUZLyEzWJQQJ99CBACGhslBXJ3w3AAAYACOG6iBH",
            //    "centralindia"
            //);
            //var config = SpeechTranslationConfig.FromEndpoint(
            //    new Uri("https://az-ci-speech-vaani-poc-01.cognitiveservices.azure.com/"),
            //    settings.AzureSubscriptionKey
            //);

//            var config = SpeechTranslationConfig.FromSubscription(
//    settings.AzureSubscriptionKey,
//    "centralindia"
//);

//            config.SetProperty(
//                PropertyId.SpeechServiceConnection_Endpoint,
//                "https://az-ci-speech-vaani-poc-01.cognitiveservices.azure.com/"
//            );


            config.SpeechRecognitionLanguage = settings.SourceLanguage;

            var targetLang = settings.TargetLanguage.Split('-')[0];
            config.AddTargetLanguage(targetLang);

            var physicalMic = _deviceService.FindPhysicalMicrophone();

            if (physicalMic != null)
            {
                _logger.Info(LogCategory.Device, $"🎤 Using physical microphone: {physicalMic.FriendlyName}");
                audioConfig = AzureAudioConfig.FromMicrophoneInput(physicalMic.ID);
            }
            else
            {
                _logger.Warning(LogCategory.Device, "Physical microphone not found, using default");
                audioConfig = AzureAudioConfig.FromDefaultMicrophoneInput();
            }

            config.OutputFormat = OutputFormat.Detailed;
            config.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, "Masked");
            config.SetProperty(PropertyId.SpeechServiceResponse_RequestWordLevelTimestamps, "true");
            config.SetProperty(PropertyId.SpeechServiceResponse_RequestSentenceBoundary, "true");
            config.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
            //config.SetProperty(PropertyId.SpeechServiceConnection_LanguageIdMode, "Continuous");


            //config.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, AudioConfiguration.OutgoingInitialSilenceTimeoutMs.ToString());
            //config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs,AudioConfiguration.OutgoingEndSilenceTimeoutMs.ToString());
            //config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs,AudioConfiguration.OutgoingEndSilenceTimeoutMs.ToString());



            _outgoingRecognizer = new TranslationRecognizer(config, audioConfig);

            var speechConfig = SpeechConfig.FromSubscription(settings.AzureSubscriptionKey, settings.AzureRegion);
            speechConfig.SpeechSynthesisLanguage = settings.TargetLanguage;
            speechConfig.SpeechSynthesisVoiceName = settings.TargetVoice;
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
            speechConfig.SetProperty("SpeechSynthesis_ProsodyRate", "1.0");

            var cableDevice = _deviceService.FindOutgoingCableDevice();
            if (cableDevice != null)
            {
                _logger.Info(LogCategory.Device, $"🔊 CABLE device found: {cableDevice.FriendlyName}");
            }
            else
            {
                _logger.Warning(LogCategory.Device, "CABLE device not found!");
            }

            synthesizer = new SpeechSynthesizer(speechConfig, null);

            await AzureSpeechSynthesisHelper.WarmupAzureConnection(synthesizer, ct, msg => _logger.Info(LogCategory.Connection, msg));

            _outgoingPlaybackTask = ProcessOutgoingPlaybackAsync(cableDevice, ct);

            // ✅ OPTIMIZATION #1: Track user speaking state for smart pause
            _outgoingRecognizer.Recognizing += (s, e) =>
            {
                // ✅ LOOPBACK PROTECTION: Ignore recognitions during incoming playback
                if (_microphonePausedForPlayback)
                {
                    _logger.Debug(LogCategory.Recognition, $"[OUT] Ignoring recognition during playback (loopback protection)");
                    return;
                }

                // Track speaking state
                _isUserSpeaking = !string.IsNullOrEmpty(e.Result.Text);

                if (_isUserSpeaking)
                {
                    _logger.Debug(LogCategory.Recognition, $"[OUT] Recognizing: {e.Result.Text}");

                    // ✅ OPTIMIZATION #2: Only marshal event invocation to UI thread
                    _ = Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        MessageReceived?.Invoke(this, new MessageEventArgs
                        {
                            Direction = MessageDirection.Outgoing,
                            Text = e.Result.Text,
                            MessageType = MessageType.Recognizing,
                            IsFromMeeting = false
                        });
                    });
                }
            };

            _outgoingRecognizer.Recognized += async (s, e) =>
            {
                // Reset speaking state on recognition completion
                _isUserSpeaking = false;

                if (ct.IsCancellationRequested) return;

                // ✅ LOOPBACK PROTECTION: Ignore recognitions during incoming playback
                if (_microphonePausedForPlayback)
                {
                    _logger.Debug(LogCategory.Recognition, $"[OUT] Ignoring recognized text during playback (loopback protection): {e.Result.Text}");
                    return;
                }

                if (e.Result.Reason == ResultReason.TranslatedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                {
                    var original = e.Result.Text.Trim();

                    Interlocked.Increment(ref _totalRecognitions);

                    try
                    {
                        var jsonResult = e.Result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
                        _logger.Debug(LogCategory.Recognition, $"[OUT] Confidence JSON: {jsonResult}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(LogCategory.Recognition, $"[OUT] Error parsing confidence: {ex.Message}");
                    }


                    // Check for duplicate transcript entries before proceeding with synthesis and events                  
                    if (await _outgoingTranscriptManager.TryAddTranscriptAsync(original) && e.Result.Translations.TryGetValue(targetLang, out var translated))          
                    {
                        var translatedForSynthesis = TextProcessingHelper.CleanTextForSynthesis(translated);
                        if (string.IsNullOrWhiteSpace(translatedForSynthesis))
                        {
                            _logger.Warning(LogCategory.Synthesis, $"[OUT] Text empty after cleaning: '{translated}'");
                            return;
                        }

                        _logger.Info(LogCategory.Recognition, $"[OUT] You said: {original}");
                        _logger.Info(LogCategory.Recognition, $"[OUT] Translated: {translated}");

                        // ✅ OPTIMIZATION #2: Only marshal event invocation to UI thread
                        _ = Dispatcher.UIThread.InvokeAsync(() =>
                        {
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
                        });

                        var synthStartTime = DateTime.UtcNow;

                        // ✅ OPTIMIZATION #4: Use Task.Factory.StartNew for immediate execution
                        _ = Task.Factory.StartNew(async () =>
                        {
                            try
                            {
                                var result = await AzureSpeechSynthesisHelper.SynthesizeWithRetry(synthesizer, translatedForSynthesis, ct,
                                    msg => _logger.Debug(LogCategory.Synthesis, msg));
                                var synthDuration = (DateTime.UtcNow - synthStartTime).TotalMilliseconds;

                                if (result?.Reason == ResultReason.SynthesizingAudioCompleted)
                                {
                                    _logger.Info(LogCategory.Synthesis, $"[OUT] Completed in {synthDuration:F0}ms");
                                    await _outgoingPlaybackChannel!.Writer.WriteAsync((original, translatedForSynthesis, result), ct);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(LogCategory.Synthesis, $"[OUT] Synthesis error: {ex.Message}");
                            }
                        }, ct, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
                    }
                }
            };

            _outgoingRecognizer.Canceled += (s, e) =>
            {
                if (e.Reason == CancellationReason.Error)
                {
                    _logger.Error(LogCategory.Recognition, $"[OUT] Recognition ERROR: {e.ErrorCode} - {e.ErrorDetails}");

                    _ = Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // Event invocation only
                    });
                }
            };

            _outgoingRecognizer.SessionStarted += (s, e) =>
            {
                if (!_outgoingSessionNotified)
                {
                    _outgoingSessionNotified = true;

                    _logger.Info(LogCategory.System, "[OUT] ✅ Session started - Listening to your microphone...");

                    _ = Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SystemMessage?.Invoke(this, new SystemMessageEventArgs
                        {
                            Message = "Translation Started",
                            Detail = "Speak to translate",
                            MessageType = SystemMessageType.Started
                        });
                    });
                }
                else
                {
                    _logger.Debug(LogCategory.System, "[OUT] Session resumed after microphone pause");
                }
            };

            if (!_isMicrophoneMuted)
            {
                await _outgoingRecognizer.StartContinuousRecognitionAsync();
            }
            else
            {
                _logger.Info(LogCategory.Outgoing, "Started in MUTED state");
            }

            await _outgoingPlaybackTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.Outgoing, $"EXCEPTION: {ex.Message}");
        }
        finally
        {
            if (_outgoingRecognizer != null)
            {
                try
                {
                    await _outgoingRecognizer.StopContinuousRecognitionAsync();
                }
                catch { }

                _outgoingRecognizer.Dispose();
                _outgoingRecognizer = null;
            }

            audioConfig?.Dispose();
            synthesizer?.Dispose();

            _logger.Info(LogCategory.Outgoing, "Stopped");
        }
    }

    private async Task ProcessOutgoingPlaybackAsync(MMDevice? cableDevice, CancellationToken ct)
    {
        _logger.Info(LogCategory.Outgoing, "[OUT] 🎵 Playback processor started");

        try
        {
            await foreach (var item in _outgoingPlaybackChannel!.Reader.ReadAllAsync(ct))
            {
                if (ct.IsCancellationRequested)
                {
                    _logger.Info(LogCategory.Outgoing, "[OUT] ⏹️ Cancellation requested - stopping playback");
                    break;
                }

                if (_isSpeakerMuted)
                {
                    _logger.Debug(LogCategory.Outgoing, "[OUT] 🔇 Skipping playback - speaker muted");
                    continue;
                }

                try
                {
                    await _outgoingPlaybackLock.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    _logger.Info(LogCategory.Outgoing, "[OUT] ⏹️ Playback lock acquisition cancelled");
                    break;
                }

                try
                {
                    // Mark synthesis as active
                    _isOutgoingSynthesizing = true;

                    var audioDurationMs = AudioPlaybackManager.CalculateAudioDuration(
                        item.result.AudioData.Length,
                        AudioConfiguration.SampleRate,
                        AudioConfiguration.Channels,
                        AudioConfiguration.BitsPerSample);

                    _logger.Info(LogCategory.Playback, $"[OUT] 🔊 Starting playback ({audioDurationMs:F0}ms) to meeting...");
                    _logger.Debug(LogCategory.Playback, $"[OUT] Original: '{item.original}' → Translated: '{item.translated}'");

                    // Notify UI that synthesis started
                    SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                    {
                        OriginalText = item.original,
                        TranslatedText = item.translated,
                        IsFromMeeting = false,
                        IsSynthesizing = true
                    });

                    // Play audio (this will be cancelled if ct is triggered)
                    await AudioPlaybackManager.PlayAudioToCableDeviceAsync(item.result.AudioData, cableDevice, ct);

                    _logger.Info(LogCategory.Playback, $"[OUT] ✅ Playback completed successfully ({audioDurationMs:F0}ms)");

                    // Notify UI that synthesis completed
                    SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                    {
                        OriginalText = item.original,
                        TranslatedText = item.translated,
                        IsFromMeeting = false,
                        IsSynthesizing = false
                    });
                }
                catch (OperationCanceledException)
                {
                    _logger.Warning(LogCategory.Playback, "[OUT] ⚠️ Playback cancelled mid-stream");

                    // Notify UI that synthesis was interrupted
                    SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                    {
                        OriginalText = item.original,
                        TranslatedText = item.translated,
                        IsFromMeeting = false,
                        IsSynthesizing = false
                    });

                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategory.Playback, $"[OUT] ❌ Playback error: {ex.Message}");

                    // Notify UI of error
                    SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                    {
                        OriginalText = item.original,
                        TranslatedText = item.translated,
                        IsFromMeeting = false,
                        IsSynthesizing = false
                    });
                }
                finally
                {
                    _isOutgoingSynthesizing = false;
                    _outgoingPlaybackLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info(LogCategory.Outgoing, "[OUT] ⏹️ Playback processor cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.Outgoing, $"[OUT] ❌ Playback processor error: {ex.Message}");
        }
        finally
        {
            _isOutgoingSynthesizing = false;
            _logger.Info(LogCategory.Outgoing, "[OUT] 🛑 Playback processor finished");
        }
    }
  
    private async Task IncomingFlow(TranslationSettings settings, CancellationToken ct)
    {
        SpeechSynthesizer? synthesizer = null;
        PushAudioInputStream? pushStream = null;
        int audioPassCount = 0;

        try
        {
            _logger.Info(LogCategory.Incoming, "Initializing...");

            var cableDevice = _deviceService.FindIncomingCableDevice();

            if (cableDevice == null)
            {
                _logger.Error(LogCategory.Device, "[IN] CABLE device NOT FOUND");
                return;
            }

            _logger.Info(LogCategory.Device, $"[IN] CABLE device: {cableDevice.FriendlyName}");

            var physicalSpeaker = _deviceService.FindPhysicalSpeaker();
            if (physicalSpeaker != null)
            {
                _logger.Info(LogCategory.Device, $"[IN] 🔊 Using speaker: {physicalSpeaker.FriendlyName}");
            }
            else
            {
                _logger.Warning(LogCategory.Device, "[IN] Physical speaker not found, will use default");
            }

            string endpoint = $"wss://{settings.AzureRegion}.stt.speech.microsoft.com/speech/universal/v2";
            var config = SpeechTranslationConfig.FromEndpoint(new Uri(endpoint), settings.AzureSubscriptionKey);

            //var endpoint = new Uri("wss://az-ci-speech-vaani-poc-01.cognitiveservices.azure.com/speech/universal/v2");
            //var config = SpeechTranslationConfig.FromEndpoint(endpoint, settings.AzureSubscriptionKey);

            //            var config = SpeechTranslationConfig.FromEndpoint(
            //    new Uri("https://az-ci-speech-vaani-poc-01.cognitiveservices.azure.com/"),
            //    settings.AzureSubscriptionKey
            //);

            //            var config = SpeechTranslationConfig.FromSubscription(
            //"6eYPCd1m96DyqcZApSLkHAOsvrlUdXhD3Wqxepu3txpkdUZLyEzWJQQJ99CBACGhslBXJ3w3AAAYACOG6iBH",
            //"centralindia"
            //);

            //            var config = SpeechTranslationConfig.FromEndpoint(
            //    new Uri("https://az-ci-speech-vaani-poc-01.cognitiveservices.azure.com/"),
            //    settings.AzureSubscriptionKey
            //);


//            var config = SpeechTranslationConfig.FromSubscription(
//    settings.AzureSubscriptionKey,
//    "centralindia"
//);

//            config.SetProperty(
//                PropertyId.SpeechServiceConnection_Endpoint,
//                "https://az-ci-speech-vaani-poc-01.cognitiveservices.azure.com/"
//            );

            config.SpeechRecognitionLanguage = settings.TargetLanguage;

            var targetLang = settings.SourceLanguage.Split('-')[0];
            config.AddTargetLanguage(targetLang);

            config.OutputFormat = OutputFormat.Detailed;
            config.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, "Masked");
            config.SetProperty(PropertyId.SpeechServiceResponse_RequestWordLevelTimestamps, "true");
            config.SetProperty(PropertyId.SpeechServiceResponse_RequestSentenceBoundary, "true");
            config.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
            //config.SetProperty(PropertyId.SpeechServiceConnection_LanguageIdMode, "Continuous");


            //config.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, AudioConfiguration.InitialSilenceTimeoutMs.ToString());
            //config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, AudioConfiguration.EndSilenceTimeoutMs.ToString());
            //config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, AudioConfiguration.SegmentationSilenceTimeoutMs.ToString());


            var deviceNumber = AudioDeviceHelper.GetWaveInDeviceNumber(cableDevice.FriendlyName);
            _incomingWaveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(AudioConfiguration.SampleRate, AudioConfiguration.BitsPerSample, AudioConfiguration.Channels),
                BufferMilliseconds = 20
            };

            pushStream = AudioInputStream.CreatePushStream() as PushAudioInputStream;
            var audioConfig = AzureAudioConfig.FromStreamInput(pushStream);

            _incomingRecognizer = new TranslationRecognizer(config, audioConfig);

            var speechConfig = SpeechConfig.FromSubscription(settings.AzureSubscriptionKey, settings.AzureRegion);
            speechConfig.SpeechSynthesisLanguage = settings.SourceLanguage;
            speechConfig.SpeechSynthesisVoiceName = settings.SourceVoice;
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
            speechConfig.SetProperty(PropertyId.SpeechServiceResponse_RequestSentenceBoundary, "true");
            speechConfig.SetProperty("SpeechSynthesis_ProsodyRate", "1");

            synthesizer = new SpeechSynthesizer(speechConfig, null);

            await AzureSpeechSynthesisHelper.WarmupAzureConnection(synthesizer, ct, msg => _logger.Info(LogCategory.Connection, msg));

            _incomingPlaybackTask = ProcessIncomingPlaybackAsync(physicalSpeaker, ct);

            // ✅ OPTIMIZATION #3: Optimized audio level computation
            _incomingWaveIn.DataAvailable += (s, e) =>
            {
                if (ct.IsCancellationRequested) return;

                // Push audio to Azure immediately to lower latency
                pushStream?.Write(e.Buffer, e.BytesRecorded);

                // Lightweight, throttled telemetry (only when needed)
                audioPassCount++;
                if (audioPassCount % 300 == 0) // Less frequent
                {
                    // Compute coarse level using every 16th sample to reduce CPU
                    float audioLevel = 0;
                    for (int i = 0; i < e.BytesRecorded; i += 32) // 2 bytes per sample * 16
                    {
                        if (i + 1 < e.BytesRecorded)
                        {
                            short sample = BitConverter.ToInt16(e.Buffer, i);
                            audioLevel = Math.Max(audioLevel, Math.Abs((float)sample) / 32768f);
                        }
                    }
                    _logger.Debug(LogCategory.AudioCapture, $"[IN] Level: {audioLevel:F3}");
                    audioPassCount = 0;
                }
            };

            _incomingRecognizer.Recognizing += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Result.Text))
                {
                    _logger.Debug(LogCategory.Recognition, $"[IN] Recognizing: {e.Result.Text}");

                    // ✅ OPTIMIZATION #2: Only marshal event invocation to UI thread
                    _ = Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        MessageReceived?.Invoke(this, new MessageEventArgs
                        {
                            Direction = MessageDirection.Incoming,
                            Text = e.Result.Text,
                            MessageType = MessageType.Recognizing,
                            IsFromMeeting = true
                        });
                    });
                }
            };

            _incomingRecognizer.Recognized += async (s, e) =>
            {
                if (ct.IsCancellationRequested) return;

                if (e.Result.Reason == ResultReason.TranslatedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                {
                    var original = e.Result.Text.Trim();

                    Interlocked.Increment(ref _totalRecognitions);

                    //var lidResult = AutoDetectSourceLanguageResult.FromResult(e.Result);
                    //string actualLanguage = lidResult.Language;
                    //bool isEnglish = actualLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);

                    //if (!isEnglish)
                    //{
                    //    _logger.Info(LogCategory.Playback, $"Non-English detected ({actualLanguage}). Applying special playback logic...");
                    //}
                    //else
                    //{
                    //    _logger.Debug(LogCategory.Recognition, "[IN] English detected. Using standard playback.");
                    //}

                    //try
                    //{
                    //    var jsonResult = e.Result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
                    //    _logger.Debug(LogCategory.Recognition, $"[IN] Confidence JSON: {jsonResult}");
                    //}
                    //catch (Exception ex)
                    //{
                    //    _logger.Debug(LogCategory.Recognition, $"[IN] Error parsing confidence: {ex.Message}");
                    //}

                    if (!await _incomingTranscriptManager.TryAddTranscriptAsync(original))
                    {
                        return;
                    }

                    if (e.Result.Translations.TryGetValue(targetLang, out var translated))
                    {
                        var translatedForSynthesis = TextProcessingHelper.CleanTextForSynthesis(translated);

                        if (string.IsNullOrWhiteSpace(translatedForSynthesis))
                        {
                            _logger.Warning(LogCategory.Synthesis, $"[IN] Text empty after cleaning: '{translated}'");
                            return;
                        }

                        _logger.Info(LogCategory.Recognition, $"[IN] Meeting said: {original}");
                        _logger.Info(LogCategory.Recognition, $"[IN] Translated: {translated}");

                        // ✅ OPTIMIZATION #2: Only marshal event invocation to UI thread
                        _ = Dispatcher.UIThread.InvokeAsync(() =>
                        {
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

                        });

                        var synthStartTime = DateTime.UtcNow;

                        // ✅ OPTIMIZATION #4: Use Task.Factory.StartNew for immediate execution
                        _ = Task.Factory.StartNew(async () =>
                        {
                            try
                            {
                                var result = await AzureSpeechSynthesisHelper.SynthesizeWithRetry(synthesizer, translatedForSynthesis, ct,
                                    msg => _logger.Debug(LogCategory.Synthesis, msg));
                                var synthDuration = (DateTime.UtcNow - synthStartTime).TotalMilliseconds;

                                if (result?.Reason == ResultReason.SynthesizingAudioCompleted)
                                {
                                    _logger.Info(LogCategory.Synthesis, $"[IN] Completed in {synthDuration:F0}ms");
                                    await _incomingPlaybackChannel!.Writer.WriteAsync((original, translatedForSynthesis, result), ct);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(LogCategory.Synthesis, $"[IN] Synthesis error: {ex.Message}");
                            }
                        }, ct, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
                    }
                }
            };

            _incomingRecognizer.Canceled += (s, e) =>
            {
                if (e.Reason == CancellationReason.Error)
                {
                    _logger.Error(LogCategory.Recognition, $"[IN] Recognition ERROR: {e.ErrorCode} - {e.ErrorDetails}");

                    _ = Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // Event invocation only
                    });
                }
            };

            _incomingRecognizer.SessionStarted += (s, e) =>
            {
                if (!_incomingSessionNotified)
                {
                    _incomingSessionNotified = true;
                    _logger.Info(LogCategory.System, "[IN] ✅ Session started - Listening to meeting audio...");

                    _ = Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // Event invocation only
                    });
                }
                else
                {
                    _logger.Debug(LogCategory.System, "[IN] Session resumed");
                }
            };

            if (!_isMicrophoneMuted)
            {
                await _incomingRecognizer.StartContinuousRecognitionAsync();
                _incomingWaveIn.StartRecording();
                _logger.Info(LogCategory.AudioCapture, "[IN] Audio capture started");
            }
            else
            {
                _logger.Info(LogCategory.Incoming, "Started in MUTED state");
            }

            await _incomingPlaybackTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.Incoming, $"EXCEPTION: {ex.Message}");
        }
        finally
        {
            if (_incomingWaveIn != null)
            {
                try
                {
                    _incomingWaveIn.StopRecording();
                }
                catch { }

                _incomingWaveIn.Dispose();
                _incomingWaveIn = null;
            }

            if (_incomingRecognizer != null)
            {
                try
                {
                    await _incomingRecognizer.StopContinuousRecognitionAsync();
                }
                catch { }

                _incomingRecognizer.Dispose();
                _incomingRecognizer = null;
            }

            pushStream?.Dispose();
            synthesizer?.Dispose();

            _logger.Info(LogCategory.Incoming, $"Stopped - Audio processed: {audioPassCount}");
        }
    }

    private async Task ProcessIncomingPlaybackAsync(MMDevice? physicalSpeaker, CancellationToken ct)
    {
        _logger.Info(LogCategory.Incoming, "[IN] 🎵 Playback processor started");

        try
        {
            await foreach (var item in _incomingPlaybackChannel!.Reader.ReadAllAsync(ct))
            {
                if (ct.IsCancellationRequested)
                {
                    _logger.Info(LogCategory.Incoming, "[IN] ⏹️ Cancellation requested - stopping playback");
                    break;
                }

                if (_isSpeakerMuted)
                {
                    _logger.Debug(LogCategory.Incoming, "[IN] 🔇 Skipping playback - speaker muted");
                    continue;
                }

                try
                {
                    await _incomingPlaybackLock.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    _logger.Info(LogCategory.Incoming, "[IN] ⏹️ Playback lock acquisition cancelled");
                    break;
                }

                try
                {
                    _isPlayingIncomingAudio = true;
                    _isIncomingSynthesizing = true;

                    var audioDurationMs = AudioPlaybackManager.CalculateAudioDuration(
                        item.result.AudioData.Length,
                        AudioConfiguration.SampleRate,
                        AudioConfiguration.Channels,
                        AudioConfiguration.BitsPerSample);

                    _logger.Debug(LogCategory.Playback, $"[IN] Original: '{item.original}' → Translated: '{item.translated}'");

                    // Smart microphone pause logic
                    bool needsPause = _outgoingRecognizer != null && !_isMicrophoneMuted && _isUserSpeaking;
                    bool microphonePaused = false;

                    if (_outgoingRecognizer != null && !_isMicrophoneMuted)
                    {
                        await _microphoneControlLock.WaitAsync(ct);
                        try
                        {
                            if (needsPause)
                            {
                                _logger.Debug(LogCategory.AudioCapture, "[LOOPBACK] ⏸️ Pausing microphone (user was speaking)");
                            }
                            else
                            {
                                _logger.Debug(LogCategory.AudioCapture, "[LOOPBACK] ⏸️ Pausing microphone (preventive protection)");
                            }

                            await _outgoingRecognizer.StopContinuousRecognitionAsync();
                            _microphonePausedForPlayback = true;
                            microphonePaused = true;

                            _logger.Info(LogCategory.AudioCapture, "[LOOPBACK] ✅ Microphone paused - protection ACTIVE");
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(LogCategory.AudioCapture, $"[LOOPBACK] ❌ Failed to pause microphone: {ex.Message}");
                        }
                        finally
                        {
                            _microphoneControlLock.Release();
                        }
                    }

                    if (microphonePaused)
                    {
                        await Task.Delay(50, ct);
                    }

                    _logger.Info(LogCategory.Playback, $"[IN] 🔊 Starting playback ({audioDurationMs:F0}ms) to speakers...");

                    // Notify UI that synthesis started
                    SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                    {
                        OriginalText = item.original,
                        TranslatedText = item.translated,
                        IsFromMeeting = true,
                        IsSynthesizing = true
                    });

                    // Play audio (this will be cancelled if ct is triggered)
                    await AudioPlaybackManager.PlayAudioToPhysicalSpeakerAsync(item.result.AudioData, physicalSpeaker, ct);

                    _logger.Info(LogCategory.Playback, $"[IN] ✅ Playback completed successfully ({audioDurationMs:F0}ms)");

                    // Notify UI that synthesis completed
                    SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                    {
                        OriginalText = item.original,
                        TranslatedText = item.translated,
                        IsFromMeeting = true,
                        IsSynthesizing = false
                    });

                    // Adaptive settling delay
                    if (microphonePaused)
                    {
                        int settlingDelay = needsPause ? 100 : 50;
                        await Task.Delay(settlingDelay, ct);
                    }

                    // Resume microphone after audio fully cleared
                    if (microphonePaused && _outgoingRecognizer != null && !_isMicrophoneMuted)
                    {
                        await _microphoneControlLock.WaitAsync(ct);
                        try
                        {
                            _logger.Debug(LogCategory.AudioCapture, "[LOOPBACK] ▶️ Resuming microphone...");
                            _microphonePausedForPlayback = false;
                            await _outgoingRecognizer.StartContinuousRecognitionAsync();
                            _logger.Info(LogCategory.AudioCapture, "[LOOPBACK] ✅ Microphone resumed - protection INACTIVE");
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(LogCategory.AudioCapture, $"[LOOPBACK] ❌ Failed to resume microphone: {ex.Message}");
                            _microphonePausedForPlayback = false;
                        }
                        finally
                        {
                            _microphoneControlLock.Release();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.Warning(LogCategory.Playback, "[IN] ⚠️ Playback cancelled mid-stream");

                    // Notify UI that synthesis was interrupted
                    SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                    {
                        OriginalText = item.original,
                        TranslatedText = item.translated,
                        IsFromMeeting = true,
                        IsSynthesizing = false
                    });

                    _microphonePausedForPlayback = false;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategory.Playback, $"[IN] ❌ Playback error: {ex.Message}");

                    // Notify UI of error
                    SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
                    {
                        OriginalText = item.original,
                        TranslatedText = item.translated,
                        IsFromMeeting = true,
                        IsSynthesizing = false
                    });

                    _microphonePausedForPlayback = false;
                }
                finally
                {
                    _isPlayingIncomingAudio = false;
                    _isIncomingSynthesizing = false;
                    _incomingPlaybackLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info(LogCategory.Incoming, "[IN] ⏹️ Playback processor cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategory.Incoming, $"[IN] ❌ Playback processor error: {ex.Message}");
        }
        finally
        {
            _isPlayingIncomingAudio = false;
            _isIncomingSynthesizing = false;
            _microphonePausedForPlayback = false;
            _logger.Info(LogCategory.Incoming, "[IN] 🛑 Playback processor finished");
        }
    }
   
    private void LogMetrics()
    {
        _logger.Info(LogCategory.Metrics, "***************************************************************");
        _logger.Info(LogCategory.Metrics, "Translation Session Metrics:");
        _logger.Info(LogCategory.Metrics, $"   Outgoing Queue Peak: {_outgoingQueuePeak}");
        _logger.Info(LogCategory.Metrics, $"   Incoming Queue Peak: {_incomingQueuePeak}");
        _logger.Info(LogCategory.Metrics, $"   Outgoing Messages Skipped: {_outgoingMessagesSkipped}");
        _logger.Info(LogCategory.Metrics, $"   Incoming Messages Skipped: {_incomingMessagesSkipped}");

        if (_totalRecognitions > 0)
        {
            var qualityRate = (1 - (_lowConfidenceCount / (double)_totalRecognitions)) * 100;
            _logger.Info(LogCategory.Metrics, $"   Recognition Quality Rate: {qualityRate:F1}%");
            _logger.Info(LogCategory.Metrics, $"   Low Confidence Detections: {_lowConfidenceCount}/{_totalRecognitions}");
        }

        _logger.Info(LogCategory.Metrics, "***************************************************************");
    }

    private void LogRaw(string message)
    {
        LogMessage?.Invoke(this, message);
        // ✅ Also log to Output window for debugging
        System.Diagnostics.Debug.WriteLine($"[TranslationService] {message}");
    }      

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
            _logger?.Info(LogCategory.System, "🧹 Disposing TranslationService...");

            try
            {
                // Cancel the cancellation token source
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                _logger?.Debug(LogCategory.System, "✅ Cancellation token disposed");
            }
            catch (Exception ex)
            {
                _logger?.Warning(LogCategory.System, $"⚠️ Error disposing CTS: {ex.Message}");
            }

            // Dispose semaphores
            try
            {
                _outgoingPlaybackLock?.Dispose();
                _incomingPlaybackLock?.Dispose();
                _muteLock?.Dispose();
                _microphoneControlLock?.Dispose();
                _logger?.Debug(LogCategory.System, "✅ Semaphores disposed");
            }
            catch (Exception ex)
            {
                _logger?.Warning(LogCategory.System, $"⚠️ Error disposing semaphores: {ex.Message}");
            }

            // Dispose transcript managers
            try
            {
                _outgoingTranscriptManager?.Dispose();
                _incomingTranscriptManager?.Dispose();
                _logger?.Debug(LogCategory.System, "✅ Transcript managers disposed");
            }
            catch (Exception ex)
            {
                _logger?.Warning(LogCategory.System, $"⚠️ Error disposing transcript managers: {ex.Message}");
            }

            // Dispose recognizers
            try
            {
                _outgoingRecognizer?.Dispose();
                _outgoingRecognizer = null;
                _incomingRecognizer?.Dispose();
                _incomingRecognizer = null;
                _logger?.Debug(LogCategory.System, "✅ Recognizers disposed");
            }
            catch (Exception ex)
            {
                _logger?.Warning(LogCategory.System, $"⚠️ Error disposing recognizers: {ex.Message}");
            }

            // Dispose wave input
            try
            {
                _incomingWaveIn?.Dispose();
                _incomingWaveIn = null;
                _logger?.Debug(LogCategory.System, "✅ Wave input disposed");
            }
            catch (Exception ex)
            {
                _logger?.Warning(LogCategory.System, $"⚠️ Error disposing wave input: {ex.Message}");
            }

            // Complete channels safely (only if not already completed)
            try
            {
                if (_outgoingPlaybackChannel != null)
                {
                    // TryComplete returns false if already completed
                    if (_outgoingPlaybackChannel.Writer.TryComplete())
                    {
                        _logger?.Debug(LogCategory.System, "✅ Outgoing playback channel completed");
                    }
                    else
                    {
                        _logger?.Debug(LogCategory.System, "ℹ️ Outgoing playback channel already completed");
                    }
                    _outgoingPlaybackChannel = null;
                }

                if (_incomingPlaybackChannel != null)
                {
                    // TryComplete returns false if already completed
                    if (_incomingPlaybackChannel.Writer.TryComplete())
                    {
                        _logger?.Debug(LogCategory.System, "✅ Incoming playback channel completed");
                    }
                    else
                    {
                        _logger?.Debug(LogCategory.System, "ℹ️ Incoming playback channel already completed");
                    }
                    _incomingPlaybackChannel = null;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning(LogCategory.System, $"⚠️ Error completing channels: {ex.Message}");
            }

            _logger?.Info(LogCategory.System, "✅ TranslationService disposed successfully");
        }

        _disposed = true;
    }
}