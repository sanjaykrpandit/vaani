namespace Vaani.API.Models.Entities;

/// <summary>
/// Azure subscription entity for storing Azure configuration
/// </summary>
public class AzureSubscription
{
    public int Id { get; set; }
    public string SubscriptionKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation property
    public ICollection<Meeting> Meetings { get; set; } = new List<Meeting>();
}
