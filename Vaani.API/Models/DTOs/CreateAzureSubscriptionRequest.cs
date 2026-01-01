namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request model for creating a new Azure subscription
/// </summary>
public class CreateAzureSubscriptionRequest
{
    public string SubscriptionKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string? Description { get; set; }
}
