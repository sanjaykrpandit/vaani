using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Services;

/// <summary>
/// Service for Azure subscription management
/// </summary>
public class AzureSubscriptionService : IAzureSubscriptionService
{
    private readonly VaaniDbContext _dbContext;
    private readonly ILogger<AzureSubscriptionService> _logger;

    public AzureSubscriptionService(
        VaaniDbContext dbContext,
        ILogger<AzureSubscriptionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AzureSubscriptionResponse?> CreateSubscriptionAsync(CreateAzureSubscriptionRequest request)
    {
        try
        {
            var subscription = new AzureSubscription
            {
                SubscriptionKey = request.SubscriptionKey,
                Region = request.Region,
                Description = request.Description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.AzureSubscriptions.Add(subscription);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Azure subscription created: {Id}", subscription.Id);

            return MapToResponse(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Azure subscription");
            return null;
        }
    }

    public async Task<AzureSubscriptionResponse?> UpdateSubscriptionAsync(int id, UpdateAzureSubscriptionRequest request)
    {
        try
        {
            var subscription = await _dbContext.AzureSubscriptions.FindAsync(id);

            if (subscription == null)
            {
                _logger.LogWarning("Azure subscription not found for update: {Id}", id);
                return null;
            }

            if (request.SubscriptionKey != null) subscription.SubscriptionKey = request.SubscriptionKey;
            if (request.Region != null) subscription.Region = request.Region;
            if (request.Description != null) subscription.Description = request.Description;
            if (request.IsActive.HasValue) subscription.IsActive = request.IsActive.Value;

            subscription.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Azure subscription updated: {Id}", id);

            return MapToResponse(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Azure subscription: {Id}", id);
            return null;
        }
    }

    public async Task<bool> DeleteSubscriptionAsync(int id)
    {
        try
        {
            var subscription = await _dbContext.AzureSubscriptions.FindAsync(id);

            if (subscription == null)
            {
                _logger.LogWarning("Azure subscription not found for deletion: {Id}", id);
                return false;
            }

            // Check if subscription is being used by any meetings
            var hasActiveMeetings = await _dbContext.Meetings
                .AnyAsync(m => m.AzureSubscriptionId == id);

            if (hasActiveMeetings)
            {
                _logger.LogWarning("Cannot delete Azure subscription {Id} - it is being used by meetings", id);
                return false;
            }

            _dbContext.AzureSubscriptions.Remove(subscription);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Azure subscription deleted: {Id}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Azure subscription: {Id}", id);
            return false;
        }
    }

    public async Task<AzureSubscriptionResponse?> GetSubscriptionByIdAsync(int id)
    {
        try
        {
            var subscription = await _dbContext.AzureSubscriptions.FindAsync(id);
            return subscription != null ? MapToResponse(subscription) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Azure subscription: {Id}", id);
            return null;
        }
    }

    public async Task<IEnumerable<AzureSubscriptionResponse>> GetAllSubscriptionsAsync()
    {
        try
        {
            var subscriptions = await _dbContext.AzureSubscriptions
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            return subscriptions.Select(MapToResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all Azure subscriptions");
            return Enumerable.Empty<AzureSubscriptionResponse>();
        }
    }

    private AzureSubscriptionResponse MapToResponse(AzureSubscription subscription)
    {
        return new AzureSubscriptionResponse
        {
            Id = subscription.Id,
            SubscriptionKey = subscription.SubscriptionKey,
            Region = subscription.Region,
            Description = subscription.Description,
            IsActive = subscription.IsActive,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };
    }
}
