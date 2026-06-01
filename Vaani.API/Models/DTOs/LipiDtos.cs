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
    public Dictionary<string, string>? PreviewTranslations { get; set; }
    public bool IsFinal { get; set; }
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

public class LipiDirectTokenRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = "en-US";
    public List<string> TargetLanguages { get; set; } = [];
}

public class LipiDirectTokenResponse
{
    public bool Success { get; set; }
    public string SpeechToken { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public List<string> TargetLanguages { get; set; } = [];
    public DateTime ExpiresAtUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}

public class LipiDirectTranscriptEntryDto
{
    public string OriginalText { get; set; } = string.Empty;
    public Dictionary<string, string> Translations { get; set; } = [];
    public DateTime RecognizedAtUtc { get; set; } = DateTime.UtcNow;
}

public class LipiDirectTranscriptBatchRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? SourceLanguage { get; set; }
    public List<string>? TargetLanguages { get; set; }
    public List<LipiDirectTranscriptEntryDto> Entries { get; set; } = [];
}

public class LipiDirectTranscriptBatchResponse
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}

public class LipiDirectConversationalRewriteRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string Domain { get; set; } = "general";
    public string? OriginalText { get; set; }
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class LipiDirectConversationalRewriteResponse
{
    public bool Success { get; set; }
    public bool Applied { get; set; }
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}

public class LipiDirectClientErrorReportRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string? SourceLanguage { get; set; }
    public string? ConnectionMode { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public class LipiDirectClientErrorReportResponse
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}

public enum LipiEventType
{
    Recognizing = 1,
    Recognized = 2,
    SessionStarted = 3,
    SessionStopped = 4,
    Error = 5
}

public class ConversationalDictionaryEntryDto
{
    public string FormalText { get; set; } = string.Empty;
    public string ConversationalText { get; set; } = string.Empty;
    public string MatchMode { get; set; } = "Contains";
    public string Domain { get; set; } = "general";
}

public class LipiDirectDictionaryResponse
{
    public bool Success { get; set; }
    public string DictionaryVersion { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, List<ConversationalDictionaryEntryDto>> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}