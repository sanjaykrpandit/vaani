namespace Vaani.API.Models.DTOs;

/// <summary>
/// Response model for validate meeting token
/// </summary>
public class ValidateMeetingTokenResponse
{
    public string MeetingId { get; set; } = string.Empty;
    public string MeetingName { get; set; } = string.Empty;
    public DateTime ValidUntil { get; set; }
    public bool IsValid { get; set; }
    public string DownloadLink { get; set; } = string.Empty;
}
