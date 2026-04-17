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