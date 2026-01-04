using System;

namespace Vaani.Authentication.Models;

/// <summary>
/// Request model for validating a meeting ID with the backend API
/// </summary>
public class MeetingValidationRequest
{
    /// <summary>
    /// Meeting ID provided by the organizer (e.g., VM-2025-1220-A7B3)
    /// </summary>
    public string MeetingId { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for the device/machine
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    public string AppVersion { get; set; } = string.Empty;
    public string DeviceName { get; internal set; } = string.Empty;
}
