using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Interfaces;

/// <summary>
/// Interface for admin authentication and meeting management
/// </summary>
public interface IAdminService
{
    Task<AdminLoginResponse> AuthenticateAsync(string userId, string password);
    Task<MeetingResponse?> CreateMeetingAsync(CreateMeetingRequest request, string currentUserId);
    Task<MeetingResponse?> UpdateMeetingAsync(string meetingId, UpdateMeetingRequest request, string currentUserId, string currentUserRole);
    Task<bool> DeleteMeetingAsync(string meetingId, string currentUserId, string currentUserRole);
    Task<MeetingResponse?> GetMeetingByIdAsync(string meetingId, string currentUserId, string currentUserRole);
    Task<IEnumerable<MeetingResponse>> GetAllMeetingsAsync(string currentUserId, string currentUserRole);
    Task<AdminUser?> GetAdminUserByIdAsync(string userId);
    
    // Admin User management methods
    Task<AdminUserResponse?> CreateAdminUserAsync(CreateAdminUserRequest request);
    Task<AdminUserResponse?> UpdateAdminUserAsync(string userId, UpdateAdminUserRequest request);
    Task<AdminUserResponse?> GetAdminUserResponseByIdAsync(string userId);
    Task<IEnumerable<AdminUserResponse>> GetAllAdminUsersAsync();
    
    // Meeting token methods
    Task<GenerateMeetingTokenResponse?> GenerateMeetingTokenAsync(string meetingId, string currentUserId, string currentUserRole);
    Task<ValidateMeetingTokenResponse> ValidateMeetingTokenAsync(ValidateMeetingTokenRequest request);
    
    // Analytics methods
    Task<SessionMetricsDto?> GetSessionMetricsAsync(string meetingId, string currentUserId, string currentUserRole);
    Task<IEnumerable<SessionLogDto>> GetSessionLogsAsync(int sessionId, string currentUserId, string currentUserRole);
}
