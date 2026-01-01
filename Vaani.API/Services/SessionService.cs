using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;

namespace Vaani.API.Services;

/// <summary>
/// Service for session management operations
/// </summary>
public class SessionService : ISessionService
{
    private readonly VaaniDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        VaaniDbContext dbContext,
        IJwtTokenService jwtTokenService,
        ILogger<SessionService> logger)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    public async Task<(bool success, int? sessionId, string? accessToken, string? message)> CreateSessionAsync(string meetingId, string deviceId, string deviceName, string appVersion)
    {
        try
        {
            // Get meeting with Azure subscription
            var meeting = await _dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());

            if (meeting == null)
            {
                return (false, null, null, "Meeting not found");
            }

            if (!meeting.IsActive)
            {
                return (false, null, null, "Meeting is not active");
            }

            // Check time window
            var now = DateTime.UtcNow;
            if (now < meeting.ValidFrom)
            {
                return (false, null, null, "Meeting has not started yet");
            }

            if (now > meeting.ValidUntil)
            {
                return (false, null, null, "Meeting has expired");
            }

            // Check if session already exists for this device
            var existingSession = await _dbContext.Sessions
                .Where(s => s.MeetingId == meetingId && s.DeviceId == deviceId && s.Status == "Active")
                .FirstOrDefaultAsync();

            // Generate JWT token for API authentication
            var accessToken = _jwtTokenService.GenerateToken(meetingId, deviceId, meeting.ValidUntil);

            if (existingSession != null)
            {
                _logger.LogInformation("Returning existing session for device: {DeviceId} in meeting: {MeetingId}", deviceId, meetingId);
                return (true, existingSession.Id, accessToken, "Existing session found");
            }

            // Create session entity
            var session = new Session
            {
                MeetingId = meetingId,
                DeviceId = deviceId,
                DeviceName = deviceName,
                AppVersion = appVersion,
                StartedAt = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow,
                Status = "Active"
            };

            _dbContext.Sessions.Add(session);
            await _dbContext.SaveChangesAsync();

            // Log session creation
            var sessionLog = new SessionLog
            {
                SessionId = session.Id,
                EventType = "SessionCreated",
                Timestamp = DateTime.UtcNow,
                Details = $"Device: {deviceName}, App: {appVersion}"
            };
            _dbContext.SessionLogs.Add(sessionLog);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Session created for meeting: {MeetingId}, Device: {DeviceId}, SessionId: {SessionId}", 
                meetingId, deviceId, session.Id);

            return (true, session.Id, accessToken, "Session created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session for meeting: {MeetingId}", meetingId);
            return (false, null, null, "An error occurred while creating session");
        }
    }

    public async Task<HeartbeatResponse> ProcessHeartbeatAsync(string meetingId, string deviceId)
    {
        try
        {
            // Get session from database
            var session = await _dbContext.Sessions
                .Where(s => s.MeetingId == meetingId && s.DeviceId == deviceId && s.Status == "Active")
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (session == null)
            {
                return new HeartbeatResponse
                {
                    Success = false,
                    ErrorCode = "SESSION_NOT_FOUND",
                    Message = "Session not found"
                };
            }

            // Check if meeting is expired
            var meeting = await _dbContext.Meetings.FindAsync(session.MeetingId);
            if (meeting == null || meeting.ValidUntil < DateTime.UtcNow)
            {
                session.Status = "Expired";
                await _dbContext.SaveChangesAsync();

                return new HeartbeatResponse
                {
                    Success = false,
                    ErrorCode = "SESSION_EXPIRED",
                    Message = "Session has expired"
                };
            }

            // Update last heartbeat
            session.LastHeartbeat = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Log heartbeat
            var sessionLog = new SessionLog
            {
                SessionId = session.Id,
                EventType = "Heartbeat",
                Timestamp = DateTime.UtcNow
            };
            _dbContext.SessionLogs.Add(sessionLog);
            await _dbContext.SaveChangesAsync();

            // Calculate remaining minutes
            var remainingMinutes = (int)(meeting.ValidUntil - DateTime.UtcNow).TotalMinutes;

            return new HeartbeatResponse
            {
                Success = true,
                RemainingMinutes = remainingMinutes,
                Message = "Heartbeat successful"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat for meeting: {MeetingId}, device: {DeviceId}", meetingId, deviceId);
            return new HeartbeatResponse
            {
                Success = false,
                ErrorCode = "INTERNAL_ERROR",
                Message = "An error occurred while processing heartbeat"
            };
        }
    }

    public async Task<EndSessionResponse> EndSessionAsync(int sessionId)
    {
        try
        {
            var session = await _dbContext.Sessions.FindAsync(sessionId);

            if (session == null)
            {
                return new EndSessionResponse
                {
                    Success = false,
                    ErrorCode = "SESSION_NOT_FOUND",
                    Message = "Session not found"
                };
            }

            if (session.Status != "Active")
            {
                return new EndSessionResponse
                {
                    Success = false,
                    ErrorCode = "SESSION_NOT_ACTIVE",
                    Message = $"Session is already {session.Status}"
                };
            }

            // Update session status
            session.Status = "Ended";
            session.EndedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            // Calculate duration
            var duration = session.EndedAt.Value - session.StartedAt;
            var durationMinutes = (int)duration.TotalMinutes;

            // Log session end
            var sessionLog = new SessionLog
            {
                SessionId = session.Id,
                EventType = "SessionEnded",
                Timestamp = DateTime.UtcNow,
                Details = $"Duration: {durationMinutes} minutes"
            };
            _dbContext.SessionLogs.Add(sessionLog);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Session ended - SessionId: {SessionId}, Meeting: {MeetingId}, Device: {DeviceId}, Duration: {Duration} minutes", 
                sessionId, session.MeetingId, session.DeviceId, durationMinutes);

            return new EndSessionResponse
            {
                Success = true,
                Message = "Session ended successfully",
                DurationMinutes = durationMinutes
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending session: {SessionId}", sessionId);
            return new EndSessionResponse
            {
                Success = false,
                ErrorCode = "INTERNAL_ERROR",
                Message = "An error occurred while ending session"
            };
        }
    }

    public async Task<bool> IsSessionValidAsync(string meetingId, string deviceId)
    {
        var session = await _dbContext.Sessions
            .Where(s => s.MeetingId == meetingId && s.DeviceId == deviceId && s.Status == "Active")
            .AnyAsync();

        return session;
    }

    public async Task<int> GetRemainingMinutesAsync(string meetingId, string deviceId)
    {
        var meeting = await _dbContext.Meetings
            .FirstOrDefaultAsync(m => m.MeetingId == meetingId);
            
        if (meeting == null) return 0;

        var remaining = meeting.ValidUntil - DateTime.UtcNow;
        return remaining.TotalMinutes > 0 ? (int)remaining.TotalMinutes : 0;
    }
}
