namespace Vaani.API.Models.DTOs;

/// <summary>
/// Response DTO for meeting validation
/// </summary>
public class MeetingValidationResponse
{
    public bool IsValid { get; set; }
    public string MeetingName { get; set; } = string.Empty;
    public string EncryptedConfig { get; set; } = string.Empty;
    public DateTime ValidUntil { get; set; }
    public int RemainingMinutes { get; set; }
    public MeetingFeaturesDto Features { get; set; } = new();
    public string SessionToken { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }

    /// <summary>
    /// SignalR hub URL for backend translation.
    /// Desktop uses this instead of direct Azure credentials.
    /// e.g. https://api.vaani.com/hubs/translation
    /// </summary>
    public string BackendTranslationHubUrl { get; set; } = string.Empty;
}

public class MeetingFeaturesDto
{
    public bool AllowReconnect { get; set; } = true;
    public int HeartbeatIntervalSeconds { get; set; } = 60;
    public bool EnableLocalCache { get; set; } = false;
}
