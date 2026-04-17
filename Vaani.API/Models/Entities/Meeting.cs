namespace Vaani.API.Models.Entities;

/// <summary>
/// Meeting entity representing a translation meeting session
/// </summary>
public class Meeting
{
    public int Id { get; set; }
    public string MeetingId { get; set; } = string.Empty;
    public string MeetingName { get; set; } = string.Empty;
    public string MeetingLanguage { get; set; } = string.Empty;
    public int AzureSubscriptionId { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public string PublicToken { get; set; } = string.Empty;
    
    // Password protection
    public bool RequiresPassword { get; set; } = false;
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }

    // Navigation properties
    public AzureSubscription AzureSubscription { get; set; } = null!;
    public ICollection<Session> Sessions { get; set; } = new List<Session>();

}
