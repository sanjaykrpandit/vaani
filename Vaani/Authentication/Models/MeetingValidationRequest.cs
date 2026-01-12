using System;

namespace Vaani.Authentication.Models;

/// <summary>
/// Request model for validating a meeting ID with the backend API
/// </summary>
public class MeetingValidationRequest
{   
    public string MeetingId { get; set; } = string.Empty;  
    public string DeviceId { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string DeviceName { get; internal set; } = string.Empty;
    public string UserName { get; internal set; } = string.Empty;
}
