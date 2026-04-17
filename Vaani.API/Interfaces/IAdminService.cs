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
    
    // Admin User management methods
    Task<AdminUserResponse?> CreateAdminUserAsync(CreateAdminUserRequest request);
    Task<AdminUserResponse?> UpdateAdminUserAsync(string userId, UpdateAdminUserRequest request);
    Task<AdminUserResponse?> GetAdminUserResponseByIdAsync(string userId);
    Task<IEnumerable<AdminUserResponse>> GetAllAdminUsersAsync();
    
    // Meeting token methods
    Task<GenerateMeetingTokenResponse?> GenerateMeetingTokenAsync(string meetingId);
    Task<ValidateMeetingTokenResponse> ValidateMeetingTokenAsync(ValidateMeetingTokenRequest request);
    
    // Analytics methods
    Task<SessionMetricsDto?> GetSessionMetricsAsync(string meetingId);
    Task<IEnumerable<SessionLogDto>> GetSessionLogsAsync(int sessionId);
}
