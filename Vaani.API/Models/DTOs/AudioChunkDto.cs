namespace Vaani.API.Models.DTOs;

/// <summary>
/// A binary audio chunk streamed from the desktop client to the backend pipeline
/// </summary>
public class AudioChunkDto
{
    /// <summary>Translation session this chunk belongs to</summary>
    public string TranslationSessionId { get; set; } = string.Empty;

    /// <summary>Which pipeline direction this audio belongs to</summary>
    public AudioPipelineDirection Pipeline { get; set; } = AudioPipelineDirection.Outgoing;

    /// <summary>Raw PCM audio bytes (16kHz, 16-bit, mono)</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>Sequence number for ordering / gap detection</summary>
    public long SequenceNumber { get; set; }

    /// <summary>Client UTC timestamp when this chunk was captured</summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Translation event sent from backend back to the desktop client
/// </summary>
public class TranslationEventDto
{
    public string TranslationSessionId { get; set; } = string.Empty;
    public TranslationEventType EventType { get; set; }
    public AudioPipelineDirection Pipeline { get; set; }

    /// <summary>Recognised original text (Recognizing / Recognized events)</summary>
    public string? OriginalText { get; set; }

    /// <summary>Translated text</summary>
    public string? TranslatedText { get; set; }

    /// <summary>Synthesized audio bytes (AudioOutput event)</summary>
    public byte[]? AudioData { get; set; }

    /// <summary>System message (SessionStarted, Stopped, Error)</summary>
    public string? SystemMessage { get; set; }

    public DateTime? CapturedAtUtc { get; set; }
    public DateTime? RecognizedAtUtc { get; set; }
    public DateTime? SynthesisStartedAtUtc { get; set; }
    public DateTime? AudioGeneratedAtUtc { get; set; }
    public double? CaptureToRecognizedMs { get; set; }
    public double? RecognitionToSynthesisStartMs { get; set; }
    public double? SynthesisDurationMs { get; set; }
    public double? EndToEndLatencyMs { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum AudioPipelineDirection
{
    Outgoing = 1,
    Incoming = 2
}

public enum TranslationEventType
{
    Recognizing = 1,
    Recognized = 2,
    Translated = 3,
    AudioOutput = 4,
    SynthesizingStarted = 5,
    SynthesizingCompleted = 6,
    SessionStarted = 7,
    SessionStopped = 8,
    Error = 9
}
