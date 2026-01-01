using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Services;

/// <summary>
/// Service for admin authentication and meeting management
/// </summary>
public class AdminService : IAdminService
{
    private readonly VaaniDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        VaaniDbContext dbContext,
        IJwtTokenService jwtTokenService,
        ILogger<AdminService> logger)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    public async Task<AdminLoginResponse> AuthenticateAsync(string userId, string password)
    {
        try
        {
            var admin = await _dbContext.AdminUsers
                .FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive);

            if (admin == null)
            {
                _logger.LogWarning("Login attempt with invalid userId: {UserId}", userId);
                return new AdminLoginResponse
                {
                    Success = false,
                    Message = "Invalid credentials"
                };
            }

            if (!VerifyPassword(password, admin.PasswordHash))
            {
                _logger.LogWarning("Login attempt with invalid password for userId: {UserId}", userId);
                return new AdminLoginResponse
                {
                    Success = false,
                    Message = "Invalid credentials"
                };
            }

            // Update last login time
            admin.LastLoginAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Generate JWT token
            var expiresAt = DateTime.UtcNow.AddHours(8);
            var token = _jwtTokenService.GenerateAdminToken(userId, admin.FullName, expiresAt);

            _logger.LogInformation("Admin login successful: {UserId}", userId);

            return new AdminLoginResponse
            {
                Success = true,
                AccessToken = token,
                ExpiresAt = expiresAt,
                Message = "Login successful",
                User = new AdminUserDto
                {
                    UserId = admin.UserId,
                    FullName = admin.FullName,
                    Email = admin.Email
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during admin authentication for userId: {UserId}", userId);
            return new AdminLoginResponse
            {
                Success = false,
                Message = "An error occurred during authentication"
            };
        }
    }

    public async Task<MeetingResponse?> CreateMeetingAsync(CreateMeetingRequest request)
    {
        try
        {
            var existingMeeting = await _dbContext.Meetings
                .FirstOrDefaultAsync(m => m.MeetingId == request.MeetingId.ToUpperInvariant());

            if (existingMeeting != null)
            {
                _logger.LogWarning("Attempt to create duplicate meeting: {MeetingId}", request.MeetingId);
                return null;
            }

            // Verify Azure subscription exists
            var azureSubscription = await _dbContext.AzureSubscriptions.FindAsync(request.AzureSubscriptionId);
            if (azureSubscription == null)
            {
                _logger.LogWarning("Azure subscription not found: {AzureSubscriptionId}", request.AzureSubscriptionId);
                return null;
            }

            var meeting = new Meeting
            {
                MeetingId = request.MeetingId.ToUpperInvariant(),
                MeetingName = request.MeetingName,
                AzureSubscriptionId = request.AzureSubscriptionId,
                ValidFrom = request.ValidFrom,
                ValidUntil = request.ValidUntil,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Meetings.Add(meeting);
            await _dbContext.SaveChangesAsync();

            // Reload with Azure subscription
            meeting = await _dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .FirstAsync(m => m.Id == meeting.Id);

            _logger.LogInformation("Meeting created: {MeetingId}", meeting.MeetingId);

            return MapToMeetingResponse(meeting);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating meeting: {MeetingId}", request.MeetingId);
            return null;
        }
    }

    public async Task<MeetingResponse?> UpdateMeetingAsync(string meetingId, UpdateMeetingRequest request)
    {
        try
        {
            var meeting = await _dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());

            if (meeting == null)
            {
                _logger.LogWarning("Meeting not found for update: {MeetingId}", meetingId);
                return null;
            }

            if (request.MeetingName != null) meeting.MeetingName = request.MeetingName;
            if (request.AzureSubscriptionId.HasValue)
            {
                // Verify new Azure subscription exists
                var azureSubscription = await _dbContext.AzureSubscriptions.FindAsync(request.AzureSubscriptionId.Value);
                if (azureSubscription == null)
                {
                    _logger.LogWarning("Azure subscription not found: {AzureSubscriptionId}", request.AzureSubscriptionId);
                    return null;
                }
                meeting.AzureSubscriptionId = request.AzureSubscriptionId.Value;
            }
            if (request.ValidFrom.HasValue) meeting.ValidFrom = request.ValidFrom.Value;
            if (request.ValidUntil.HasValue) meeting.ValidUntil = request.ValidUntil.Value;
            if (request.IsActive.HasValue) meeting.IsActive = request.IsActive.Value;

            meeting.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            // Reload with Azure subscription
            meeting = await _dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .FirstAsync(m => m.Id == meeting.Id);

            _logger.LogInformation("Meeting updated: {MeetingId}", meetingId);

            return MapToMeetingResponse(meeting);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating meeting: {MeetingId}", meetingId);
            return null;
        }
    }

    public async Task<bool> DeleteMeetingAsync(string meetingId)
    {
        try
        {
            var meeting = await _dbContext.Meetings
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());

            if (meeting == null)
            {
                _logger.LogWarning("Meeting not found for deletion: {MeetingId}", meetingId);
                return false;
            }

            _dbContext.Meetings.Remove(meeting);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Meeting deleted: {MeetingId}", meetingId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting meeting: {MeetingId}", meetingId);
            return false;
        }
    }

    public async Task<MeetingResponse?> GetMeetingByIdAsync(string meetingId)
    {
        try
        {
            var meeting = await _dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());

            return meeting != null ? MapToMeetingResponse(meeting) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving meeting: {MeetingId}", meetingId);
            return null;
        }
    }

    public async Task<IEnumerable<MeetingResponse>> GetAllMeetingsAsync()
    {
        try
        {
            var meetings = await _dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            return meetings.Select(MapToMeetingResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all meetings");
            return Enumerable.Empty<MeetingResponse>();
        }
    }

    public async Task<AdminUser?> GetAdminUserByIdAsync(string userId)
    {
        return await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive);
    }

    private MeetingResponse MapToMeetingResponse(Meeting meeting)
    {
        return new MeetingResponse
        {
            Id = meeting.Id,
            MeetingId = meeting.MeetingId,
            MeetingName = meeting.MeetingName,
            AzureSubscriptionId = meeting.AzureSubscriptionId,
            AzureSubscription = meeting.AzureSubscription != null ? new AzureSubscriptionResponse
            {
                Id = meeting.AzureSubscription.Id,
                SubscriptionKey = meeting.AzureSubscription.SubscriptionKey,
                Region = meeting.AzureSubscription.Region,
                Description = meeting.AzureSubscription.Description,
                IsActive = meeting.AzureSubscription.IsActive,
                CreatedAt = meeting.AzureSubscription.CreatedAt,
                UpdatedAt = meeting.AzureSubscription.UpdatedAt
            } : null,
            ValidFrom = meeting.ValidFrom,
            ValidUntil = meeting.ValidUntil,
            CreatedAt = meeting.CreatedAt,
            UpdatedAt = meeting.UpdatedAt,
            IsActive = meeting.IsActive
        };
    }

    private bool VerifyPassword(string password, string passwordHash)
    {
        var hash = HashPassword(password);
        return hash == passwordHash;
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
