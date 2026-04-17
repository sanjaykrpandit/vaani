using Vaani.API.Models.Entities;

namespace Vaani.API.Interfaces;

/// <summary>
/// Interface for database operations
/// </summary>
public interface IVaaniRepository
{
    // Meeting operations
    Task<Meeting?> GetMeetingByIdAsync(string meetingId);
    Task<Meeting?> GetMeetingByIdWithSessionsAsync(string meetingId);
    Task<IEnumerable<Meeting>> GetActiveMeetingsAsync();
    
    // Session operations
    Task<Session?> GetSessionByIdAsync(int sessionId);
    Task<Session?> GetSessionByMeetingAndDeviceAsync(string meetingId, string deviceId);
    Task<Session> CreateSessionAsync(Session session);
    Task UpdateSessionAsync(Session session);
    Task<IEnumerable<Session>> GetActiveSessionsAsync();
    
    // Session log operations
    Task CreateSessionLogAsync(SessionLog log);
    Task<IEnumerable<SessionLog>> GetSessionLogsAsync(int sessionId);


    // Language operations
    Task<IEnumerable<Language>> GetAllLanguagesAsync();
    Task<Language> CreateLanguageAsync(Language language);
    Task UpdateLanguageAsync(Language language);
    Task DeleteLanguageAsync(int languageCode);   
}
