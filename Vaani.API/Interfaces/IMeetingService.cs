using Vaani.API.Models.DTOs;

namespace Vaani.API.Interfaces;

/// <summary>
/// Interface for meeting-related operations
/// </summary>
public interface IMeetingService
{
    /// <summary>
    /// Validate a meeting ID and create a session
    /// </summary>
    Task<MeetingValidationResponse> ValidateMeetingAsync(MeetingValidationRequest request);
    
    /// <summary>
    /// Get meeting configuration by meeting ID
    /// </summary>
    Task<MeetingConfigurationDto?> GetMeetingConfigurationAsync(string meetingId);
    
    /// <summary>
    /// Check if a meeting is valid and active
    /// </summary>
    Task<bool> IsMeetingValidAsync(string meetingId);
}
