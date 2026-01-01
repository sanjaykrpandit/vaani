using Vaani.API.Models.DTOs;

namespace Vaani.API.Interfaces;

/// <summary>
/// Interface for Azure subscription management
/// </summary>
public interface IAzureSubscriptionService
{
    Task<AzureSubscriptionResponse?> CreateSubscriptionAsync(CreateAzureSubscriptionRequest request);
    Task<AzureSubscriptionResponse?> UpdateSubscriptionAsync(int id, UpdateAzureSubscriptionRequest request);
    Task<bool> DeleteSubscriptionAsync(int id);
    Task<AzureSubscriptionResponse?> GetSubscriptionByIdAsync(int id);
    Task<IEnumerable<AzureSubscriptionResponse>> GetAllSubscriptionsAsync();
}
