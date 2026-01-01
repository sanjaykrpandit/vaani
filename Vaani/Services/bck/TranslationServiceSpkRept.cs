//using Microsoft.CognitiveServices.Speech;
//using Microsoft.CognitiveServices.Speech.Audio;
//using Microsoft.CognitiveServices.Speech.Translation;



//using NAudio.CoreAudioApi;
//using NAudio.Wave;
//using Vaani.Models;
//using Avalonia.Threading;
//using AzureAudioConfig = Microsoft.CognitiveServices.Speech.Audio.AudioConfig;
//using System.Collections.Concurrent;
//using System.Buffers;
//using vconsole.Services.Logger;



//namespace Vaani.Services;

///// <summary>
///// Manages bidirectional real-time translation between two languages using Azure Cognitive Services.
///// </summary>
//public class TranslationService : IDisposable
//{
//    private DeviceService _deviceService;
//    private CancellationTokenSource? _cts;
//    private readonly SemaphoreSlim _outgoingPlaybackLock = new(1, 1);
//    private readonly SemaphoreSlim _incomingPlaybackLock = new(1, 1);

//    private volatile bool _isPlayingIncomingAudio;
//    private volatile bool _isPlayingOutgoingAudio;  
//    private Task? _runningTask;

//    private Task? _incomingPlaybackTask;
//    private Task? _outgoingPlaybackTask;

//    private volatile bool _isMicrophoneMuted = false;
//    private volatile bool _isSpeakerMuted = false;

//    private TranslationRecognizer? _outgoingRecognizer;
//    private TranslationRecognizer? _incomingRecognizer;
//    private WaveInEvent? _incomingWaveIn;
//    private readonly SemaphoreSlim _muteLock = new(1, 1);

//    private readonly HashSet<string> _recentlyPlayedTranslations = new(StringComparer.OrdinalIgnoreCase);
//    private readonly ReaderWriterLockSlim _echoPreventionLock = new();
//    private DateTime _lastOutgoingPlaybackTime = DateTime.MinValue;

//    private TranscriptDuplicateManager _outgoingTranscriptManager;
//    private TranscriptDuplicateManager _incomingTranscriptManager;

//    private int _outgoingMessagesSkipped = 0;
//    private int _incomingMessagesSkipped = 0;
//    private int _outgoingQueuePeak = 0;
//    private int _incomingQueuePeak = 0;

//    //private static readonly ArrayPool<byte> _audioBufferPool = ArrayPool<byte>.Shared;
//    private bool _disposed;

//    private TranslationLogger _logger;

//    public event EventHandler<string>? LogMessage;
//    public event EventHandler<MessageEventArgs>? MessageReceived;
//    public event EventHandler<TranslationEventArgs>? TranslationReceived;
//    public event EventHandler<SystemMessageEventArgs>? SystemMessage;
//    public event EventHandler<SynthesizingEventArgs>? SynthesizingStatusChanged;
//    public event EventHandler<bool>? MeetingAudioActivityChanged;

//    private int _lowConfidenceCount = 0;
//    private int _totalRecognitions = 0;


//    private volatile bool _outgoingSessionNotified = false;
//    private volatile bool _incomingSessionNotified = false;

//    private volatile bool _enableParallelFlow = false;
//    private bool _parallelFlowAutoDetected = false;

//    private volatile bool _isUserSpeaking = false;
//    private DateTime _lastUserSpeechTime = DateTime.MinValue;
//    private DateTime _lastIncomingAudioActivity = DateTime.MinValue;

//    public TranslationService()
//    {
//        _deviceService = new DeviceService();
//        _logger = new TranslationLogger(LogRaw, LogLevel.Info); // Default to Info level
//        _outgoingTranscriptManager = new TranscriptDuplicateManager(msg => _logger.Debug(LogCategory.Duplicate, msg));
//        _incomingTranscriptManager = new TranscriptDuplicateManager(msg => _logger.Debug(LogCategory.Duplicate, msg));
//    }

//    /// <summary>
//    /// Sets the minimum log level for filtering. Use Debug for verbose logging.
//    /// </summary>
//    public void SetLogLevel(LogLevel level)
//    {
//        _logger.SetMinLogLevel(level);
//        _logger.Info(LogCategory.System, $"Log level set to: {level}");
//    }

//    /// <summary>
//    /// Enables specific log categories. Pass null to enable all categories.
//    /// </summary>
//    public void SetLogCategories(params LogCategory[]? categories)
//    {
//        _logger.SetEnabledCategories(categories);
//        if (categories == null)
//            _logger.Info(LogCategory.System, "All log categories enabled");
//        else
//            _logger.Info(LogCategory.System, $"Enabled categories: {string.Join(", ", categories)}");
//    }

//    public bool IsRunning => _cts != null && !_cts.Token.IsCancellationRequested;


//    public async void SetMicrophoneMute(bool muted)
//    {
//        if (_isMicrophoneMuted == muted) return;
//        _isMicrophoneMuted = muted;

//        await _muteLock.WaitAsync();
//        try
//        {
//            if (muted)
//            {
//                _logger.Info(LogCategory.System, "Microphone MUTING - Stopping your microphone...");

//                if (_outgoingRecognizer != null)
//                {
//                    try
//                    {
//                        await _outgoingRecognizer.StopContinuousRecognitionAsync();
//                        _logger.Info(LogCategory.Outgoing, "Your microphone paused");
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.Error(LogCategory.Outgoing, $"Error pausing microphone: {ex.Message}");
//                    }
//                }

//                _logger.Info(LogCategory.System, "🔇 Microphone MUTED (Meeting audio continues)");
//            }
//            else
//            {
//                _logger.Info(LogCategory.System, "Microphone UNMUTING - Resuming your microphone...");

//                if (_outgoingRecognizer != null)
//                {
//                    try
//                    {
//                        await _outgoingRecognizer.StartContinuousRecognitionAsync();
//                        _logger.Info(LogCategory.Outgoing, "Your microphone resumed");
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.Error(LogCategory.Outgoing, $"Error resuming microphone: {ex.Message}");
//                    }
//                }
//                else
//                {
//                    _logger.Warning(LogCategory.Outgoing, "Cannot resume - microphone recognizer is null");
//                }

//                _logger.Info(LogCategory.System, "🔊 Microphone UNMUTED - Speak now!");
//            }
//        }
//        finally
//        {
//            _muteLock.Release();
//        }
//    }

//    public async void SetSpeakerMute(bool muted)
//    {
//        if (_isSpeakerMuted == muted) return;
//        _isSpeakerMuted = muted;

//        await _muteLock.WaitAsync();
//        try
//        {
//            if (muted)
//            {
//                _logger.Info(LogCategory.System, "Speaker MUTING...");

//                // ❌ DON'T STOP WAVEIN - Keep capturing for indicator
//                // Only stop Azure recognition
//                if (_incomingRecognizer != null)
//                {
//                    try
//                    {
//                        await _incomingRecognizer.StopContinuousRecognitionAsync();
//                        _logger.Info(LogCategory.Incoming, "Translation paused (capture continues)");
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.Error(LogCategory.Incoming, $"Error pausing: {ex.Message}");
//                    }
//                }

//                _logger.Info(LogCategory.System, "🔇 Speaker MUTED (Capture continues for indicator)");
//            }
//            else
//            {
//                _logger.Info(LogCategory.System, "Speaker UNMUTING...");

//                // Resume Azure recognition
//                if (_incomingRecognizer != null)
//                {
//                    try
//                    {
//                        await _incomingRecognizer.StartContinuousRecognitionAsync();
//                        _logger.Info(LogCategory.Incoming, "Translation resumed");
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.Error(LogCategory.Incoming, $"Error resuming: {ex.Message}");
//                    }
//                }
//                else
//                {
//                    _logger.Warning(LogCategory.Incoming, "Cannot resume - recognizer is null");
//                }

//                // ❌ REMOVE THIS ENTIRE BLOCK - Monitor already exists in IncomingFlow!
//                // DON'T restart WaveIn here - it should always be running

//                _logger.Info(LogCategory.System, "🔊 Speaker UNMUTED");
//            }
//        }
//        finally
//        {
//            _muteLock.Release();
//        }
//    }


//    //public async void SetSpeakerMute(bool muted)
//    //{
//    //    if (_isSpeakerMuted == muted) return;
//    //    _isSpeakerMuted = muted;

//    //    await _muteLock.WaitAsync();
//    //    try
//    //    {
//    //        if (muted)
//    //        {
//    //            _logger.Info(LogCategory.System, "Speaker MUTING - Stopping meeting audio capture...");

//    //            if (_incomingWaveIn != null)
//    //            {
//    //                try
//    //                {
//    //                    _incomingWaveIn.StopRecording();
//    //                    _logger.Info(LogCategory.Incoming, "Meeting audio capture paused");
//    //                }
//    //                catch (Exception ex)
//    //                {
//    //                    _logger.Error(LogCategory.Incoming, $"Error pausing audio capture: {ex.Message}");
//    //                }
//    //            }

//    //            if (_incomingRecognizer != null)
//    //            {
//    //                try
//    //                {
//    //                    await _incomingRecognizer.StopContinuousRecognitionAsync();
//    //                    _logger.Info(LogCategory.Incoming, "Meeting recognition paused");
//    //                }
//    //                catch (Exception ex)
//    //                {
//    //                    _logger.Error(LogCategory.Incoming, $"Error pausing recognition: {ex.Message}");
//    //                }
//    //            }

//    //            _logger.Info(LogCategory.System, "🔇 Speaker MUTED (Your microphone continues)");
//    //        }
//    //        else
//    //        {
//    //            _logger.Info(LogCategory.System, "Speaker UNMUTING - Resuming meeting audio capture...");

//    //            if (_incomingRecognizer != null)
//    //            {
//    //                try
//    //                {
//    //                    await _incomingRecognizer.StartContinuousRecognitionAsync();
//    //                    _logger.Info(LogCategory.Incoming, "Meeting recognition resumed");
//    //                }
//    //                catch (Exception ex)
//    //                {
//    //                    _logger.Error(LogCategory.Incoming, $"Error resuming recognition: {ex.Message}");
//    //                }
//    //            }
//    //            else
//    //            {
//    //                _logger.Warning(LogCategory.Incoming, "Cannot resume - meeting recognizer is null");
//    //            }

//    //            if (_incomingWaveIn != null)
//    //            {
//    //                try
//    //                {
//    //                    _incomingWaveIn.StartRecording();
//    //                    _logger.Info(LogCategory.Incoming, "Meeting audio capture resumed");
//    //                }
//    //                catch (Exception ex)
//    //                {
//    //                    _logger.Error(LogCategory.Incoming, $"Error resuming audio capture: {ex.Message}");
//    //                }
//    //            }
//    //            else
//    //            {
//    //                _logger.Warning(LogCategory.Incoming, "Cannot resume - WaveIn is null");
//    //            }

//    //            _logger.Info(LogCategory.System, "🔊 Speaker UNMUTED - Meeting audio playing!");
//    //        }
//    //    }
//    //    finally
//    //    {
//    //        _muteLock.Release();
//    //    }
//    //}


//    // Add this method after SetSpeakerMute:
//    /// <summary>
//    /// Sets parallel flow mode manually. 
//    /// When enabled, microphone remains active during incoming audio playback.
//    /// Only recommended with separate CABLE A+B devices to prevent echo.
//    /// </summary>
//    public void SetParallelFlowMode(bool enabled)
//    {
//        _enableParallelFlow = enabled;
//        _parallelFlowAutoDetected = false; // Manual override
//        _logger.Info(LogCategory.System, $"Parallel flow mode: {(enabled ? "ENABLED (manual - simultaneous)" : "DISABLED (manual - echo prevention)")}");
//    }

//    public async Task StartTranslationAsync(TranslationSettings settings)
//    {
//        if (IsRunning)
//        {
//            _logger.Warning(LogCategory.System, "Translation is already running!");
//            return;
//        }

//        if (settings == null || string.IsNullOrWhiteSpace(settings.AzureSubscriptionKey) || string.IsNullOrWhiteSpace(settings.AzureRegion))
//        {
//            _logger.Error(LogCategory.System, "Invalid translation settings!");
//            return;
//        }
              

//        _logger.Info(LogCategory.System, "═════════════════════════════════════════");
//        _logger.Info(LogCategory.System, "║  Starting Bidirectional Translation    ║");
//        _logger.Info(LogCategory.System, "═════════════════════════════════════════");
//        _logger.Info(LogCategory.System, $"Source: {settings.SourceLanguage} ↔ Target: {settings.TargetLanguage}");
//        _logger.Info(LogCategory.System, $"Azure Region: {settings.AzureRegion}");
//        _logger.Debug(LogCategory.System, $"Azure Key: {TextProcessingHelper.MaskKey(settings.AzureSubscriptionKey)}");
//        _logger.Info(LogCategory.System, $"Source Voice: {settings.SourceVoice}");
//        _logger.Info(LogCategory.System, $"Target Voice: {settings.TargetVoice}");


//        if (!ValidateCableConnection())
//        {
//            _logger.Error(LogCategory.System, "Cannot start: VB Cable setup issue");

//            SystemMessage?.Invoke(this, new SystemMessageEventArgs
//            {
//                Message = "Setup Error",
//                Detail = "VB Cable device not found. Please install VB-Audio Cable.",
//                MessageType = SystemMessageType.Error
//            });

//            MeetingAudioActivityChanged?.Invoke(this, false);
//            return; // Don't start
//        }


//        // ✅ NEW: Auto-detect parallel flow capability
//        if (AudioConfiguration.AutoDetectParallelFlow && !_parallelFlowAutoDetected)
//        {
//            bool hasCableAB = _deviceService.HasCableABDevices();
//            _enableParallelFlow = hasCableAB;
//            _parallelFlowAutoDetected = true;

//            if (hasCableAB)
//            {
//                _logger.Info(LogCategory.System, "🚀 PARALLEL FLOW MODE: ENABLED (Cable A+B detected)");
//                _logger.Info(LogCategory.System, "   → Microphone will remain active during playback");
//                _logger.Info(LogCategory.System, "   → Reduced latency for quick responses");
//            }
//            else
//            {
//                _logger.Info(LogCategory.System, "🔒 SEQUENTIAL MODE: ENABLED (Single Cable detected)");
//                _logger.Info(LogCategory.System, "   → Microphone will pause during playback for echo prevention");
//            }
//        }

//        //auto-detect parallel flow capability


//        _cts = new CancellationTokenSource();






//        _isPlayingOutgoingAudio = false;         

//        _echoPreventionLock.EnterWriteLock();
//        try
//        {
//            _recentlyPlayedTranslations.Clear();
//            _lastOutgoingPlaybackTime = DateTime.MinValue;
//        }
//        finally
//        {
//            _echoPreventionLock.ExitWriteLock();
//        }

//        _outgoingSessionNotified = false;
//        _incomingSessionNotified = false;

//        _outgoingTranscriptManager.Clear();
//        _incomingTranscriptManager.Clear();

//        _outgoingMessagesSkipped = 0;
//        _incomingMessagesSkipped = 0;
//        _outgoingQueuePeak = 0;
//        _incomingQueuePeak = 0;

//        var outgoingTask = Task.Run(() => OutgoingFlow(settings, _cts.Token));
//        var incomingTask = Task.Run(() => IncomingFlow(settings, _cts.Token));
//        _runningTask = Task.WhenAll(outgoingTask, incomingTask);

//        try
//        {
//            await _runningTask;
//        }
//        catch (OperationCanceledException) { }
//        catch (Exception ex)
//        {
//            _logger.Error(LogCategory.System, $"Translation error: {ex.Message}");
//        }
//        finally
//        {
//            _logger.Info(LogCategory.System, "Translation has stopped.");
//            LogMetrics();

//            SystemMessage?.Invoke(this, new SystemMessageEventArgs
//            {
//                Message = "Translation Stopped",
//                Detail = "",
//                MessageType = SystemMessageType.Stopped
//            });

//            _runningTask = null;
//        }
//    }

//    public async Task StopTranslationAsync()
//    {
//        if (_cts != null && !_cts.Token.IsCancellationRequested)
//        {
//            _logger.Info(LogCategory.System, "Stopping translation...");

//            // ✅ Immediately set indicator to disconnected
//            MeetingAudioActivityChanged?.Invoke(this, false);
//            _logger.Info(LogCategory.Device, "[IN] 🔴 VB Cable disconnecting");

//            _cts.Cancel();

//            var running = _runningTask;
//            if (running != null)
//            {
//                try
//                {
//                    await running.WaitAsync(TimeSpan.FromSeconds(10));
//                }
//                catch (OperationCanceledException) { }
//                catch (TimeoutException)
//                {
//                    _logger.Warning(LogCategory.System, "Translation shutdown timed out.");
//                }
//                catch (Exception ex)
//                {
//                    _logger.Error(LogCategory.System, $"Translation shutdown error: {ex.Message}");
//                }
//            }
//        }
//    }


//    private async Task OutgoingFlow(TranslationSettings settings, CancellationToken ct)
//    {
//        SpeechSynthesizer? synthesizer = null;
//        AzureAudioConfig? audioConfig = null;
//        var autoDetectConfig = AutoDetectSourceLanguageConfig.FromLanguages(new string[] { "en-US", "es-ES", "fr-FR", "de-DE", "ja-JP", "hi-IN", "zh-CN" });
//        try
//        {
//            _logger.Info(LogCategory.Outgoing, "Initializing...");

//            // ✅ OPTIMIZED: Use endpoint-based configuration for better performance
//            string endpoint = $"wss://{settings.AzureRegion}.stt.speech.microsoft.com/speech/universal/v2";
//            var config = SpeechTranslationConfig.FromEndpoint(new Uri(endpoint), settings.AzureSubscriptionKey);

//            // ✅ Configure language settings after endpoint initialization
//            config.SpeechRecognitionLanguage = settings.SourceLanguage;

//            var targetLang = settings.TargetLanguage.Split('-')[0];
//            config.AddTargetLanguage(targetLang);

//            var physicalMic = _deviceService.FindPhysicalMicrophone();

//            if (physicalMic != null)
//            {
//                _logger.Info(LogCategory.Device, $"🎤 Using physical microphone: {physicalMic.FriendlyName}");
//                audioConfig = AzureAudioConfig.FromMicrophoneInput(physicalMic.ID);
//            }
//            else
//            {
//                _logger.Warning(LogCategory.Device, "Physical microphone not found, using default");
//                audioConfig = AzureAudioConfig.FromDefaultMicrophoneInput();
//            }

//            // ✅ Configure recognition settings
//            config.OutputFormat = OutputFormat.Detailed;
//            config.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, "Masked");
//            config.SetProperty(PropertyId.SpeechServiceResponse_RequestWordLevelTimestamps, "true");
//            config.SetProperty(PropertyId.SpeechServiceResponse_RequestSentenceBoundary, "true");
//            config.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
//            config.SetProperty(PropertyId.SpeechServiceConnection_LanguageIdMode, "Continuous");
//            _logger.Info(LogCategory.Connection, $"[OUT] Using optimized endpoint: {endpoint}");

//            _outgoingRecognizer = new TranslationRecognizer(config, audioConfig);
//            //_outgoingRecognizer = new TranslationRecognizer(config, autoDetectConfig, audioConfig);

//            var speechConfig = SpeechConfig.FromSubscription(settings.AzureSubscriptionKey, settings.AzureRegion);
//            speechConfig.SpeechSynthesisLanguage = settings.TargetLanguage;
//            speechConfig.SpeechSynthesisVoiceName = settings.TargetVoice;
//            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
//            speechConfig.SetProperty("SpeechSynthesis_ProsodyRate", "1.0");

//            //var cableDevice = _deviceService.FindCableMMDevice(DataFlow.Render);
//            var cableDevice = _deviceService.FindOutgoingCableDevice();
//            if (cableDevice != null)
//            {
//                _logger.Info(LogCategory.Device, $"🔊 CABLE device found: {cableDevice.FriendlyName}");
//            }
//            else
//            {
//                _logger.Warning(LogCategory.Device, "CABLE device not found!");
//            }

//            synthesizer = new SpeechSynthesizer(speechConfig, null);

//            var synthesizedQueue = new ConcurrentQueue<(string original, string translated, SpeechSynthesisResult result)>();
//            var synthesizedSemaphore = new SemaphoreSlim(0);

//            await AzureSpeechSynthesisHelper.WarmupAzureConnection(synthesizer, ct, msg => _logger.Info(LogCategory.Connection, msg));

//            _outgoingPlaybackTask = Task.Run(() => ProcessOutgoingQueueOptimized(synthesizedQueue, synthesizedSemaphore, cableDevice, ct), ct);


//            _outgoingRecognizer.Recognizing += (s, e) =>
//            {
//                if (!string.IsNullOrEmpty(e.Result.Text))
//                {
//                    // ✅ Audio ducking: mark user as speaking
//                    if (_enableParallelFlow && AudioConfiguration.EnableAudioDucking)
//                    {
//                        _isUserSpeaking = true;
//                        _lastUserSpeechTime = DateTime.UtcNow;
//                    }

//                    _ = Dispatcher.UIThread.InvokeAsync(() =>
//                    {
//                        _logger.Debug(LogCategory.Recognition, $"[OUT] Recognizing: {e.Result.Text}");
//                        MessageReceived?.Invoke(this, new MessageEventArgs
//                        {
//                            Direction = MessageDirection.Outgoing,
//                            Text = e.Result.Text,
//                            MessageType = MessageType.Recognizing,
//                            IsFromMeeting = false
//                        });
//                    });
//                }
//            };

//            _outgoingRecognizer.Recognized += async (s, e) =>
//            {
//                if (ct.IsCancellationRequested) return;

//                if (e.Result.Reason == ResultReason.TranslatedSpeech && !string.IsNullOrEmpty(e.Result.Text))
//                {
//                    var original = e.Result.Text.Trim();

//                    Interlocked.Increment(ref _totalRecognitions);

//                    // Log confidence (debug only)
//                    try
//                    {
//                        var jsonResult = e.Result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
//                        _logger.Debug(LogCategory.Recognition, $"[OUT] Confidence JSON: {jsonResult}");
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.Debug(LogCategory.Recognition, $"[OUT] Error parsing confidence: {ex.Message}");
//                    }

//                    if (_outgoingTranscriptManager.TryAddTranscript(original) &&
//                        e.Result.Translations.TryGetValue(targetLang, out var translated))
//                    {
//                        var translatedForSynthesis = TextProcessingHelper.CleanTextForSynthesis(translated);

//                        if (string.IsNullOrWhiteSpace(translatedForSynthesis))
//                        {
//                            _logger.Warning(LogCategory.Synthesis, $"[OUT] Text empty after cleaning: '{translated}'");
//                            return;
//                        }

//                        _ = Dispatcher.UIThread.InvokeAsync(() =>
//                        {
//                            _logger.Info(LogCategory.Recognition, $"[OUT] You said: {original}");
//                            _logger.Info(LogCategory.Recognition, $"[OUT] Translated: {translated}");

//                            MessageReceived?.Invoke(this, new MessageEventArgs
//                            {
//                                Direction = MessageDirection.Outgoing,
//                                Text = original,
//                                MessageType = MessageType.Recognized,
//                                IsFromMeeting = false
//                            });

//                            TranslationReceived?.Invoke(this, new TranslationEventArgs
//                            {
//                                Direction = MessageDirection.Outgoing,
//                                OriginalText = original,
//                                TranslatedText = translated,
//                                IsFromMeeting = false
//                            });
//                        });

//                        var synthStartTime = DateTime.UtcNow;
//                        _ = Task.Run(async () =>
//                        {
//                            try
//                            {
//                                var result = await AzureSpeechSynthesisHelper.SynthesizeWithRetry(synthesizer, translatedForSynthesis, ct,
//                                    msg => _logger.Debug(LogCategory.Synthesis, msg));
//                                var synthDuration = (DateTime.UtcNow - synthStartTime).TotalMilliseconds;

//                                if (result?.Reason == ResultReason.SynthesizingAudioCompleted)
//                                {
//                                    _logger.Info(LogCategory.Synthesis, $"[OUT] Completed in {synthDuration:F0}ms");

//                                    synthesizedQueue.Enqueue((original, translatedForSynthesis, result));
//                                    synthesizedSemaphore.Release();

//                                    int queueSize = synthesizedQueue.Count;
//                                    ThreadSafeMetrics.UpdatePeak(ref _outgoingQueuePeak, queueSize);
//                                    _logger.Debug(LogCategory.Queue, $"[OUT] Queue size: {queueSize}");
//                                }
//                            }
//                            catch (Exception ex)
//                            {
//                                _logger.Error(LogCategory.Synthesis, $"[OUT] Synthesis error: {ex.Message}");
//                            }
//                        }, ct);
//                    }
//                }
//            };

//            _outgoingRecognizer.Canceled += (s, e) =>
//            {
//                if (e.Reason == CancellationReason.Error)
//                {
//                    _ = Dispatcher.UIThread.InvokeAsync(() =>
//                    {
//                        _logger.Error(LogCategory.Recognition, $"[OUT] Recognition ERROR: {e.ErrorCode} - {e.ErrorDetails}");
//                    });
//                }
//            };

//            _outgoingRecognizer.SessionStarted += (s, e) =>
//            {

//                if (!_outgoingSessionNotified)
//                {
//                    _outgoingSessionNotified = true;

//                    _ = Dispatcher.UIThread.InvokeAsync(() =>
//                    {
//                        _logger.Info(LogCategory.System, "[OUT] ✅ Session started - Listening to your microphone...");
//                        SystemMessage?.Invoke(this, new SystemMessageEventArgs
//                        {
//                            Message = "Translation Started",
//                            Detail = "Speak to translate",
//                            MessageType = SystemMessageType.Started
//                        });
//                    });
//                }
//                else
//                {
//                    // ✅ Log subsequent session starts (for debugging) but don't notify UI
//                    _logger.Debug(LogCategory.System, "[OUT] Session resumed after microphone pause");
//                }
//            };

//            if (!_isMicrophoneMuted)
//            {
//                await _outgoingRecognizer.StartContinuousRecognitionAsync();
//            }
//            else
//            {
//                _logger.Info(LogCategory.Outgoing, "Started in MUTED state");
//            }

//            while (!ct.IsCancellationRequested)
//                await Task.Delay(100, ct);
//        }
//        catch (OperationCanceledException) { }
//        catch (Exception ex)
//        {
//            _logger.Error(LogCategory.Outgoing, $"EXCEPTION: {ex.Message}");
//        }
//        finally
//        {
//            if (_outgoingRecognizer != null)
//            {
//                try
//                {
//                    await _outgoingRecognizer.StopContinuousRecognitionAsync();
//                }
//                catch { }

//                _outgoingRecognizer.Dispose();
//                _outgoingRecognizer = null;
//            }

//            audioConfig?.Dispose();
//            synthesizer?.Dispose();

//            _logger.Info(LogCategory.Outgoing, "Stopped");
//        }
//    }

//    //private async Task OutgoingFlow(TranslationSettings settings, CancellationToken ct)
//    //{
//    //    SpeechSynthesizer? synthesizer = null;
//    //    AzureAudioConfig? audioConfig = null;

//    //    try
//    //    {
//    //        _logger.Info(LogCategory.Outgoing, "Initializing...");

//    //        var config = SpeechTranslationConfig.FromSubscription(settings.AzureSubscriptionKey, settings.AzureRegion);
//    //        config.SpeechRecognitionLanguage = settings.SourceLanguage;

//    //        var targetLang = settings.TargetLanguage.Split('-')[0];
//    //        config.AddTargetLanguage(targetLang);

//    //        var physicalMic = _deviceService.FindPhysicalMicrophone();

//    //        if (physicalMic != null)
//    //        {
//    //            _logger.Info(LogCategory.Device, $"🎤 Using physical microphone: {physicalMic.FriendlyName}");
//    //            audioConfig = AzureAudioConfig.FromMicrophoneInput(physicalMic.ID);
//    //        }
//    //        else
//    //        {
//    //            _logger.Warning(LogCategory.Device, "Physical microphone not found, using default");
//    //            audioConfig = AzureAudioConfig.FromDefaultMicrophoneInput();
//    //        }

//    //        // Configure recognition settings
//    //        config.OutputFormat = OutputFormat.Detailed;
//    //        config.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, "Masked");
//    //        config.SetProperty(PropertyId.SpeechServiceResponse_RequestWordLevelTimestamps, "true");
//    //        config.SetProperty(PropertyId.SpeechServiceResponse_RequestSentenceBoundary, "true");
//    //        config.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
//    //        //config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "500");
//    //        //config.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "500");
//    //        //config.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "3000");

//    //        _outgoingRecognizer = new TranslationRecognizer(config, audioConfig);

//    //        var speechConfig = SpeechConfig.FromSubscription(settings.AzureSubscriptionKey, settings.AzureRegion);
//    //        speechConfig.SpeechSynthesisLanguage = settings.TargetLanguage;
//    //        speechConfig.SpeechSynthesisVoiceName = settings.TargetVoice;
//    //        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
//    //        speechConfig.SetProperty("SpeechSynthesis_ProsodyRate", "1.0");
//    //        //speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "500");
//    //        //speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "500");

//    //        var cableDevice = _deviceService.FindCableMMDevice(DataFlow.Render);
//    //        if (cableDevice != null)
//    //        {
//    //            _logger.Info(LogCategory.Device, $"🔊 CABLE device found: {cableDevice.FriendlyName}");
//    //        }
//    //        else
//    //        {
//    //            _logger.Warning(LogCategory.Device, "CABLE device not found!");
//    //        }

//    //        synthesizer = new SpeechSynthesizer(speechConfig, null);

//    //        var synthesizedQueue = new ConcurrentQueue<(string original, string translated, SpeechSynthesisResult result)>();
//    //        var synthesizedSemaphore = new SemaphoreSlim(0);

//    //        await AzureSpeechSynthesisHelper.WarmupAzureConnection(synthesizer, ct, msg => _logger.Info(LogCategory.Connection, msg));

//    //        _outgoingPlaybackTask = Task.Run(() => ProcessOutgoingQueueOptimized(synthesizedQueue, synthesizedSemaphore, cableDevice, ct), ct);


//    //        _outgoingRecognizer.Recognizing += (s, e) =>
//    //        {
//    //            if (!string.IsNullOrEmpty(e.Result.Text))
//    //            {
//    //                _ = Dispatcher.UIThread.InvokeAsync(() =>
//    //                {
//    //                    _logger.Debug(LogCategory.Recognition, $"[OUT] Recognizing: {e.Result.Text}");
//    //                    MessageReceived?.Invoke(this, new MessageEventArgs
//    //                    {
//    //                        Direction = MessageDirection.Outgoing,
//    //                        Text = e.Result.Text,
//    //                        MessageType = MessageType.Recognizing,
//    //                        IsFromMeeting = false
//    //                    });
//    //                });
//    //            }
//    //        };

//    //        _outgoingRecognizer.Recognized += async (s, e) =>
//    //        {
//    //            if (ct.IsCancellationRequested) return;

//    //            if (e.Result.Reason == ResultReason.TranslatedSpeech && !string.IsNullOrEmpty(e.Result.Text))
//    //            {
//    //                var original = e.Result.Text.Trim();

//    //                Interlocked.Increment(ref _totalRecognitions);

//    //                // Log confidence (debug only)
//    //                try
//    //                {
//    //                    var jsonResult = e.Result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
//    //                    _logger.Debug(LogCategory.Recognition, $"[OUT] Confidence JSON: {jsonResult}");
//    //                }
//    //                catch (Exception ex)
//    //                {
//    //                    _logger.Debug(LogCategory.Recognition, $"[OUT] Error parsing confidence: {ex.Message}");
//    //                }

//    //                if (_outgoingTranscriptManager.TryAddTranscript(original) &&
//    //                    e.Result.Translations.TryGetValue(targetLang, out var translated))
//    //                {
//    //                    var translatedForSynthesis = TextProcessingHelper.CleanTextForSynthesis(translated);

//    //                    if (string.IsNullOrWhiteSpace(translatedForSynthesis))
//    //                    {
//    //                        _logger.Warning(LogCategory.Synthesis, $"[OUT] Text empty after cleaning: '{translated}'");
//    //                        return;
//    //                    }

//    //                    _ = Dispatcher.UIThread.InvokeAsync(() =>
//    //                    {
//    //                        _logger.Info(LogCategory.Recognition, $"[OUT] You said: {original}");
//    //                        _logger.Info(LogCategory.Recognition, $"[OUT] Translated: {translated}");

//    //                        MessageReceived?.Invoke(this, new MessageEventArgs
//    //                        {
//    //                            Direction = MessageDirection.Outgoing,
//    //                            Text = original,
//    //                            MessageType = MessageType.Recognized,
//    //                            IsFromMeeting = false
//    //                        });

//    //                        TranslationReceived?.Invoke(this, new TranslationEventArgs
//    //                        {
//    //                            Direction = MessageDirection.Outgoing,
//    //                            OriginalText = original,
//    //                            TranslatedText = translated,
//    //                            IsFromMeeting = false
//    //                        });
//    //                    });

//    //                    var synthStartTime = DateTime.UtcNow;
//    //                    _ = Task.Run(async () =>
//    //                    {
//    //                        try
//    //                        {
//    //                            var result = await AzureSpeechSynthesisHelper.SynthesizeWithRetry(synthesizer, translatedForSynthesis, ct,
//    //                                msg => _logger.Debug(LogCategory.Synthesis, msg));
//    //                            var synthDuration = (DateTime.UtcNow - synthStartTime).TotalMilliseconds;

//    //                            if (result?.Reason == ResultReason.SynthesizingAudioCompleted)
//    //                            {
//    //                                _logger.Info(LogCategory.Synthesis, $"[OUT] Completed in {synthDuration:F0}ms");

//    //                                synthesizedQueue.Enqueue((original, translatedForSynthesis, result));
//    //                                synthesizedSemaphore.Release();

//    //                                int queueSize = synthesizedQueue.Count;
//    //                                ThreadSafeMetrics.UpdatePeak(ref _outgoingQueuePeak, queueSize);
//    //                                _logger.Debug(LogCategory.Queue, $"[OUT] Queue size: {queueSize}");
//    //                            }
//    //                        }
//    //                        catch (Exception ex)
//    //                        {
//    //                            _logger.Error(LogCategory.Synthesis, $"[OUT] Synthesis error: {ex.Message}");
//    //                        }
//    //                    }, ct);
//    //                }
//    //            }
//    //        };

//    //        _outgoingRecognizer.Canceled += (s, e) =>
//    //        {
//    //            if (e.Reason == CancellationReason.Error)
//    //            {
//    //                _ = Dispatcher.UIThread.InvokeAsync(() =>
//    //                {
//    //                    _logger.Error(LogCategory.Recognition, $"[OUT] Recognition ERROR: {e.ErrorCode} - {e.ErrorDetails}");
//    //                });
//    //            }
//    //        };

//    //        _outgoingRecognizer.SessionStarted += (s, e) =>
//    //        {

//    //            if (!_outgoingSessionNotified)
//    //            {
//    //                _outgoingSessionNotified = true;

//    //                _ = Dispatcher.UIThread.InvokeAsync(() =>
//    //                {
//    //                    _logger.Info(LogCategory.System, "[OUT] ✅ Session started - Listening to your microphone...");
//    //                    SystemMessage?.Invoke(this, new SystemMessageEventArgs
//    //                    {
//    //                        Message = "Translation Started",
//    //                        Detail = "Speak to translate",
//    //                        MessageType = SystemMessageType.Started
//    //                    });
//    //                });
//    //            }
//    //            else
//    //            {
//    //                // ✅ Log subsequent session starts (for debugging) but don't notify UI
//    //                _logger.Debug(LogCategory.System, "[OUT] Session resumed after microphone pause");
//    //            }




//    //            //_ = Dispatcher.UIThread.InvokeAsync(() =>
//    //            //{
//    //            //    _logger.Info(LogCategory.System, "[OUT] ✅ Session started - Listening to your microphone...");
//    //            //    SystemMessage?.Invoke(this, new SystemMessageEventArgs
//    //            //    {
//    //            //        Message = "Translation Started",
//    //            //        Detail = "Speak to translate...",
//    //            //        MessageType = SystemMessageType.Started
//    //            //    });
//    //            //});
//    //        };

//    //        if (!_isMicrophoneMuted)
//    //        {
//    //            await _outgoingRecognizer.StartContinuousRecognitionAsync();
//    //        }
//    //        else
//    //        {
//    //            _logger.Info(LogCategory.Outgoing, "Started in MUTED state");
//    //        }

//    //        while (!ct.IsCancellationRequested)
//    //            await Task.Delay(100, ct);
//    //    }
//    //    catch (OperationCanceledException) { }
//    //    catch (Exception ex)
//    //    {
//    //        _logger.Error(LogCategory.Outgoing, $"EXCEPTION: {ex.Message}");
//    //    }
//    //    finally
//    //    {
//    //        if (_outgoingRecognizer != null)
//    //        {
//    //            try
//    //            {
//    //                await _outgoingRecognizer.StopContinuousRecognitionAsync();
//    //            }
//    //            catch { }

//    //            _outgoingRecognizer.Dispose();
//    //            _outgoingRecognizer = null;
//    //        }

//    //        audioConfig?.Dispose();
//    //        synthesizer?.Dispose();

//    //        _logger.Info(LogCategory.Outgoing, "Stopped");
//    //    }
//    //}

//    private async Task ProcessOutgoingQueueOptimized(ConcurrentQueue<(string original, string translated, SpeechSynthesisResult result)> queue, SemaphoreSlim semaphore, MMDevice? cableDevice, CancellationToken ct)
//    {
//        while (!ct.IsCancellationRequested)
//        {
//            try
//            {
//                await semaphore.WaitAsync(ct);
//            }
//            catch (OperationCanceledException)
//            {
//                break;
//            }

//            if (!queue.TryDequeue(out var item))
//                continue;

//            if (ct.IsCancellationRequested || _isSpeakerMuted)
//                continue;

//            try
//            {
//                await _outgoingPlaybackLock.WaitAsync(ct);
//            }
//            catch (OperationCanceledException)
//            {
//                break;
//            }

//            try
//            {
//                _isPlayingOutgoingAudio = true;

//                var audioDurationMs = AudioPlaybackManager.CalculateAudioDuration(item.result.AudioData.Length, AudioConfiguration.SampleRate,
//                    AudioConfiguration.Channels, AudioConfiguration.BitsPerSample);

//                _echoPreventionLock.EnterWriteLock();
//                try
//                {
//                    _lastOutgoingPlaybackTime = DateTime.UtcNow;
//                }
//                finally
//                {
//                    _echoPreventionLock.ExitWriteLock();
//                }

//                _logger.Info(LogCategory.Playback, $"[OUT] Playing audio ({audioDurationMs:F0}ms) to meeting...");

//                // ✅ Notify UI that synthesis is starting
//                SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
//                {
//                    OriginalText = item.original,
//                    TranslatedText = item.translated,
//                    IsFromMeeting = false,
//                    IsSynthesizing = true
//                });

//                await AudioPlaybackManager.PlayAudioToCableDevice(item.result.AudioData, cableDevice);

//                // ✅ Notify UI that synthesis is complete
//                SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
//                {
//                    OriginalText = item.original,
//                    TranslatedText = item.translated,
//                    IsFromMeeting = false,
//                    IsSynthesizing = false
//                });

//                var trimmedTranslation = item.translated.Trim();
//                _echoPreventionLock.EnterWriteLock();
//                try
//                {
//                    if (_recentlyPlayedTranslations.Count >= AudioConfiguration.MaxRecentTranslations)
//                    {
//                        _recentlyPlayedTranslations.Remove(_recentlyPlayedTranslations.First());
//                    }
//                    _recentlyPlayedTranslations.Add(trimmedTranslation);
//                }
//                finally
//                {
//                    _echoPreventionLock.ExitWriteLock();
//                }
//            }
//            catch (OperationCanceledException) { }
//            catch (Exception ex)
//            {
//                _logger.Error(LogCategory.Playback, $"[OUT] Playback error: {ex.Message}");
//            }
//            finally
//            {
//                _isPlayingOutgoingAudio = false;
//                _outgoingPlaybackLock.Release();
//            }
//        }

//        _logger.Debug(LogCategory.Queue, "[OUT] Queue processor finished");
//    }
//    private async Task IncomingFlow(TranslationSettings settings, CancellationToken ct)
//    {


//        SpeechSynthesizer? synthesizer = null;
//        PushAudioInputStream? pushStream = null;
//        int audioBlockCount = 0;
//        int audioPassCount = 0;


//        var autoDetectConfig = AutoDetectSourceLanguageConfig.FromLanguages(new string[] { "en-US", "es-ES", "fr-FR", "de-DE", "ja-JP", "hi-IN", "zh-CN" });

//        try
//        {
//            _logger.Info(LogCategory.Incoming, "Initializing...");

//            //var cableDevice = _deviceService.FindCableMMDevice(DataFlow.Capture);
//            var cableDevice = _deviceService.FindIncomingCableDevice();

//            if (cableDevice == null)
//            {
//                _logger.Error(LogCategory.Device, "[IN] CABLE device NOT FOUND");
//                return;
//            }

//            _logger.Info(LogCategory.Device, $"[IN] CABLE device: {cableDevice.FriendlyName}");

//            var physicalSpeaker = _deviceService.FindPhysicalSpeaker();
//            if (physicalSpeaker != null)
//            {
//                _logger.Info(LogCategory.Device, $"[IN] 🔊 Using speaker: {physicalSpeaker.FriendlyName}");
//            }
//            else
//            {
//                _logger.Warning(LogCategory.Device, "[IN] Physical speaker not found, will use default");
//            }

//            // ✅ OPTIMIZED: Use endpoint-based configuration for better performance
//            string endpoint = $"wss://{settings.AzureRegion}.stt.speech.microsoft.com/speech/universal/v2";
//            var config = SpeechTranslationConfig.FromEndpoint(new Uri(endpoint), settings.AzureSubscriptionKey);

//            // ✅ Configure language settings after endpoint initialization
//            config.SpeechRecognitionLanguage = settings.TargetLanguage;

//            var targetLang = settings.SourceLanguage.Split('-')[0];
//            config.AddTargetLanguage(targetLang);

//            // ✅ Performance optimizations
//            config.OutputFormat = OutputFormat.Detailed;
//            config.SetProperty(PropertyId.SpeechServiceResponse_ProfanityOption, "Masked");
//            config.SetProperty(PropertyId.SpeechServiceResponse_RequestWordLevelTimestamps, "true");
//            config.SetProperty(PropertyId.SpeechServiceResponse_RequestSentenceBoundary, "true");
//            config.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
//            config.SetProperty(PropertyId.SpeechServiceConnection_LanguageIdMode, "Continuous");

//            _logger.Info(LogCategory.Connection, $"[IN] Using optimized endpoint: {endpoint}");

//            var deviceNumber = AudioDeviceHelper.GetWaveInDeviceNumber(cableDevice.FriendlyName);

//            _incomingWaveIn = new WaveInEvent
//            {
//                DeviceNumber = deviceNumber,
//                WaveFormat = new WaveFormat(AudioConfiguration.SampleRate, AudioConfiguration.BitsPerSample, AudioConfiguration.Channels),
//                BufferMilliseconds = 50
//            };

//            pushStream = AudioInputStream.CreatePushStream() as PushAudioInputStream;
//            var audioConfig = AzureAudioConfig.FromStreamInput(pushStream);
//            _incomingRecognizer = new TranslationRecognizer(config, autoDetectConfig, audioConfig);


//            // Only if needed for troubleshooting
//            //var speechConfig = SpeechConfig.FromEndpoint(new Uri($"https://{settings.AzureRegion}.tts.speech.microsoft.com/cognitiveservices/v1"),settings.AzureSubscriptionKey);
//            var speechConfig = SpeechConfig.FromSubscription(settings.AzureSubscriptionKey, settings.AzureRegion);
//            speechConfig.SpeechSynthesisLanguage = settings.SourceLanguage;
//            speechConfig.SpeechSynthesisVoiceName = settings.SourceVoice;
//            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
//            speechConfig.SetProperty(PropertyId.SpeechServiceResponse_RequestSentenceBoundary, "true");
//            speechConfig.SetProperty("SpeechSynthesis_ProsodyRate", "1.2");

//            synthesizer = new SpeechSynthesizer(speechConfig, null);

//            var synthesizedQueue = new ConcurrentQueue<(string original, string translated, SpeechSynthesisResult result)>();
//            var synthesizedSemaphore = new SemaphoreSlim(0);

//            await AzureSpeechSynthesisHelper.WarmupAzureConnection(synthesizer, ct, msg => _logger.Info(LogCategory.Connection, msg));

//            _incomingPlaybackTask = Task.Run(() => ProcessIncomingQueueOptimized(synthesizedQueue, synthesizedSemaphore, physicalSpeaker, ct), ct);

//            //_incomingWaveIn.DataAvailable += (s, e) =>
//            //{
//            //    if (ct.IsCancellationRequested) return;

//            //    float audioLevel = 0;
//            //    for (int i = 0; i < e.BytesRecorded; i += 2)
//            //    {
//            //        if (i + 1 < e.BytesRecorded)
//            //        {
//            //            short sample = BitConverter.ToInt16(e.Buffer, i);
//            //            audioLevel = Math.Max(audioLevel, Math.Abs(sample) / 32768f);
//            //        }
//            //    }

//            //    if (_isPlayingOutgoingAudio)
//            //    {
//            //        audioBlockCount++;
//            //        if (audioBlockCount % 100 == 0)
//            //        {
//            //            _logger.Debug(LogCategory.AudioCapture, $"[IN] Blocking audio (level: {audioLevel:F3})");
//            //        }
//            //        return;
//            //    }

//            //    _echoPreventionLock.EnterReadLock();
//            //    try
//            //    {
//            //        var timeSincePlayback = (DateTime.UtcNow - _lastOutgoingPlaybackTime).TotalSeconds;

//            //        if (timeSincePlayback < 0.1 && timeSincePlayback >= 0)
//            //        {
//            //            audioBlockCount++;
//            //            return;
//            //        }
//            //    }
//            //    finally
//            //    {
//            //        _echoPreventionLock.ExitReadLock();
//            //    }

//            //    audioPassCount++;
//            //    if (audioPassCount % 100 == 0)
//            //    {
//            //        var blockRate = audioBlockCount > 0 ? (audioBlockCount / (float)(audioBlockCount + audioPassCount)) * 100 : 0;
//            //        _logger.Debug(LogCategory.AudioCapture, $"[IN] Level: {audioLevel:F3}, Block rate: {blockRate:F1}%");
//            //        audioBlockCount = 0;
//            //        audioPassCount = 0;
//            //    }

//            //    pushStream?.Write(e.Buffer, e.BytesRecorded);
//            //};


//            // Find the DataAvailable handler in IncomingFlow (around line 1070) and REPLACE with:

//            //_incomingWaveIn.DataAvailable += (s, e) =>
//            //{
//            //    if (ct.IsCancellationRequested) return;

//            //    float audioLevel = 0;
//            //    for (int i = 0; i < e.BytesRecorded; i += 2)
//            //    {
//            //        if (i + 1 < e.BytesRecorded)
//            //        {
//            //            short sample = BitConverter.ToInt16(e.Buffer, i);
//            //            audioLevel = Math.Max(audioLevel, Math.Abs(sample) / 32768f);
//            //        }
//            //    }

//            //    //print incoming data size

//            //    // i want capture in every 2 seconds


//            //    //   

//            //    //
//            //    if (e.BytesRecorded > 1600)  // ~50ms of 16kHz audio
//            //    {
//            //        _lastIncomingAudioActivity = DateTime.UtcNow;

//            //        // Optional: Log occasionally to avoid spam
//            //        if (audioPassCount % 20 == 0)
//            //        {
//            //            _logger.Info(LogCategory.AudioCapture,
//            //                $"[IN] Meeting audio: {e.BytesRecorded} bytes, Level: {audioLevel:F3}");
//            //        }
//            //    }



//            //    // ✅ FIX: Only block in sequential mode (single CABLE)
//            //    // In parallel mode (Cable A+B), allow meeting audio to flow
//            //    if (!_enableParallelFlow && _isPlayingOutgoingAudio)
//            //    {
//            //        audioBlockCount++;
//            //        if (audioBlockCount % 100 == 0)
//            //        {
//            //            _logger.Debug(LogCategory.AudioCapture, $"[IN] Blocking audio (sequential mode, level: {audioLevel:F3})");
//            //        }
//            //        return; // Block only in sequential mode
//            //    }



//            //    // ✅ FIX: Reduce echo prevention window in parallel mode
//            //    _echoPreventionLock.EnterReadLock();
//            //    try
//            //    {
//            //        var timeSincePlayback = (DateTime.UtcNow - _lastOutgoingPlaybackTime).TotalSeconds;

//            //        // ✅ In parallel mode, use shorter echo window (50ms vs 100ms)
//            //       // double echoWindow = _enableParallelFlow ? 0.05 : AudioConfiguration.EchoWindowSeconds;
//            //        double echoWindow = _enableParallelFlow  ? AudioConfiguration.ParallelModeEchoWindowSeconds  : AudioConfiguration.EchoWindowSeconds;

//            //        if (timeSincePlayback < echoWindow && timeSincePlayback >= 0)
//            //        {
//            //            audioBlockCount++;
//            //            if (_enableParallelFlow)
//            //            {
//            //                _logger.Debug(LogCategory.AudioCapture, $"[IN] Echo prevention (parallel, {timeSincePlayback * 1000:F0}ms ago)");
//            //            }
//            //            return;
//            //        }
//            //    }
//            //    finally
//            //    {
//            //        _echoPreventionLock.ExitReadLock();
//            //    }

//            //    audioPassCount++;
//            //    if (audioPassCount % 100 == 0)
//            //    {
//            //        var blockRate = audioBlockCount > 0 ? (audioBlockCount / (float)(audioBlockCount + audioPassCount)) * 100 : 0;
//            //        _logger.Debug(LogCategory.AudioCapture, $"[IN] Level: {audioLevel:F3}, Block rate: {blockRate:F1}%, Mode: {(_enableParallelFlow ? "Parallel" : "Sequential")}");
//            //        audioBlockCount = 0;
//            //        audioPassCount = 0;
//            //    }

//            //    pushStream?.Write(e.Buffer, e.BytesRecorded);
//            //};

//            _incomingWaveIn.DataAvailable += (s, e) =>
//            {
//                if (ct.IsCancellationRequested) return;

//                float audioLevel = 0;
//                for (int i = 0; i < e.BytesRecorded; i += 2)
//                {
//                    if (i + 1 < e.BytesRecorded)
//                    {
//                        short sample = BitConverter.ToInt16(e.Buffer, i);
//                        audioLevel = Math.Max(audioLevel, Math.Abs(sample) / 32768f);
//                    }
//                }

//                // ✅ FIX: Update timestamp for ANY data (not just >1600 bytes)
//                // This keeps the indicator alive even during silence
//                if (e.BytesRecorded > 0)
//                {
//                    _lastIncomingAudioActivity = DateTime.UtcNow;

//                    // Only log occasionally to avoid spam
//                    if (audioPassCount % 20 == 0 && e.BytesRecorded > 1600)
//                    {
//                        _logger.Debug(LogCategory.AudioCapture,
//                            $"[IN] Meeting audio: {e.BytesRecorded} bytes, Level: {audioLevel:F3}");
//                    }
//                }

//                // ✅ FIX: Only block in sequential mode (single CABLE)
//                if (!_enableParallelFlow && _isPlayingOutgoingAudio)
//                {
//                    audioBlockCount++;
//                    if (audioBlockCount % 100 == 0)
//                    {
//                        _logger.Debug(LogCategory.AudioCapture, $"[IN] Blocking audio (sequential mode, level: {audioLevel:F3})");
//                    }
//                    return;
//                }

//                // ✅ Echo prevention window
//                _echoPreventionLock.EnterReadLock();
//                try
//                {
//                    var timeSincePlayback = (DateTime.UtcNow - _lastOutgoingPlaybackTime).TotalSeconds;
//                    double echoWindow = _enableParallelFlow ? AudioConfiguration.ParallelModeEchoWindowSeconds : AudioConfiguration.EchoWindowSeconds;

//                    if (timeSincePlayback < echoWindow && timeSincePlayback >= 0)
//                    {
//                        audioBlockCount++;
//                        if (_enableParallelFlow)
//                        {
//                            _logger.Debug(LogCategory.AudioCapture, $"[IN] Echo prevention (parallel, {timeSincePlayback * 1000:F0}ms ago)");
//                        }
//                        return;
//                    }
//                }
//                finally
//                {
//                    _echoPreventionLock.ExitReadLock();
//                }

//                audioPassCount++;
//                if (audioPassCount % 100 == 0)
//                {
//                    var blockRate = audioBlockCount > 0 ? (audioBlockCount / (float)(audioBlockCount + audioPassCount)) * 100 : 0;
//                    _logger.Debug(LogCategory.AudioCapture, $"[IN] Level: {audioLevel:F3}, Block rate: {blockRate:F1}%, Mode: {(_enableParallelFlow ? "Parallel" : "Sequential")}");
//                    audioBlockCount = 0;
//                    audioPassCount = 0;
//                }

//                pushStream?.Write(e.Buffer, e.BytesRecorded);
//            };

//            _incomingRecognizer.Recognizing += (s, e) =>
//            {
//                if (!string.IsNullOrEmpty(e.Result.Text))
//                {
//                    _ = Dispatcher.UIThread.InvokeAsync(() =>
//                    {
//                        _logger.Debug(LogCategory.Recognition, $"[IN] Recognizing: {e.Result.Text}");
//                        MessageReceived?.Invoke(this, new MessageEventArgs
//                        {
//                            Direction = MessageDirection.Incoming,
//                            Text = e.Result.Text,
//                            MessageType = MessageType.Recognizing,
//                            IsFromMeeting = true
//                        });
//                    });
//                }
//            };

//            _incomingRecognizer.Recognized += async (s, e) =>
//            {
//                if (ct.IsCancellationRequested) return;

//                if (e.Result.Reason == ResultReason.TranslatedSpeech && !string.IsNullOrEmpty(e.Result.Text))
//                {
//                    var original = e.Result.Text.Trim();

//                    Interlocked.Increment(ref _totalRecognitions);

//                    var lidResult = AutoDetectSourceLanguageResult.FromResult(e.Result);
//                    string actualLanguage = lidResult.Language;
//                    bool isEnglish = actualLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);

//                    if (!isEnglish)
//                    {
//                        _logger.Info(LogCategory.Playback, $"Non-English detected ({actualLanguage}). Applying special playback logic...");
//                    }
//                    else
//                    {
//                        _logger.Debug(LogCategory.Recognition, "[IN] English detected. Using standard playback.");
//                    }

//                    try
//                    {
//                        var jsonResult = e.Result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
//                        _logger.Debug(LogCategory.Recognition, $"[IN] Confidence JSON: {jsonResult}");
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.Debug(LogCategory.Recognition, $"[IN] Error parsing confidence: {ex.Message}");
//                    }

//                    if (!_incomingTranscriptManager.TryAddTranscript(original))
//                    {
//                        return;
//                    }

//                    if (e.Result.Translations.TryGetValue(targetLang, out var translated))
//                    {
//                        var translatedForSynthesis = TextProcessingHelper.CleanTextForSynthesis(translated);

//                        if (string.IsNullOrWhiteSpace(translatedForSynthesis))
//                        {
//                            _logger.Warning(LogCategory.Synthesis, $"[IN] Text empty after cleaning: '{translated}'");
//                            return;
//                        }

//                        _ = Dispatcher.UIThread.InvokeAsync(() =>
//                        {
//                            _logger.Info(LogCategory.Recognition, $"[IN] Meeting said: {original}");
//                            _logger.Info(LogCategory.Recognition, $"[IN] Translated: {translated}");

//                            MessageReceived?.Invoke(this, new MessageEventArgs
//                            {
//                                Direction = MessageDirection.Incoming,
//                                Text = original,
//                                MessageType = MessageType.Recognized,
//                                IsFromMeeting = true
//                            });

//                            TranslationReceived?.Invoke(this, new TranslationEventArgs
//                            {
//                                Direction = MessageDirection.Incoming,
//                                OriginalText = original,
//                                TranslatedText = translated,
//                                IsFromMeeting = true
//                            });
//                        });

//                        var synthStartTime = DateTime.UtcNow;
//                        _ = Task.Run(async () =>
//                        {
//                            try
//                            {
//                                var result = await AzureSpeechSynthesisHelper.SynthesizeWithRetry(synthesizer, translatedForSynthesis, ct,
//                                    msg => _logger.Debug(LogCategory.Synthesis, msg));
//                                var synthDuration = (DateTime.UtcNow - synthStartTime).TotalMilliseconds;

//                                if (result?.Reason == ResultReason.SynthesizingAudioCompleted)
//                                {
//                                    _logger.Info(LogCategory.Synthesis, $"[IN] Completed in {synthDuration:F0}ms");

//                                    synthesizedQueue.Enqueue((original, translatedForSynthesis, result));
//                                    synthesizedSemaphore.Release();

//                                    int queueSize = synthesizedQueue.Count;
//                                    ThreadSafeMetrics.UpdatePeak(ref _incomingQueuePeak, queueSize);
//                                    _logger.Debug(LogCategory.Queue, $"[IN] Queue size: {queueSize}");
//                                }
//                            }
//                            catch (Exception ex)
//                            {
//                                _logger.Error(LogCategory.Synthesis, $"[IN] Synthesis error: {ex.Message}");
//                            }
//                        }, ct);
//                    }
//                }
//            };

//            _incomingRecognizer.Canceled += (s, e) =>
//            {
//                if (e.Reason == CancellationReason.Error)
//                {
//                    _ = Dispatcher.UIThread.InvokeAsync(() =>
//                    {
//                        _logger.Error(LogCategory.Recognition, $"[IN] Recognition ERROR: {e.ErrorCode} - {e.ErrorDetails}");
//                    });
//                }
//            };

//            _incomingRecognizer.SessionStarted += (s, e) =>
//            {

//                if (!_incomingSessionNotified)
//                {
//                    _incomingSessionNotified = true;

//                    _ = Dispatcher.UIThread.InvokeAsync(() =>
//                        _logger.Info(LogCategory.System, "[IN] ✅ Session started - Listening to meeting audio..."));
//                }
//                else
//                {
//                    _logger.Debug(LogCategory.System, "[IN] Session resumed");
//                }

//                //if (!_incomingSessionNotified)
//                //{
//                //    _incomingSessionNotified = true;

//                //    _ = Dispatcher.UIThread.InvokeAsync(() =>
//                //        _logger.Info(LogCategory.System, "[IN] ✅ Session started - Listening to meeting audio..."));
//                //}
//                //else
//                //{
//                //    _logger.Debug(LogCategory.System, "[IN] Session resumed");
//                //}
//            };

//            //if (!_isMicrophoneMuted)
//            //{
//            //    await _incomingRecognizer.StartContinuousRecognitionAsync();
//            //    _incomingWaveIn.StartRecording();
//            //    _logger.Info(LogCategory.AudioCapture, "[IN] Audio capture started");
//            //}
//            //else
//            //{
//            //    _logger.Info(LogCategory.Incoming, "Started in MUTED state");
//            //}

//            //while (!ct.IsCancellationRequested)
//            //    await Task.Delay(100, ct);





//            // ✅ Always start recording (regardless of mute state)
//            _incomingWaveIn.StartRecording();
//            _logger.Info(LogCategory.AudioCapture, "[IN] VB Cable audio capture started");

//            // ✅ FIX: Initialize activity timestamp to NOW (assume cable is ready)
//            // This prevents showing "disconnected" immediately after starting
//            _lastIncomingAudioActivity = DateTime.UtcNow;

//            // ✅ Start Azure recognition based on mute state
//            if (!_isSpeakerMuted)
//            {
//                await _incomingRecognizer.StartContinuousRecognitionAsync();
//                _logger.Info(LogCategory.Incoming, "[IN] Translation enabled");
//            }
//            else
//            {
//                _logger.Info(LogCategory.Incoming, "[IN] Started in MUTED state (capture active, translation disabled)");
//            }

//            // ✅ MONITOR: Watch for meeting audio data flow
//            _ = Task.Run(async () =>
//            {
//                bool lastReportedState = false;
//                bool initialConnectionDetected = false;

//                // ✅ Wait a moment before first check (give WaveIn time to start)
//                await Task.Delay(500, ct);

//                while (!ct.IsCancellationRequested)
//                {
//                    await Task.Delay(1000, ct);

//                    var timeSinceActivity = (DateTime.UtcNow - _lastIncomingAudioActivity).TotalSeconds;
//                    bool isActive = timeSinceActivity < 3.0;

//                    // Only notify when state changes
//                    if (isActive != lastReportedState)
//                    {
//                        MeetingAudioActivityChanged?.Invoke(this, isActive);
//                        lastReportedState = isActive;

//                        if (isActive && !initialConnectionDetected)
//                        {
//                            initialConnectionDetected = true;
//                            _logger.Info(LogCategory.Device,
//                                "🟢 Meeting audio detected - Connection established!");
//                        }
//                        else if (!isActive && initialConnectionDetected)
//                        {
//                            _logger.Warning(LogCategory.Device,
//                                "🔴 Meeting audio stopped - Connection lost");
//                        }
//                    }
//                }

//                // Final cleanup
//                if (lastReportedState)
//                {
//                    MeetingAudioActivityChanged?.Invoke(this, false);
//                }
//            }, ct);

//            while (!ct.IsCancellationRequested)
//                await Task.Delay(100, ct);



//        }
//        catch (OperationCanceledException) { }
//        catch (Exception ex)
//        {
//            _logger.Error(LogCategory.Incoming, $"EXCEPTION: {ex.Message}");
//        }
//        finally
//        {
//            // ✅ Ensure indicator shows disconnected when flow ends
//            MeetingAudioActivityChanged?.Invoke(this, false);
//            _logger.Info(LogCategory.Device, "[IN] 🔴 VB Cable disconnected");

//            if (_incomingWaveIn != null)
//            {
//                try
//                {
//                    _incomingWaveIn.StopRecording();
//                }
//                catch { }

//                _incomingWaveIn.Dispose();
//                _incomingWaveIn = null;
//            }

//            if (_incomingRecognizer != null)
//            {
//                try
//                {
//                    await _incomingRecognizer.StopContinuousRecognitionAsync();
//                }
//                catch { }

//                _incomingRecognizer.Dispose();
//                _incomingRecognizer = null;
//            }

//            pushStream?.Dispose();
//            synthesizer?.Dispose();

//            _logger.Info(LogCategory.Incoming, $"Stopped - Blocks: {audioBlockCount}, Passed: {audioPassCount}");
//        }
//        //finally
//        //{
//        //    if (_incomingWaveIn != null)
//        //    {
//        //        try
//        //        {
//        //            _incomingWaveIn.StopRecording();
//        //        }
//        //        catch { }

//        //        _incomingWaveIn.Dispose();
//        //        _incomingWaveIn = null;
//        //    }

//        //    if (_incomingRecognizer != null)
//        //    {
//        //        try
//        //        {
//        //            await _incomingRecognizer.StopContinuousRecognitionAsync();
//        //        }
//        //        catch { }

//        //        _incomingRecognizer.Dispose();
//        //        _incomingRecognizer = null;
//        //    }

//        //    pushStream?.Dispose();
//        //    synthesizer?.Dispose();

//        //    _logger.Info(LogCategory.Incoming, $"Stopped - Blocks: {audioBlockCount}, Passed: {audioPassCount}");
//        //}
//    }

//    private async Task ProcessIncomingQueueOptimized(ConcurrentQueue<(string original, string translated, SpeechSynthesisResult result)> queue, SemaphoreSlim semaphore, MMDevice? physicalSpeaker, CancellationToken ct)
//    {
//        while (!ct.IsCancellationRequested)
//        {
//            try
//            {
//                await semaphore.WaitAsync(ct);
//            }
//            catch (OperationCanceledException)
//            {
//                break;
//            }

//            if (!queue.TryDequeue(out var item))
//                continue;

//            if (ct.IsCancellationRequested || _isSpeakerMuted)
//                continue;

//            try
//            {
//                await _incomingPlaybackLock.WaitAsync(ct);
//            }
//            catch (OperationCanceledException)
//            {
//                break;
//            }

//            try
//            {
//                _isPlayingIncomingAudio = true;

//                var audioDurationMs = AudioPlaybackManager.CalculateAudioDuration(item.result.AudioData.Length, AudioConfiguration.SampleRate,
//                    AudioConfiguration.Channels, AudioConfiguration.BitsPerSample);

//                // ✅ Conditional microphone pause based on parallel flow mode
//                bool shouldPauseMicrophone = !_enableParallelFlow && !_isMicrophoneMuted;
//                int earlyResumeMs = 0;

//                if (shouldPauseMicrophone && _outgoingRecognizer != null)
//                {
//                    try
//                    {
//                        // Calculate dynamic buffer for echo prevention
//                        var dynamicBufferMs = AudioConfiguration.BaseAcousticEchoBufferMs +
//                                              (int)(audioDurationMs * AudioConfiguration.AcousticEchoBufferPercentage);
//                        dynamicBufferMs = Math.Max(AudioConfiguration.MinAcousticEchoBufferMs,
//                                                   Math.Min(dynamicBufferMs, AudioConfiguration.MaxAcousticEchoBufferMs));

//                        earlyResumeMs = Math.Max(0, dynamicBufferMs - 50);

//                        await _outgoingRecognizer.StopContinuousRecognitionAsync();
//                        _logger.Debug(LogCategory.Playback, $"[IN] 🔇 Mic paused: {audioDurationMs:F0}ms + {dynamicBufferMs}ms buffer");
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.Warning(LogCategory.Playback, $"[IN] Failed to pause microphone: {ex.Message}");
//                    }
//                }
//                else if (_enableParallelFlow)
//                {
//                    _logger.Debug(LogCategory.Playback, "[IN] ⚡ Parallel mode: Microphone stays active");
//                }

//                _logger.Info(LogCategory.Playback, $"[IN] Playing audio ({audioDurationMs:F0}ms) to speakers...");

//                SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
//                {
//                    OriginalText = item.original,
//                    TranslatedText = item.translated,
//                    IsFromMeeting = true,
//                    IsSynthesizing = true
//                });

//                // Play audio (now potentially parallel with outgoing recognition)

//                // ✅ NEW: Apply audio ducking in parallel flow mode
//                if (_enableParallelFlow && AudioConfiguration.EnableAudioDucking)
//                {
//                    //await AudioPlaybackManager.PlayAudioToPhysicalSpeaker(item.result.AudioData, physicalSpeaker);
//                    await PlayAudioWithDucking(item.result.AudioData, physicalSpeaker, ct);
//                }
//                else
//                {
//                    // Standard playback without ducking
//                    await AudioPlaybackManager.PlayAudioToPhysicalSpeaker(item.result.AudioData, physicalSpeaker);
//                }



//                // Only wait for buffer if we paused the microphone
//                if (shouldPauseMicrophone && earlyResumeMs > 0)
//                {
//                    await Task.Delay(earlyResumeMs, ct);
//                }

//                SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
//                {
//                    OriginalText = item.original,
//                    TranslatedText = item.translated,
//                    IsFromMeeting = true,
//                    IsSynthesizing = false
//                });

//                // Resume microphone only if we paused it
//                if (shouldPauseMicrophone && _outgoingRecognizer != null)
//                {
//                    try
//                    {
//                        await _outgoingRecognizer.StartContinuousRecognitionAsync();
//                        _logger.Debug(LogCategory.Playback, "[IN] 🔊 Mic resumed");
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.Warning(LogCategory.Playback, $"[IN] Failed to resume microphone: {ex.Message}");
//                    }
//                }
//            }
//            catch (OperationCanceledException) { }
//            catch (Exception ex)
//            {
//                _logger.Error(LogCategory.Playback, $"[IN] Playback error: {ex.Message}");
//            }
//            finally
//            {
//                _isPlayingIncomingAudio = false;
//                _incomingPlaybackLock.Release();
//            }
//        }

//        _logger.Debug(LogCategory.Queue, "[IN] Queue processor finished");
//    }

//    public bool ValidateCableConnection()
//    {
//        try
//        {
//            var cableDevice = _deviceService.FindIncomingCableDevice();

//            if (cableDevice == null)
//            {
//                _logger.Error(LogCategory.Device, "❌ VB Cable device not found");
//                return false;
//            }

//            if (cableDevice.State != DeviceState.Active)
//            {
//                _logger.Error(LogCategory.Device, $"❌ VB Cable is {cableDevice.State}");
//                return false;
//            }

//            _logger.Info(LogCategory.Device, $"✅ VB Cable found: {cableDevice.FriendlyName}");
//            return true;
//        }
//        catch (Exception ex)
//        {
//            _logger.Error(LogCategory.Device, $"❌ Cable check failed: {ex.Message}");
//            return false;
//        }
//    }
//    //private async Task ProcessIncomingQueueOptimized(ConcurrentQueue<(string original, string translated, SpeechSynthesisResult result)> queue, SemaphoreSlim semaphore, MMDevice? physicalSpeaker, CancellationToken ct)
//    //{
//    //    while (!ct.IsCancellationRequested)
//    //    {
//    //        try
//    //        {
//    //            await semaphore.WaitAsync(ct);
//    //        }
//    //        catch (OperationCanceledException)
//    //        {
//    //            break;
//    //        }

//    //        if (!queue.TryDequeue(out var item))
//    //            continue;

//    //        if (ct.IsCancellationRequested || _isSpeakerMuted)
//    //            continue;

//    //        try
//    //        {
//    //            await _incomingPlaybackLock.WaitAsync(ct);
//    //        }
//    //        catch (OperationCanceledException)
//    //        {
//    //            break;
//    //        }

//    //        try
//    //        {
//    //            _isPlayingIncomingAudio = true;

//    //            var audioDurationMs = AudioPlaybackManager.CalculateAudioDuration(item.result.AudioData.Length, AudioConfiguration.SampleRate,
//    //                AudioConfiguration.Channels, AudioConfiguration.BitsPerSample);

//    //            // ✅ OPTIMIZED: Smarter buffer calculation
//    //            var dynamicBufferMs = AudioConfiguration.BaseAcousticEchoBufferMs +
//    //                                  (int)(audioDurationMs * AudioConfiguration.AcousticEchoBufferPercentage);
//    //            dynamicBufferMs = Math.Max(AudioConfiguration.MinAcousticEchoBufferMs,
//    //                                       Math.Min(dynamicBufferMs, AudioConfiguration.MaxAcousticEchoBufferMs));

//    //            // ✅ OPTIMIZATION: Start mic resume 50ms early (predictive)
//    //            var earlyResumeMs = Math.Max(0, dynamicBufferMs - 50);

//    //            // ✅ PAUSE microphone for echo prevention
//    //            if (_outgoingRecognizer != null && !_isMicrophoneMuted)
//    //            {
//    //                try
//    //                {
//    //                    await _outgoingRecognizer.StopContinuousRecognitionAsync();
//    //                    _logger.Info(LogCategory.Playback, $"[IN] 🔇 Mic paused: {audioDurationMs:F0}ms + {dynamicBufferMs}ms buffer (100% echo prevention)");
//    //                }
//    //                catch (Exception ex)
//    //                {
//    //                    _logger.Warning(LogCategory.Playback, $"[IN] Failed to pause microphone: {ex.Message}");
//    //                }
//    //            }

//    //            _logger.Info(LogCategory.Playback, $"[IN] Playing audio ({audioDurationMs:F0}ms) to speakers...");

//    //            SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
//    //            {
//    //                OriginalText = item.original,
//    //                TranslatedText = item.translated,
//    //                IsFromMeeting = true,
//    //                IsSynthesizing = true
//    //            });

//    //            // ✅ Play audio
//    //            await AudioPlaybackManager.PlayAudioToPhysicalSpeaker(item.result.AudioData, physicalSpeaker);

//    //            // ✅ OPTIMIZED: Wait only the early resume time
//    //            if (earlyResumeMs > 0)
//    //            {
//    //                await Task.Delay(earlyResumeMs, ct);
//    //            }

//    //            SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
//    //            {
//    //                OriginalText = item.original,
//    //                TranslatedText = item.translated,
//    //                IsFromMeeting = true,
//    //                IsSynthesizing = false
//    //            });

//    //            _logger.Debug(LogCategory.Playback, "[IN] Playback completed, buffer: {dynamicBufferMs}ms");

//    //            // ✅ RESUME microphone (50ms earlier than before)
//    //            if (_outgoingRecognizer != null && !_isMicrophoneMuted)
//    //            {
//    //                try
//    //                {
//    //                    await _outgoingRecognizer.StartContinuousRecognitionAsync();
//    //                    _logger.Info(LogCategory.Playback, "[IN] 🔊 Mic resumed (optimized timing)");
//    //                }
//    //                catch (Exception ex)
//    //                {
//    //                    _logger.Warning(LogCategory.Playback, $"[IN] Failed to resume microphone: {ex.Message}");
//    //                }
//    //            }
//    //        }
//    //        catch (OperationCanceledException) { }
//    //        catch (Exception ex)
//    //        {
//    //            _logger.Error(LogCategory.Playback, $"[IN] Playback error: {ex.Message}");
//    //        }
//    //        finally
//    //        {
//    //            _isPlayingIncomingAudio = false;
//    //            _incomingPlaybackLock.Release();
//    //        }
//    //    }

//    //    _logger.Debug(LogCategory.Queue, "[IN] Queue processor finished");
//    //}


//    //private async Task ProcessIncomingQueueOptimized(ConcurrentQueue<(string original, string translated, SpeechSynthesisResult result)> queue, SemaphoreSlim semaphore, MMDevice? physicalSpeaker, CancellationToken ct)
//    //{
//    //    while (!ct.IsCancellationRequested)
//    //    {
//    //        try
//    //        {
//    //            await semaphore.WaitAsync(ct);
//    //        }
//    //        catch (OperationCanceledException)
//    //        {
//    //            break;
//    //        }

//    //        if (!queue.TryDequeue(out var item))
//    //            continue;

//    //        if (ct.IsCancellationRequested || _isSpeakerMuted)
//    //            return;

//    //        try
//    //        {
//    //            await _incomingPlaybackLock.WaitAsync(ct);
//    //        }
//    //        catch (OperationCanceledException)
//    //        {
//    //            break;
//    //        }

//    //        try
//    //        {
//    //            _isPlayingIncomingAudio = true;

//    //            var audioDurationMs = AudioPlaybackManager.CalculateAudioDuration(item.result.AudioData.Length, AudioConfiguration.SampleRate,
//    //                AudioConfiguration.Channels, AudioConfiguration.BitsPerSample);

//    //            _logger.Info(LogCategory.Playback, $"[IN] Playing audio ({audioDurationMs:F0}ms) to speakers...");

//    //            // ✅ Notify UI that synthesis is starting
//    //            SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
//    //            {
//    //                OriginalText = item.original,
//    //                TranslatedText = item.translated,
//    //                IsFromMeeting = true,
//    //                IsSynthesizing = true
//    //            });

//    //            await AudioPlaybackManager.PlayAudioToPhysicalSpeaker(item.result.AudioData, physicalSpeaker);

//    //            // ✅ Notify UI that synthesis is complete
//    //            SynthesizingStatusChanged?.Invoke(this, new SynthesizingEventArgs
//    //            {
//    //                OriginalText = item.original,
//    //                TranslatedText = item.translated,
//    //                IsFromMeeting = true,
//    //                IsSynthesizing = false
//    //            });

//    //            _logger.Debug(LogCategory.Playback, "[IN] Playback completed");
//    //        }
//    //        catch (OperationCanceledException) { }
//    //        catch (Exception ex)
//    //        {
//    //            _logger.Error(LogCategory.Playback, $"[IN] Playback error: {ex.Message}");
//    //        }
//    //        finally
//    //        {
//    //            _isPlayingIncomingAudio = false;
//    //            _incomingPlaybackLock.Release();
//    //        }
//    //    }

//    //    _logger.Debug(LogCategory.Queue, "[IN] Queue processor finished");
//    //}



//    /// <summary>
//    /// Plays audio with smart volume ducking when user speaks (simplified version).
//    /// </summary>
//    /// <summary>
//    /// Plays audio with smart volume ducking when user speaks.
//    /// Uses logarithmic volume scaling for accurate perception.
//    /// </summary>
//    private async Task PlayAudioWithDucking(byte[] audioData, MMDevice? device, CancellationToken ct)
//    {
//        try
//        {
//            using var ms = new MemoryStream(audioData);
//            using var rawSource = new RawSourceWaveStream(ms, new WaveFormat(
//                AudioConfiguration.SampleRate,
//                AudioConfiguration.BitsPerSample,
//                AudioConfiguration.Channels));

//            var volumeProvider = new NAudio.Wave.SampleProviders.VolumeSampleProvider(rawSource.ToSampleProvider());
//            volumeProvider.Volume = AudioConfiguration.NormalVolumeLevel;

//            IWavePlayer? outputDevice = null;

//            // Try specific device first
//            if (device != null)
//            {
//                try
//                {
//                    outputDevice = new WasapiOut(device, AudioClientShareMode.Shared, false, 50);
//                    outputDevice.Init(volumeProvider);
//                    outputDevice.Play();
//                    _logger.Debug(LogCategory.Playback, $"[IN] Using specific device: {device.FriendlyName}");
//                }
//                catch (Exception ex)
//                {
//                    _logger.Warning(LogCategory.Playback, $"[IN] Failed to use specific device: {ex.Message}");
//                    outputDevice?.Dispose();
//                    outputDevice = null;
//                    ms.Position = 0;
//                }
//            }

//            // Fallback to default WASAPI
//            if (outputDevice == null)
//            {
//                try
//                {
//                    outputDevice = new WasapiOut(AudioClientShareMode.Shared, 50);
//                    outputDevice.Init(volumeProvider);
//                    outputDevice.Play();
//                    _logger.Debug(LogCategory.Playback, "[IN] Using default WASAPI device");
//                }
//                catch (Exception ex)
//                {
//                    _logger.Warning(LogCategory.Playback, $"[IN] Failed to use default WASAPI: {ex.Message}");
//                    outputDevice?.Dispose();
//                    outputDevice = null;
//                    ms.Position = 0;
//                }
//            }

//            // Final fallback to DirectSound
//            if (outputDevice == null)
//            {
//                outputDevice = new DirectSoundOut();
//                outputDevice.Init(volumeProvider);
//                outputDevice.Play();
//                _logger.Debug(LogCategory.Playback, "[IN] Using DirectSound fallback");
//            }

//            using (outputDevice)
//            {
//                float currentVolume = AudioConfiguration.NormalVolumeLevel;
//                bool wasDucked = false;
//                DateTime lastDuckTime = DateTime.MinValue;

//                while (outputDevice.PlaybackState == PlaybackState.Playing && !ct.IsCancellationRequested)
//                {
//                    await Task.Delay(AudioConfiguration.DuckingCheckIntervalMs, ct);

//                    // ✅ FIX: Check if user is ACTIVELY speaking (not meeting person)
//                    var timeSinceUserSpeech = (DateTime.UtcNow - _lastUserSpeechTime).TotalMilliseconds;
//                    bool userIsCurrentlySpeaking = _isUserSpeaking && timeSinceUserSpeech < 400;

//                    // ✅ FIX: Use logarithmic volume for accurate perception
//                    float targetVolume;
//                    if (userIsCurrentlySpeaking)
//                    {
//                        // Duck aggressively when user speaks
//                        targetVolume = AudioConfiguration.DuckedVolumeLevel;
//                        lastDuckTime = DateTime.UtcNow;
//                    }
//                    else if ((DateTime.UtcNow - lastDuckTime).TotalMilliseconds < 400)
//                    {
//                        // Grace period: stay ducked briefly after user stops
//                        targetVolume = AudioConfiguration.DuckedVolumeLevel;
//                    }
//                    else
//                    {
//                        // Restore full volume
//                        targetVolume = AudioConfiguration.NormalVolumeLevel;
//                        _isUserSpeaking = false; // ✅ Reset flag
//                    }

//                    // ✅ FIX: Smooth transition with proper step calculation
//                    if (Math.Abs(currentVolume - targetVolume) > 0.01f)
//                    {
//                        int stepsNeeded = (int)(AudioConfiguration.VolumeTransitionMs / (float)AudioConfiguration.DuckingCheckIntervalMs);
//                        float step = (targetVolume - currentVolume) / stepsNeeded;
//                        currentVolume += step;
//                        currentVolume = Math.Clamp(currentVolume, 0.01f, 1.0f); // Never fully silent

//                        // Apply volume
//                        volumeProvider.Volume = currentVolume;

//                        // Logging
//                        if (userIsCurrentlySpeaking && !wasDucked)
//                        {
//                            _logger.Info(LogCategory.Playback, $"[IN] 🔉 Ducking to {currentVolume * 100:F0}% (YOU are speaking)");
//                            wasDucked = true;
//                        }
//                        else if (!userIsCurrentlySpeaking && wasDucked && currentVolume > 0.9f)
//                        {
//                            _logger.Info(LogCategory.Playback, $"[IN] 🔊 Restored to {currentVolume * 100:F0}% (you stopped)");
//                            wasDucked = false;
//                        }
//                    }
//                }

//                outputDevice.Stop();
//            }
//        }
//        catch (Exception ex)
//        {
//            _logger.Error(LogCategory.Playback, $"[IN] Ducking playback error: {ex.Message}");
//            throw;
//        }
//    }


//    private void LogMetrics()
//    {
//        _logger.Info(LogCategory.Metrics, "***************************************************************");
//        _logger.Info(LogCategory.Metrics, "Translation Session Metrics:");
//        _logger.Info(LogCategory.Metrics, $"   Outgoing Queue Peak: {_outgoingQueuePeak}");
//        _logger.Info(LogCategory.Metrics, $"   Incoming Queue Peak: {_incomingQueuePeak}");
//        _logger.Info(LogCategory.Metrics, $"   Outgoing Messages Skipped: {_outgoingMessagesSkipped}");
//        _logger.Info(LogCategory.Metrics, $"   Incoming Messages Skipped: {_incomingMessagesSkipped}");

//        if (_totalRecognitions > 0)
//        {
//            var qualityRate = (1 - (_lowConfidenceCount / (double)_totalRecognitions)) * 100;
//            _logger.Info(LogCategory.Metrics, $"   Recognition Quality Rate: {qualityRate:F1}%");
//            _logger.Info(LogCategory.Metrics, $"   Low Confidence Detections: {_lowConfidenceCount}/{_totalRecognitions}");
//        }

//        _logger.Info(LogCategory.Metrics, "***************************************************************");
//    }

//    private void LogRaw(string message)
//    {
//        LogMessage?.Invoke(this, message);
//    }

//    public void Dispose()
//    {
//        Dispose(true);
//        GC.SuppressFinalize(this);
//    }

//    protected virtual void Dispose(bool disposing)
//    {
//        if (_disposed) return;

//        if (disposing)
//        {
//            _cts?.Cancel();
//            _cts?.Dispose();

//            _outgoingPlaybackLock?.Dispose();
//            _incomingPlaybackLock?.Dispose();

//            _muteLock?.Dispose();
//            _echoPreventionLock?.Dispose();
            
//            _outgoingTranscriptManager?.Dispose();
//            _incomingTranscriptManager?.Dispose();

//            _outgoingRecognizer?.Dispose();
//            _incomingRecognizer?.Dispose();
//            _incomingWaveIn?.Dispose();
//        }

//        _disposed = true;
//    }
//}
