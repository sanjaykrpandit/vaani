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
    public string AppVersion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public string Status { get; set; } = "Active"; // Active, Ended, Expired
    
    // Navigation property
    public Meeting? Meeting { get; set; }
    public ICollection<SessionLog> SessionLogs { get; set; } = new List<SessionLog>();
}
