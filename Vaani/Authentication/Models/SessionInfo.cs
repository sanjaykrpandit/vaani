using System;

namespace Vaani.Authentication.Models;

/// <summary>
/// Information about the current active session
/// </summary>
public class SessionInfo
{
    /// <summary>
    /// Meeting configuration for this session
    /// </summary>
    public MeetingConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// UTC timestamp when session started
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// UTC timestamp when session expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Session token for API calls
    /// </summary>
    public string SessionToken { get; set; } = string.Empty;

    /// <summary>
    /// Device identifier for this session
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Last heartbeat timestamp
    /// </summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>
    /// Indicates if session is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Check if session has expired
    /// </summary>
    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Get remaining time in the session
    /// </summary>
    public TimeSpan GetRemainingTime()
    {
        var remaining = ExpiresAt - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Check if session is about to expire (within warning threshold)
    /// </summary>
    public bool IsExpiringIn(TimeSpan threshold)
    {
        return GetRemainingTime() <= threshold;
    }
}

public class MeetingSessionInfo
{
    public bool Success { get; set; }
    public int? SessionId { get; set; }
    public string? AccessToken { get; set; }
    public string? Message { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
