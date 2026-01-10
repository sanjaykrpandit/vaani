namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request model for creating a session
/// </summary>
public class CreateSessionRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
}
