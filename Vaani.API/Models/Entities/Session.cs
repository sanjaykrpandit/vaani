namespace Vaani.API.Models.Entities;

/// <summary>
/// Session entity representing an active client session
/// </summary>
public class Session
{
    public int Id { get; set; }
    public string MeetingId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Active"; // Active, Ended, Expired
    public string SessionLog { get; set; } = string.Empty;
    public string SessionTrascript { get; set; } = string.Empty;

    // Navigation property
    public Meeting? Meeting { get; set; }
    public ICollection<SessionLog> SessionLogs { get; set; } = new List<SessionLog>();
}
