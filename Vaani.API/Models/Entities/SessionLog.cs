namespace Vaani.API.Models.Entities;

/// <summary>
/// Session log entity for tracking session events
/// </summary>
public class SessionLog
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string EventType { get; set; } = string.Empty; // Heartbeat, Validation, EndSession
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }
    
    // Navigation property
    public Session? Session { get; set; }
}
