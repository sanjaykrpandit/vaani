namespace Vaani.API.Models.DTOs;

public class LipiStartRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }
    public string SourceLanguage { get; set; } = "en-US";
    public List<string> TargetLanguages { get; set; } = [];
    public string AudioFormat { get; set; } = "Raw16Khz16BitMonoPcm";
}

public class LipiStartResponse
{
    public bool Success { get; set; }
    public string? LipiSessionId { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public string HubUrl { get; set; } = "/hubs/lipi";
}

public class LipiAudioChunkDto
{
    public string LipiSessionId { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public long SequenceNumber { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}

public class LipiEventDto
{
    public string LipiSessionId { get; set; } = string.Empty;
    public LipiEventType EventType { get; set; }
    public string? OriginalText { get; set; }
    public Dictionary<string, string>? Translations { get; set; }
    public string? SystemMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class LipiStatusResponse
{
    public string LipiSessionId { get; set; } = string.Empty;
    public string MeetingId { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public IReadOnlyList<string> TargetLanguages { get; set; } = [];
    public bool IsRunning { get; set; }
    public DateTime StartedAt { get; set; }
    public int TotalRecognitions { get; set; }
    public int ErrorCount { get; set; }
    public string? LastError { get; set; }
}

public enum LipiEventType
{
    Recognizing = 1,
    Recognized = 2,
    SessionStarted = 3,
    SessionStopped = 4,
    Error = 5
}