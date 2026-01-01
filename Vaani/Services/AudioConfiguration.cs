namespace Vaani.Services;

/// <summary>
/// Configuration constants for audio processing, queuing, and retry behavior.
/// </summary>
public static class AudioConfiguration
{
    public const int SampleRate = 16000;
    public const int BitsPerSample = 16;
    public const int Channels = 1;
    public const int MaxQueueSize = 10;
    public const double EchoWindowSeconds = 0.1;
    public const int MaxTranscriptAgeMinutes = 5;
    public const int MaxRecentTranslations = 10;
    public const int SynthesisRetryAttempts = 2;
    public const int SynthesisRetryDelayMs = 50;
    public const int NormalBufferMs = 50;
    public const int MediumQueueBufferMs = 20;
    public const int LargeQueueBufferMs = 0;
    public const int IncomingNormalBufferMs = 20;
    public const int IncomingMediumBufferMs = 10;
    public const int IncomingLargeBufferMs = 0;
    public const int BufferMilliseconds = 20;
    public const int NumberOfBuffers = 3;
    public const float NoiseThreshold = 0.01f;
    public const int EchoPreventionMs = 50;

    // ✅ Azure Acoustic Echo Cancellation (.NET 10 Compatible)
    /// <summary>
    /// Enable Azure's built-in Acoustic Echo Cancellation (AEC) for microphone input.
    /// Works with .NET 10 and latest Azure Speech SDK.
    /// Set to false to use microphone pausing fallback.
    /// </summary>
    public const bool EnableAzureAEC = true;

    /// <summary>
    /// Enable Azure's noise suppression for cleaner audio recognition.
    /// Requires EnableAzureAEC to be true.
    /// </summary>
    public const bool EnableNoiseSuppression = true;

    /// <summary>
    /// Enable beamforming for directional microphone focus.
    /// Only works with array microphones. Safe to enable on all devices.
    /// </summary>
    public const bool EnableBeamforming = false;

    // ✅ Fallback: Microphone Pausing (used if AEC fails or single CABLE device)
    /// <summary>
    /// Base acoustic echo buffer time (ms) when using microphone pausing fallback.
    /// Only used if EnableAzureAEC = false or AEC initialization fails.
    /// </summary>
    public const int BaseAcousticEchoBufferMs = 150;

    /// <summary>
    /// Percentage of audio duration to add as echo buffer (microphone pausing fallback).
    /// </summary>
    public const double AcousticEchoBufferPercentage = 0.05;

    public const int MinAcousticEchoBufferMs = 80;

    /// <summary>
    /// Maximum acoustic echo buffer allowed (ms) for microphone pausing fallback.
    /// </summary>
    public const int MaxAcousticEchoBufferMs = 400;

    // ✅ NEW: Parallel Flow Configuration
    /// <summary>
    /// Enable automatic parallel flow detection when Cable A+B devices are detected.
    /// When true, the system will automatically enable parallel flow if both CABLE-A and CABLE-B are available.
    /// When false, always uses sequential mode (microphone pauses during incoming audio).
    /// </summary>
    public const bool AutoDetectParallelFlow = true;

    // Voice Activity Detection (VAD) Timeout Settings
    public const int InitialSilenceTimeoutMs = 2000;
   // public const int EndSilenceTimeoutMs = 500;
    public const int SegmentationSilenceTimeoutMs = 500;
    public const int OutgoingInitialSilenceTimeoutMs = 2000;
   // public const int OutgoingEndSilenceTimeoutMs = 500;


    // Current: 500ms
    public const int EndSilenceTimeoutMs = 400; // ⬇️ 400ms = faster recognition completion

    // Current: 500ms
    public const int OutgoingEndSilenceTimeoutMs = 400; // ⬇️ Match incoming for consistency

   

    // ✅ Keep only essential ducking configuration
    // Remove VolumeRestoreDelayMs - we'll use a simpler approach

    // ✅ Audio Ducking Configuration
    /// <summary>
    /// Enable audio ducking: automatically reduce incoming audio volume when user speaks.
    /// Only active in parallel flow mode.
    /// </summary>
    public const bool EnableAudioDucking = true;

 
    /// <summary>
    /// Normal volume level (0.0 to 1.0).
    /// Default: 1.0 (100% volume).
    /// </summary>
    public const float NormalVolumeLevel = 1.0f;

    /// <summary>
    /// Volume transition duration in milliseconds.
    /// Default: 150ms (smooth but responsive).
    /// </summary>
    public const int VolumeTransitionMs = 150;

    /// <summary>
    /// Interval for checking user speech state during audio playback (ms).
    /// Default: 50ms.
    /// </summary>
    public const int DuckingCheckIntervalMs = 50;



    /// <summary>
    /// Ducked volume level (0.0 to 1.0). 
    /// Uses LOGARITHMIC scaling for human perception.
    /// 0.30 = ~50% perceived volume (aggressive ducking)
    /// </summary>
    public const float DuckedVolumeLevel = 0.15f;

    /// <summary>
    /// Grace period before resuming full volume (ms).
    /// Prevents rapid volume changes during brief pauses.
    /// </summary>
    public const int VolumeRestoreGracePeriodMs = 400;

    /// <summary>
    /// Enable incoming audio capture during outgoing playback in parallel mode.
    /// When true, meeting person's voice can be captured while your voice plays.
    /// Only active in parallel flow mode (Cable A+B).
    /// </summary>
    public const bool AllowSimultaneousCapture = true;

    /// <summary>
    /// Reduced echo window for parallel mode (seconds).
    /// Shorter window allows faster audio pass-through.
    /// Default: 0.05 (50ms).
    /// </summary>
    public const double ParallelModeEchoWindowSeconds = 0.05;
}