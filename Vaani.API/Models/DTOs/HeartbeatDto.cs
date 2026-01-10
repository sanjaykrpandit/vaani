namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request DTO for session heartbeat
/// </summary>
public class HeartbeatRequest
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response DTO for session heartbeat
/// </summary>
public class HeartbeatResponse
{
    public bool Success { get; set; }
    public int RemainingMinutes { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
}
