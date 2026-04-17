using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Interfaces;

/// <summary>
/// Interface for session management operations
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Create a new session
    /// </summary>
    Task<(bool success, string? message, int? sessionId)> StartSessionAsync(string meetingId, string deviceId);

       
    Task<(bool success, int? sessionId, string? accessToken, string? message)> CreateSessionAsync(string meetingId, string deviceId, string deviceName, string appVersion, string username);
    /// <summary>
    /// Process heartbeat for a session
    /// </summary>
    Task<HeartbeatResponse> ProcessHeartbeatAsync(string meetingId, string deviceId);
    
    /// <summary>
    /// End a session
    /// </summary>
    Task<EndSessionResponse> EndSessionAsync(EndSessionRequest request);
    
    /// <summary>
    /// Check if a session is valid
    /// </summary>
    Task<bool> IsSessionValidAsync(string meetingId, string deviceId);
    
    /// <summary>
    /// Get remaining minutes for a session
    /// </summary>
    Task<int> GetRemainingMinutesAsync(string meetingId, string deviceId);
}
