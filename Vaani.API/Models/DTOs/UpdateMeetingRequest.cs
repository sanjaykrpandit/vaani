namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request model for updating an existing meeting
/// </summary>
public class UpdateMeetingRequest
{
    public string? MeetingName { get; set; }
    public string? MeetingLanguage { get; set; }
    public int? AzureSubscriptionId { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool? IsActive { get; set; }
    public string? Password { get; set; }
    public bool? ClearPassword { get; set; } // Set to true to remove password protection
}
