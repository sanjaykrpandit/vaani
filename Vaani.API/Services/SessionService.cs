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
       
    public async Task<(bool success, int? sessionId, string? accessToken, string? message)> CreateSessionAsync(string meetingId, string deviceId, string deviceName, string appVersion, string username)
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
                .Where(s => s.MeetingId == meetingId && s.DeviceId == deviceId && (s.Status == "Active" || s.Status == "Initial"))
                .FirstOrDefaultAsync();

            // Generate JWT token for API authentication
            var accessToken = _jwtTokenService.GenerateToken(meetingId, deviceId, meeting.ValidUntil);

            if (existingSession != null) {
                _logger.LogInformation("Returning existing session for device: {DeviceId} in meeting: {MeetingId}", deviceId, meetingId);
                return (true, existingSession.Id, accessToken, "Existing session found");
            }

            //if (existingSession != null)
            //{
            //    // Log session creation
            //    var _sessionLog = new SessionLog
            //    {
            //        SessionId = existingSession.Id,
            //        EventType = "SessionCreated",
            //        Timestamp = DateTime.UtcNow,
            //        Details = $"Device: {deviceName}, User: {username}"
            //    };
            //    _dbContext.SessionLogs.Add(_sessionLog);
            //    await _dbContext.SaveChangesAsync();

            //    _logger.LogInformation("Returning existing session for device: {DeviceId} in meeting: {MeetingId}", deviceId, meetingId);
            //    return (true, existingSession.Id, accessToken, "Existing session found");
            //}

            // Create session entity
            var session = new Session
            {
                MeetingId = meetingId,
                DeviceId = deviceId,
                DeviceName = deviceName,
                AppVersion = appVersion,
                StartedAt = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow,
                Status = "Initial",
                CreatedAt = DateTime.UtcNow,
                SessionTrascript = string.Empty,
                SessionLog = string.Empty,
                UserName = username
            };

            _dbContext.Sessions.Add(session);
            await _dbContext.SaveChangesAsync();

            // Log session creation
            var sessionLog = new SessionLog
            {
                SessionId = session.Id,
                EventType = "SessionCreated",
                Timestamp = DateTime.UtcNow,
                Details = $"Device: {deviceName}"
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

    public async Task<(bool success, string? message, int? sessionId)> StartSessionAsync(string meetingId, string deviceId)
    {
        try
        {
            // Get meeting with Azure subscription
            var meeting = await _dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());

            if (meeting == null)
            {
                return (false, "Meeting not found",0);
            }

            if (!meeting.IsActive)
            {
                return (false, "Meeting is not active", 0);
            }

            // Check time window
            var now = DateTime.UtcNow;
            if (now < meeting.ValidFrom)
            {
                return (false, "Meeting has not started yet", 0);
            }

            if (now > meeting.ValidUntil)
            {
                return (false, "Meeting has expired", 0);
            }

            // Check if session already exists for this device
            var session = await _dbContext.Sessions
                .Where(s => s.MeetingId == meetingId && s.DeviceId == deviceId)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync();


            if (session == null) {
                return (false, "No active session found to start", 0);
            }

            session.Status = "Active";
            session.StartedAt = DateTime.UtcNow;
            _dbContext.Sessions.Update(session);
            await _dbContext.SaveChangesAsync();

            // Log session creation
            var sessionLog = new SessionLog
            {
                SessionId = session.Id,
                EventType = "SessionStarted",
                Timestamp = DateTime.UtcNow,
                Details = ""
            };
            _dbContext.SessionLogs.Add(sessionLog);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Session created for meeting: {MeetingId}, Device: {DeviceId}, SessionId: {SessionId}",
                meetingId, deviceId, session.Id);

            return (true, "Session started successfully", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session for meeting: {MeetingId}", meetingId);
            return (false, "An error occurred while creating session", 0);
        }
    }

    public async Task<HeartbeatResponse> ProcessHeartbeatAsync(string meetingId, string deviceId)
    {
        try
        {
            // Get session from database
            var session = await _dbContext.Sessions
                .Where(s => s.MeetingId == meetingId && s.DeviceId == deviceId && (s.Status == "Active" || s.Status == "Initial"))
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

    public async Task<EndSessionResponse> EndSessionAsync(EndSessionRequest request)
    {
        try
        {

            var session = await _dbContext.Sessions
               .Where(s => s.MeetingId == request.meetingId && s.DeviceId == request.deviceId)
               .OrderByDescending(s => s.Id)
               .FirstOrDefaultAsync();


            if (session == null)
            {
                return new EndSessionResponse
                {
                    Success = false,
                    ErrorCode = "SESSION_NOT_FOUND",
                    Message = "Session not found"
                };
            }
          

            // Update session status
            session.Status = "Ended";
            session.EndedAt = DateTime.UtcNow;
            session.SessionLog = session.SessionLog + " \n " + request.SessionLog;
            session.SessionTrascript = session.SessionTrascript + " \n " + request.SessionTrascript;
            _dbContext.Sessions.Update(session);
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
                session.Id, session.MeetingId, session.DeviceId, durationMinutes);

            return new EndSessionResponse
            {
                Success = true,
                Message = "Session ended successfully",
                DurationMinutes = durationMinutes
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending session: {DeviceId}", request.deviceId);
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
            .Where(s => s.MeetingId == meetingId && s.DeviceId == deviceId && (s.Status == "Active" || s.Status == "Initial"))
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
