namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request to start a backend translation session
/// </summary>
public class TranslationStartRequest
{
    /// <summary>Meeting ID owning the Azure subscription</summary>
    public string MeetingId { get; set; } = string.Empty;

    /// <summary>Active session ID (from sessions table)</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Hub connection ID (set server-side from SignalR context)</summary>
    public string? ConnectionId { get; set; }

    /// <summary>Direction: Outgoing (user mic → meeting), Incoming (meeting → user), or Both</summary>
    public TranslationDirection Direction { get; set; } = TranslationDirection.Both;

    /// <summary>Source language code, e.g. en-US. Overrides meeting config when provided.</summary>
    public string? SourceLanguage { get; set; }

    /// <summary>Target language code, e.g. hi-IN. Overrides meeting config when provided.</summary>
    public string? TargetLanguage { get; set; }

    /// <summary>TTS voice for source language synthesis</summary>
    public string? SourceVoice { get; set; }

    /// <summary>TTS voice for target language synthesis</summary>
    public string? TargetVoice { get; set; }

    /// <summary>Audio format sent by the client (default: Raw16Khz16BitMonoPcm)</summary>
    public string AudioFormat { get; set; } = "Raw16Khz16BitMonoPcm";
}

public enum TranslationDirection
{
    Outgoing = 1,
    Incoming = 2,
    Both = 3,
    /// <summary>
    /// Both pipelines active for real-time transcription, but no TTS synthesis.
    /// Used when source and target language are the same (bypass mode).
    /// </summary>
    TranscribeBoth = 4
}
