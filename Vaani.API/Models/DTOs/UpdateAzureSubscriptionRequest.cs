namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request model for updating an Azure subscription
/// </summary>
public class UpdateAzureSubscriptionRequest
{
    public string? SubscriptionKey { get; set; }
    public string? Region { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}
