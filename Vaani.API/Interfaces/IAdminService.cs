using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Interfaces;

/// <summary>
/// Interface for admin authentication and meeting management
/// </summary>
public interface IAdminService
{
    Task<AdminLoginResponse> AuthenticateAsync(string userId, string password);
    Task<MeetingResponse?> CreateMeetingAsync(CreateMeetingRequest request);
    Task<MeetingResponse?> UpdateMeetingAsync(string meetingId, UpdateMeetingRequest request);
    Task<bool> DeleteMeetingAsync(string meetingId);
    Task<MeetingResponse?> GetMeetingByIdAsync(string meetingId);
    Task<IEnumerable<MeetingResponse>> GetAllMeetingsAsync();
    Task<AdminUser?> GetAdminUserByIdAsync(string userId);
    
    // Analytics methods
    Task<SessionMetricsDto?> GetSessionMetricsAsync(string meetingId);
    Task<IEnumerable<SessionLogDto>> GetSessionLogsAsync(int sessionId);
}
