using System;

namespace Vaani.Authentication.Models;

/// <summary>
/// Response model from the backend API after meeting validation
/// </summary>
public class MeetingValidationResponse
{
    /// <summary>
    /// Indicates if the meeting ID is valid and active
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Human-readable name of the meeting
    /// </summary>
    public string MeetingName { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded AES-256 encrypted configuration blob
    /// Format: VAANI_ENC_v1_<encrypted_data>
    /// </summary>
    public string EncryptedConfig { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the meeting session expires
    /// </summary>
    public DateTime ValidUntil { get; set; }

    /// <summary>
    /// Remaining minutes until expiration
    /// </summary>
    public int RemainingMinutes { get; set; }

    /// <summary>
    /// Session token for subsequent API calls (heartbeat, usage logging)
    /// </summary>
    public string SessionToken { get; set; } = string.Empty;

    /// <summary>
    /// Feature flags for this meeting
    /// </summary>
    public MeetingFeatures Features { get; set; } = new();

    /// <summary>
    /// Error code if validation failed
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Human-readable error message
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Feature flags for meeting configuration
/// </summary>
public class MeetingFeatures
{
    /// <summary>
    /// Allow reconnection if disconnected during meeting
    /// </summary>
    public bool AllowReconnect { get; set; } = true;

    /// <summary>
    /// Interval in seconds for sending heartbeat to keep session alive
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Enable local caching of decrypted configuration
    /// </summary>
    public bool EnableLocalCache { get; set; } = false;
}
