namespace Vaani.API.Models.DTOs;

/// <summary>
/// Response returned when a translation session is successfully started
/// </summary>
public class TranslationStartResponse
{
    public bool Success { get; set; }

    /// <summary>Server-assigned translation session ID for all subsequent hub calls</summary>
    public string? TranslationSessionId { get; set; }

    /// <summary>SignalR hub URL for the client to connect to</summary>
    public string? HubUrl { get; set; }

    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
}
