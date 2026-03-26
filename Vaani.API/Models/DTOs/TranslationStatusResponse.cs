namespace Vaani.API.Models.DTOs;

/// <summary>
/// Current status snapshot of a translation session
/// </summary>
public class TranslationStatusResponse
{
    public string TranslationSessionId { get; set; } = string.Empty;
    public string MeetingId { get; set; } = string.Empty;
    public TranslationSessionStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public bool IsMicrophoneMuted { get; set; }
    public bool IsSpeakerMuted { get; set; }
    public int TotalRecognitions { get; set; }
    public int TotalTranslations { get; set; }
    public int ErrorCount { get; set; }
    public string? LastError { get; set; }
}

public enum TranslationSessionStatus
{
    Starting = 0,
    Running = 1,
    Stopping = 2,
    Stopped = 3,
    Error = 4
}
