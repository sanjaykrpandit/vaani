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
    private readonly IEncryptionService _encryptionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminService> _logger;
    private readonly PasswordHashingService _passwordHashingService;

    public AdminService(
        VaaniDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IEncryptionService encryptionService,
        IConfiguration configuration,
        ILogger<AdminService> logger)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _encryptionService = encryptionService;
        _configuration = configuration;
        _logger = logger;
        _passwordHashingService = new PasswordHashingService();
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
            var normalizedRole = NormalizeRole(admin.Role);
            var token = _jwtTokenService.GenerateAdminToken(userId, admin.FullName, normalizedRole, expiresAt);

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
                    Email = admin.Email,
                    Role = normalizedRole
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

    public async Task<MeetingResponse?> CreateMeetingAsync(CreateMeetingRequest request, string currentUserId)
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
            var azureSubscription = request.AzureSubscriptionId > 0
                ? await _dbContext.AzureSubscriptions.FirstOrDefaultAsync(a => a.Id == request.AzureSubscriptionId && a.IsActive)
                : await _dbContext.AzureSubscriptions.FirstOrDefaultAsync(a => a.IsActive);
            if (azureSubscription == null)
            {
                _logger.LogWarning("Azure subscription not found: {AzureSubscriptionId}", request.AzureSubscriptionId);
                return null;
            }

            var meeting = new Meeting
            {
                MeetingId = request.MeetingId.ToUpperInvariant(),
                MeetingName = request.MeetingName,
                AzureSubscriptionId = azureSubscription.Id,
                ValidFrom = request.ValidFrom,
                ValidUntil = request.ValidUntil,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = currentUserId,
                UpdatedBy = currentUserId,
                MeetingLanguage = request.MeetingLanguage ?? "en-US",
                PublicToken = Guid.NewGuid().ToString("N")
            };

            // Hash password if provided
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                var (hash, salt) = _passwordHashingService.HashPassword(request.Password);
                meeting.PasswordHash = hash;
                meeting.PasswordSalt = salt;
                meeting.RequiresPassword = true;
            }

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

    public async Task<MeetingResponse?> UpdateMeetingAsync(string meetingId, UpdateMeetingRequest request, string currentUserId, string currentUserRole)
    {
        try
        {
            if (!IsAdminRole(currentUserRole))
            {
                _logger.LogWarning("Non-admin user attempted to update meeting: {UserId} {MeetingId}", currentUserId, meetingId);
                return null;
            }

            var meeting = await GetAccessibleMeetingQuery(currentUserId, currentUserRole, includeAzureSubscription: true)
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
            if (request.MeetingLanguage != null) meeting.MeetingLanguage = request.MeetingLanguage;

            // Handle password fields
            if (request.RequiresPassword.HasValue)
            {
                // If requiresPassword set to false, clear password fields
                if (!request.RequiresPassword.Value)
                {
                    meeting.RequiresPassword = false;
                    meeting.PasswordHash = null;
                    meeting.PasswordSalt = null;
                }
                else
                {
                    // If requiresPassword is true and a password is provided, hash and store it
                    if (!string.IsNullOrWhiteSpace(request.Password))
                    {
                        var (hash, salt) = _passwordHashingService.HashPassword(request.Password);
                        meeting.PasswordHash = hash;
                        meeting.PasswordSalt = salt;
                        meeting.RequiresPassword = true;
                    }
                    else
                    {
                        // If requiresPassword true but no password provided, keep existing password (no-op)
                        meeting.RequiresPassword = true;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.Password))
            {
                // If RequiresPassword not specified but password provided, set/replace password and enable requirement
                var (hash, salt) = _passwordHashingService.HashPassword(request.Password);
                meeting.PasswordHash = hash;
                meeting.PasswordSalt = salt;
                meeting.RequiresPassword = true;
            }

            meeting.UpdatedAt = DateTime.UtcNow;
            meeting.UpdatedBy = currentUserId;

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

    public async Task<bool> DeleteMeetingAsync(string meetingId, string currentUserId, string currentUserRole)
    {
        try
        {
            if (!IsAdminRole(currentUserRole))
            {
                _logger.LogWarning("Non-admin user attempted to delete meeting: {UserId} {MeetingId}", currentUserId, meetingId);
                return false;
            }

            var meeting = await GetAccessibleMeetingQuery(currentUserId, currentUserRole)
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

    public async Task<MeetingResponse?> GetMeetingByIdAsync(string meetingId, string currentUserId, string currentUserRole)
    {
        try
        {
            var meeting = await GetAccessibleMeetingQuery(currentUserId, currentUserRole, includeAzureSubscription: true)
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());

            return meeting != null ? MapToMeetingResponse(meeting) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving meeting: {MeetingId}", meetingId);
            return null;
        }
    }

    public async Task<IEnumerable<MeetingResponse>> GetAllMeetingsAsync(string currentUserId, string currentUserRole)
    {
        try
        {
            var meetings = await GetAccessibleMeetingQuery(currentUserId, currentUserRole, includeAzureSubscription: true)
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

    public async Task<AdminUserResponse?> CreateAdminUserAsync(CreateAdminUserRequest request)
    {
        try
        {
            // Check if user already exists
            var existingUser = await _dbContext.AdminUsers
                .FirstOrDefaultAsync(a => a.UserId == request.UserId || a.Email == request.Email);

            if (existingUser != null)
            {
                _logger.LogWarning("Attempt to create duplicate admin user: {UserId} or {Email}", request.UserId, request.Email);
                return null;
            }

            var adminUser = new AdminUser
            {
                UserId = request.UserId,
                PasswordHash = HashPassword(request.Password),
                FullName = request.FullName,
                Email = request.Email,
                Role = NormalizeRole(request.Role),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.AdminUsers.Add(adminUser);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Admin user created: {UserId}", adminUser.UserId);

            return MapToAdminUserResponse(adminUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating admin user: {UserId}", request.UserId);
            return null;
        }
    }

    public async Task<AdminUserResponse?> UpdateAdminUserAsync(string userId, UpdateAdminUserRequest request)
    {
        try
        {
            var adminUser = await _dbContext.AdminUsers
                .FirstOrDefaultAsync(a => a.UserId == userId);

            if (adminUser == null)
            {
                _logger.LogWarning("Admin user not found: {UserId}", userId);
                return null;
            }

            // Check if email is being changed and if it already exists
            if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != adminUser.Email)
            {
                var emailExists = await _dbContext.AdminUsers
                    .AnyAsync(a => a.Email == request.Email && a.UserId != userId);
                
                if (emailExists)
                {
                    _logger.LogWarning("Email already exists: {Email}", request.Email);
                    return null;
                }
                
                adminUser.Email = request.Email;
            }

            if (!string.IsNullOrWhiteSpace(request.FullName))
            {
                adminUser.FullName = request.FullName;
            }

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                adminUser.PasswordHash = HashPassword(request.Password);
            }

            if (!string.IsNullOrWhiteSpace(request.Role))
            {
                adminUser.Role = NormalizeRole(request.Role);
            }

            if (request.IsActive.HasValue)
            {
                adminUser.IsActive = request.IsActive.Value;
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Admin user updated: {UserId}", userId);

            return MapToAdminUserResponse(adminUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating admin user: {UserId}", userId);
            return null;
        }
    }

    public async Task<AdminUserResponse?> GetAdminUserResponseByIdAsync(string userId)
    {
        try
        {
            var adminUser = await _dbContext.AdminUsers
                .FirstOrDefaultAsync(a => a.UserId == userId);

            return adminUser != null ? MapToAdminUserResponse(adminUser) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving admin user: {UserId}", userId);
            return null;
        }
    }

    public async Task<IEnumerable<AdminUserResponse>> GetAllAdminUsersAsync()
    {
        try
        {
            var adminUsers = await _dbContext.AdminUsers
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return adminUsers.Select(MapToAdminUserResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all admin users");
            return Enumerable.Empty<AdminUserResponse>();
        }
    }

    private AdminUserResponse MapToAdminUserResponse(AdminUser adminUser)
    {
        return new AdminUserResponse
        {
            Id = adminUser.Id,
            UserId = adminUser.UserId,
            FullName = adminUser.FullName,
            Email = adminUser.Email,
            Role = NormalizeRole(adminUser.Role),
            IsActive = adminUser.IsActive,
            CreatedAt = adminUser.CreatedAt,
            LastLoginAt = adminUser.LastLoginAt
        };
    }

    private MeetingResponse MapToMeetingResponse(Meeting meeting)
    {
        return new MeetingResponse
        {
            Id = meeting.Id,
            MeetingId = meeting.MeetingId,
            MeetingName = meeting.MeetingName,
            AzureSubscriptionId = meeting.AzureSubscriptionId,
            //AzureSubscription = meeting.AzureSubscription != null ? new AzureSubscriptionResponse
            //{
            //    Id = meeting.AzureSubscription.Id,
            //    SubscriptionKey = meeting.AzureSubscription.SubscriptionKey,
            //    Region = meeting.AzureSubscription.Region,
            //    Description = meeting.AzureSubscription.Description,
            //    IsActive = meeting.AzureSubscription.IsActive,
            //    CreatedAt = meeting.AzureSubscription.CreatedAt,
            //    UpdatedAt = meeting.AzureSubscription.UpdatedAt
            //} : null,
            ValidFrom = meeting.ValidFrom,
            ValidUntil = meeting.ValidUntil,
            CreatedAt = meeting.CreatedAt,
            UpdatedAt = meeting.UpdatedAt,
            IsActive = meeting.IsActive,
            MeetingLanguage = meeting.MeetingLanguage,
            RequiresPassword = meeting.RequiresPassword
        };
    }

    private static string NormalizeRole(string? role)
    {
        return string.Equals(role, "subadmin", StringComparison.OrdinalIgnoreCase) ? "subadmin" : "admin";
    }

    private static bool IsAdminRole(string? role)
    {
        return string.Equals(NormalizeRole(role), "admin", StringComparison.OrdinalIgnoreCase);
    }

    private IQueryable<Meeting> GetAccessibleMeetingQuery(string currentUserId, string currentUserRole, bool includeAzureSubscription = false)
    {
        IQueryable<Meeting> query = _dbContext.Meetings;

        if (includeAzureSubscription)
        {
            query = query.Include(m => m.AzureSubscription);
        }

        if (IsAdminRole(currentUserRole))
        {
            return query;
        }

        return query.Where(m => m.CreatedBy == currentUserId);
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

    public async Task<SessionMetricsDto?> GetSessionMetricsAsync(string meetingId, string currentUserId, string currentUserRole)
    {
        try
        {
            if (!IsAdminRole(currentUserRole))
            {
                _logger.LogWarning("Non-admin user attempted to view metrics: {UserId} {MeetingId}", currentUserId, meetingId);
                return null;
            }

            var meeting = await GetAccessibleMeetingQuery(currentUserId, currentUserRole)
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());

            if (meeting == null)
            {
                _logger.LogWarning("Meeting not found for metrics: {MeetingId}", meetingId);
                return null;
            }

            var sessions = await _dbContext.Sessions
                .Where(s => s.MeetingId == meetingId.ToUpperInvariant())
                .ToListAsync();

            if (!sessions.Any())
            {
                return new SessionMetricsDto
                {
                    MeetingId = meeting.MeetingId,
                    MeetingName = meeting.MeetingName
                };
            }

            // Get all session IDs
            var sessionIds = sessions.Select(s => s.Id).ToList();

            // Pull SessionLogs directly from database for all sessions
            var sessionLogs = await _dbContext.SessionLogs
                .Where(l => sessionIds.Contains(l.SessionId) &&
                           (l.EventType == "SessionStarted" || l.EventType == "SessionEnded"))
                .OrderBy(l => l.SessionId)
                .ThenBy(l => l.Timestamp)
                .ToListAsync();

            // Group logs by session ID
            var logsBySession = sessionLogs.GroupBy(l => l.SessionId).ToDictionary(g => g.Key, g => g.ToList());

            // Calculate actual duration from SessionLogs for each session
            double CalculateActualDuration(int sessionId)
            {
                if (!logsBySession.TryGetValue(sessionId, out var logs))
                    return 0;

                var startEvents = logs.Where(l => l.EventType == "SessionStarted").OrderBy(l => l.Timestamp).ToList();
                var endEvents = logs.Where(l => l.EventType == "SessionEnded").OrderBy(l => l.Timestamp).ToList();

                double totalMinutes = 0;

                for (int i = 0; i < startEvents.Count; i++)
                {
                    // Find the next end event after this start event
                    var endEvent = endEvents.FirstOrDefault(e => e.Timestamp > startEvents[i].Timestamp);

                    if (endEvent != null)
                    {
                        totalMinutes += (endEvent.Timestamp - startEvents[i].Timestamp).TotalMinutes;
                    }
                }

                return totalMinutes;
            }

            int GetHeartbeatCount(int sessionId)
            {
                if (!logsBySession.TryGetValue(sessionId, out var logs))
                    return 0;

                return logs.Count(l => l.EventType == "Heartbeat");
            }

            // Calculate metrics
            var totalSessions = sessions.Count;
            var activeSessions = sessions.Count(s => s.Status == "Active");
            var endedSessions = sessions.Count(s => s.Status == "Ended");
            var expiredSessions = sessions.Count(s => s.Status == "Expired");

            // Calculate average duration using actual session logs
            var sessionDurations = sessions
                .Select(s => CalculateActualDuration(s.Id))
                .Where(d => d > 0)
                .ToList();

            var avgDuration = sessionDurations.Any() ? sessionDurations.Average() : 0;

            var totalHeartbeats = sessionLogs.Count(l => l.EventType == "Heartbeat");

            // Device breakdown with actual durations
            var deviceBreakdown = sessions
                .GroupBy(s => new { s.DeviceId, s.DeviceName })
                .Select(g => new DeviceMetricDto
                {
                    DeviceId = g.Key.DeviceId,
                    DeviceName = g.Key.DeviceName,
                    SessionCount = g.Count(),
                    TotalMinutes = g.Sum(s => CalculateActualDuration(s.Id)),
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
                    TotalHeartbeats = g.Sum(s => GetHeartbeatCount(s.Id))
                })
                .OrderBy(t => t.Date)
                .ToList();

            // Recent sessions with actual durations
            var recentSessions = sessions
                .OrderByDescending(s => s.StartedAt)
                .Take(10)
                .Select(s => new SessionDetailDto
                {
                    SessionId = s.Id,
                    DeviceId = s.DeviceName,
                    DeviceName = s.DeviceName,
                    UserName = s.UserName,
                    AppVersion = s.AppVersion,
                    StartedAt = s.StartedAt,
                    EndedAt = s.EndedAt,
                    LastHeartbeat = s.LastHeartbeat,
                    Status = s.Status,
                    DurationMinutes = CalculateActualDuration(s.Id),
                    HeartbeatCount = GetHeartbeatCount(s.Id)
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
    public async Task<SessionMetricsDto?> GetSessionMetricsAsyncXXX(string meetingId)
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

            //pull actual timedurationfro  sessionlogs for each sessions by sessionid where EventType EndSession, StartSession

     

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

    public async Task<IEnumerable<SessionLogDto>> GetSessionLogsAsync(int sessionId, string currentUserId, string currentUserRole)
    {
        try
        {
            if (!IsAdminRole(currentUserRole))
            {
                _logger.LogWarning("Non-admin user attempted to view session logs: {UserId} {SessionId}", currentUserId, sessionId);
                return Enumerable.Empty<SessionLogDto>();
            }

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

    public async Task<GenerateMeetingTokenResponse?> GenerateMeetingTokenAsync(string meetingId, string currentUserId, string currentUserRole)
    {
        try
        {
            if (!IsAdminRole(currentUserRole))
            {
                _logger.LogWarning("Non-admin user attempted to generate token: {UserId} {MeetingId}", currentUserId, meetingId);
                return null;
            }

            var meeting = await GetAccessibleMeetingQuery(currentUserId, currentUserRole)
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant() && m.IsActive);

            if (meeting == null)
            {
                _logger.LogWarning("Meeting not found or inactive: {MeetingId}", meetingId);
                return null;
            }

            // Generate encrypted token combining meetingId and publicToken
            var encryptedToken = _encryptionService.EncryptMeetingToken(meeting.MeetingId, meeting.PublicToken);

            _logger.LogInformation("Generated token for meeting: {MeetingId}", meetingId);

            return new GenerateMeetingTokenResponse
            {
                Token = encryptedToken
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating token for meeting: {MeetingId}", meetingId);
            return null;
        }
    }

    public async Task<ValidateMeetingTokenResponse> ValidateMeetingTokenAsync(ValidateMeetingTokenRequest request)
    {
        try
        {
            // Decrypt the token
            var decryptedData = _encryptionService.DecryptMeetingToken(request.Token);

            if (decryptedData == null)
            {
                _logger.LogWarning("Invalid token format");
                return new ValidateMeetingTokenResponse
                {
                    IsValid = false
                };
            }

            var (tokenMeetingId, publicToken) = decryptedData.Value;

            // Validate that the decrypted meetingId matches the request
            ////if (!tokenMeetingId.Equals(request.MeetingId, StringComparison.OrdinalIgnoreCase))
            ////{
            ////    _logger.LogWarning("Meeting ID mismatch. Token: {TokenMeetingId}, Request: {RequestMeetingId}", 
            ////        tokenMeetingId, request.MeetingId);
            ////    return new ValidateMeetingTokenResponse
            ////    {
            ////        IsValid = false
            ////    };
            ////}

            // Get meeting from database
            var meeting = await _dbContext.Meetings
                .FirstOrDefaultAsync(m => m.MeetingId == tokenMeetingId.ToUpperInvariant());

            if (meeting == null)
            {
                _logger.LogWarning("Meeting not found: {MeetingId}", tokenMeetingId);
                return new ValidateMeetingTokenResponse
                {
                    IsValid = false
                };
            }

            // Validate the publicToken matches
            if (meeting.PublicToken != publicToken)
            {
                _logger.LogWarning("Public token mismatch for meeting: {MeetingId}", request.MeetingId);
                return new ValidateMeetingTokenResponse
                {
                    IsValid = false
                };
            }

            // Validate meeting is active
            if (!meeting.IsActive)
            {
                _logger.LogWarning("Meeting is inactive: {MeetingId}", request.MeetingId);
                return new ValidateMeetingTokenResponse
                {
                    MeetingId = meeting.MeetingId,
                    MeetingName = meeting.MeetingName,
                    ValidUntil = meeting.ValidUntil,
                    IsValid = false
                };
            }

            // Validate meeting time window
            var now = DateTime.UtcNow;
            if (now < meeting.ValidFrom.AddMinutes(-15) || now > meeting.ValidUntil)
            {
                _logger.LogWarning("Meeting outside valid time window: {MeetingId}", request.MeetingId);
                return new ValidateMeetingTokenResponse
                {
                    MeetingId = meeting.MeetingId,
                    MeetingName = meeting.MeetingName,
                    ValidUntil = meeting.ValidUntil,
                    IsValid = false
                };
            }

            // Get download link from configuration
            var isSubtitleApp = string.Equals(request.AppType, "subtitle", StringComparison.OrdinalIgnoreCase);
            var downloadLink = isSubtitleApp
                ? _configuration["AppDownload:SubtitleLink"] ?? string.Empty
                : _configuration["AppDownload:Link"] ?? string.Empty;

            _logger.LogInformation("Token validated successfully for meeting: {MeetingId}", tokenMeetingId);

            return new ValidateMeetingTokenResponse
            {
                MeetingId = meeting.MeetingId,
                MeetingName = meeting.MeetingName,
                ValidUntil = meeting.ValidUntil,
                IsValid = true,
                DownloadLink = downloadLink
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating meeting token");
            return new ValidateMeetingTokenResponse
            {
                IsValid = false
            };
        }
    }
}
