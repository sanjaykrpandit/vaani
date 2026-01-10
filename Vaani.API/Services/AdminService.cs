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

    public async Task<SessionMetricsDto?> GetSessionMetricsAsync(string meetingId)
    {
        try
        {
            var meeting = await _dbContext.Meetings
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());

            if (meeting == null)
            {
                _logger.LogWarning("Meeting not found for metrics: {MeetingId}", meetingId);
                return null;
            }

            var sessions = await _dbContext.Sessions
                .Where(s => s.MeetingId == meetingId.ToUpperInvariant())
                .Include(s => s.SessionLogs)
                .ToListAsync();

            if (!sessions.Any())
            {
                return new SessionMetricsDto
                {
                    MeetingId = meeting.MeetingId,
                    MeetingName = meeting.MeetingName
                };
            }

            // Calculate metrics
            var totalSessions = sessions.Count;
            var activeSessions = sessions.Count(s => s.Status == "Active");
            var endedSessions = sessions.Count(s => s.Status == "Ended");
            var expiredSessions = sessions.Count(s => s.Status == "Expired");

            var sessionsWithDuration = sessions.Where(s => s.EndedAt.HasValue);
            var avgDuration = sessionsWithDuration.Any()
                ? sessionsWithDuration.Average(s => (s.EndedAt!.Value - s.StartedAt).TotalMinutes)
                : 0;

            var totalHeartbeats = sessions.Sum(s => s.SessionLogs.Count(l => l.EventType == "Heartbeat"));

            // Device breakdown
            var deviceBreakdown = sessions
                .GroupBy(s => new { s.DeviceId, s.DeviceName })
                .Select(g => new DeviceMetricDto
                {
                    DeviceId = g.Key.DeviceId,
                    DeviceName = g.Key.DeviceName,
                    SessionCount = g.Count(),
                    TotalMinutes = g.Where(s => s.EndedAt.HasValue)
                        .Sum(s => (s.EndedAt!.Value - s.StartedAt).TotalMinutes),
                    LastActivity = g.Max(s => s.LastHeartbeat)
                })
                .OrderByDescending(d => d.LastActivity)
                .ToList();

            // Session timeline (group by date)
            var timeline = sessions
                .GroupBy(s => s.StartedAt.Date)
                .Select(g => new SessionTimelineDto
                {
                    Date = g.Key,
                    SessionCount = g.Count(),
                    TotalHeartbeats = g.Sum(s => s.SessionLogs.Count(l => l.EventType == "Heartbeat"))
                })
                .OrderBy(t => t.Date)
                .ToList();

            // Recent sessions
            var recentSessions = sessions
                .OrderByDescending(s => s.StartedAt)
                .Take(10)
                .Select(s => new SessionDetailDto
                {
                    SessionId = s.Id,
                    DeviceId = s.DeviceId,
                    DeviceName = s.DeviceName,
                    AppVersion = s.AppVersion,
                    StartedAt = s.StartedAt,
                    EndedAt = s.EndedAt,
                    LastHeartbeat = s.LastHeartbeat,
                    Status = s.Status,
                    DurationMinutes = s.EndedAt.HasValue
                        ? (s.EndedAt.Value - s.StartedAt).TotalMinutes
                        : (DateTime.UtcNow - s.StartedAt).TotalMinutes,
                    HeartbeatCount = s.SessionLogs.Count(l => l.EventType == "Heartbeat")
                })
                .ToList();

            return new SessionMetricsDto
            {
                MeetingId = meeting.MeetingId,
                MeetingName = meeting.MeetingName,
                TotalSessions = totalSessions,
                ActiveSessions = activeSessions,
                EndedSessions = endedSessions,
                ExpiredSessions = expiredSessions,
                AverageSessionDurationMinutes = avgDuration,
                TotalHeartbeats = totalHeartbeats,
                FirstSessionStarted = sessions.Min(s => s.StartedAt),
                LastSessionActivity = sessions.Max(s => s.LastHeartbeat),
                DeviceBreakdown = deviceBreakdown,
                SessionTimeline = timeline,
                RecentSessions = recentSessions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session metrics for meeting: {MeetingId}", meetingId);
            return null;
        }
    }

    public async Task<IEnumerable<SessionLogDto>> GetSessionLogsAsync(int sessionId)
    {
        try
        {
            var logs = await _dbContext.SessionLogs
                .Where(l => l.SessionId == sessionId)
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            return logs.Select(l => new SessionLogDto
            {
                Id = l.Id,
                SessionId = l.SessionId,
                EventType = l.EventType,
                Timestamp = l.Timestamp,
                Details = l.Details
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session logs for session: {SessionId}", sessionId);
            return Enumerable.Empty<SessionLogDto>();
        }
    }
}
