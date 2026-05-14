namespace Lipi.Models;

public class MeetingValidationRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = "1.0.0";
    public string? Password { get; set; }
}

public class MeetingValidationResponse
{
    public bool IsValid { get; set; }
    public string MeetingName { get; set; } = string.Empty;
    public string EncryptedConfig { get; set; } = string.Empty;
    public DateTime ValidUntil { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public string BackendTranslationHubUrl { get; set; } = string.Empty;
}

public class SessionStartResponse
{
    public bool Success { get; set; }
    public int? SessionId { get; set; }
    public string? Message { get; set; }
}

public class MeetingConfiguration
{
    public string MeetingId { get; set; } = string.Empty;
    public string MeetingName { get; set; } = string.Empty;
    public TranslationConfiguration TranslationConfig { get; set; } = new();
    public List<LanguageInfo> AvailableLanguages { get; set; } = [];
    public string BackendTranslationHubUrl { get; set; } = string.Empty;
}

public class TranslationConfiguration
{
    public LanguageConfiguration VendorLanguage { get; set; } = new();
    public LanguageConfiguration OrganizerLanguage { get; set; } = new();
}

public class LanguageConfiguration
{
    public string Code { get; set; } = string.Empty;
}

public class LanguageInfo
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class LipiSessionContext
{
    public string MeetingId { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string HubUrl { get; set; } = string.Empty;
    public List<LanguageInfo> AvailableLanguages { get; set; } = [];
    public string SourceLanguage { get; set; } = string.Empty;
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

public class LipiDirectTranscriptEntry
{
    public string OriginalText { get; set; } = string.Empty;
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime RecognizedAtUtc { get; set; } = DateTime.UtcNow;
}

public class LipiDirectTranscriptBatchRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? SourceLanguage { get; set; }
    public List<string>? TargetLanguages { get; set; }
    public List<LipiDirectTranscriptEntry> Entries { get; set; } = [];
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
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class LipiDirectConversationalRewriteResponse
{
    public bool Success { get; set; }
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}

public class ConversationalDictionaryEntryDto
{
    public string FormalText { get; set; } = string.Empty;
    public string ConversationalText { get; set; } = string.Empty;
    public string MatchMode { get; set; } = "Contains";
}

public class LipiDirectDictionaryResponse
{
    public bool Success { get; set; }
    public string DictionaryVersion { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public Dictionary<string, List<ConversationalDictionaryEntryDto>> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}