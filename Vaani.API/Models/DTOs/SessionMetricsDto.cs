namespace Vaani.API.Models.DTOs;

/// <summary>
/// DTO for session analytics and metrics
/// </summary>
public class SessionMetricsDto
{
    public string MeetingId { get; set; } = string.Empty;
    public string MeetingName { get; set; } = string.Empty;
    public int TotalSessions { get; set; }
    public int ActiveSessions { get; set; }
    public int EndedSessions { get; set; }
    public int ExpiredSessions { get; set; }
    public double AverageSessionDurationMinutes { get; set; }
    public int TotalHeartbeats { get; set; }
    public DateTime? FirstSessionStarted { get; set; }
    public DateTime? LastSessionActivity { get; set; }
    public List<DeviceMetricDto> DeviceBreakdown { get; set; } = new();
    public List<SessionTimelineDto> SessionTimeline { get; set; } = new();
    public List<SessionDetailDto> RecentSessions { get; set; } = new();
}

public class DeviceMetricDto
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public int SessionCount { get; set; }
    public double TotalMinutes { get; set; }
    public DateTime LastActivity { get; set; }
}

public class SessionTimelineDto
{
    public DateTime Date { get; set; }
    public int SessionCount { get; set; }
    public int TotalHeartbeats { get; set; }
}

public class SessionDetailDto
{
    public int SessionId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public string Status { get; set; } = string.Empty;
    public double DurationMinutes { get; set; }
    public int HeartbeatCount { get; set; }
}

public class SessionLogDto
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
}
