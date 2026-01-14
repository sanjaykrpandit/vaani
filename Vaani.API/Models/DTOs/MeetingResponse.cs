namespace Vaani.API.Models.DTOs;

/// <summary>
/// Response model for meeting operations
/// </summary>
public class MeetingResponse
{
    public int Id { get; set; }
    public string MeetingId { get; set; } = string.Empty;
    public string MeetingName { get; set; } = string.Empty;
    public string MeetingLanguage { get; set; } = string.Empty;
    public int AzureSubscriptionId { get; set; }
    public LanguageDto? Lanuage { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; }
}
