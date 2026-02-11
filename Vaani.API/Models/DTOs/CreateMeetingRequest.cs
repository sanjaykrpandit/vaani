namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request model for creating a new meeting
/// </summary>
public class CreateMeetingRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string MeetingName { get; set; } = string.Empty;
    public string MeetingLanguage { get; set; } = string.Empty;
    public int AzureSubscriptionId { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public string? Password { get; set; }
}
