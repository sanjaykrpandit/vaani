namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request model for validate meeting token
/// </summary>
public class ValidateMeetingTokenRequest
{
    public string MeetingId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string AppType { get; set; } = "audio";
}
