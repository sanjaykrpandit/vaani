namespace Vaani.API.Models.DTOs;

/// <summary>
/// Response model for Azure subscription operations
/// </summary>
public class AzureSubscriptionResponse
{
    public int Id { get; set; }
    public string SubscriptionKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
