using Microsoft.EntityFrameworkCore;
using Vaani.API.Interfaces;
using Vaani.API.Models.Entities;

namespace Vaani.API.Data;

/// <summary>
/// Repository for database operations
/// </summary>
public class VaaniRepository : IVaaniRepository
{
    private readonly VaaniDbContext _context;
    private readonly ILogger<VaaniRepository> _logger;

    public VaaniRepository(VaaniDbContext context, ILogger<VaaniRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Meeting Operations

    public async Task<Meeting?> GetMeetingByIdAsync(string meetingId)
    {
        try
        {
            return await _context.Meetings
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId && m.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting meeting by ID: {MeetingId}", meetingId);
            throw;
        }
    }

    public async Task<Meeting?> GetMeetingByIdWithSessionsAsync(string meetingId)
    {
        try
        {
            return await _context.Meetings
                .Include(m => m.Sessions)
                .FirstOrDefaultAsync(m => m.MeetingId == meetingId && m.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting meeting with sessions: {MeetingId}", meetingId);
            throw;
        }
    }

    public async Task<IEnumerable<Meeting>> GetActiveMeetingsAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            return await _context.Meetings
                .Where(m => m.IsActive && m.ValidUntil > now)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active meetings");
            throw;
        }
    }

    #endregion

    #region Session Operations

    public async Task<Session?> GetSessionByIdAsync(int sessionId)
    {
        try
        {
            return await _context.Sessions.FindAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session by ID: {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<Session?> GetSessionByMeetingAndDeviceAsync(string meetingId, string deviceId)
    {
        try
        {
            return await _context.Sessions
                .Where(s => s.MeetingId == meetingId && s.DeviceId == deviceId && s.Status == "Active")
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session by meeting and device: {MeetingId}, {DeviceId}", meetingId, deviceId);
            throw;
        }
    }

    public async Task<Session> CreateSessionAsync(Session session)
    {
        try
        {
            _context.Sessions.Add(session);
            await _context.SaveChangesAsync();
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session");
            throw;
        }
    }

    public async Task UpdateSessionAsync(Session session)
    {
        try
        {
            _context.Sessions.Update(session);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session: {SessionId}", session.Id);
            throw;
        }
    }

    public async Task<IEnumerable<Session>> GetActiveSessionsAsync()
    {
        try
        {
            return await _context.Sessions
                .Where(s => s.Status == "Active")
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active sessions");
            throw;
        }
    }

    #endregion

    #region Session Log Operations

    public async Task CreateSessionLogAsync(SessionLog log)
    {
        try
        {
            _context.SessionLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating session log");
            throw;
        }
    }

    public async Task<IEnumerable<SessionLog>> GetSessionLogsAsync(int sessionId)
    {
        try
        {
            return await _context.SessionLogs
                .Where(l => l.SessionId == sessionId)
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session logs for session: {SessionId}", sessionId);
            throw;
        }
    }

    #endregion
}
