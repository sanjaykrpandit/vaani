namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request DTO for ending a session
/// </summary>
public class EndSessionRequest
{
    public string? meetingId { get; set; }
    public string? deviceId { get; set; }
    public string SessionLog { get; set; } = string.Empty;
    public string SessionTrascript { get; set; } = string.Empty;
}

/// <summary>
/// Session statistics DTO (for response/logging purposes)
/// </summary>
public class SessionStatistics
{
    public string MeetingId { get; set; } = string.Empty;
    public string MeetingName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public string DeviceId { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO for ending a session
/// </summary>
public class EndSessionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? DurationMinutes { get; set; }
    public string? ErrorCode { get; set; }
}
