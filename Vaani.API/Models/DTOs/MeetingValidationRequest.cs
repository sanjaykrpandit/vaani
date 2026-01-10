namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request DTO for meeting validation
/// </summary>
public class MeetingValidationRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = "1.0.0";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
