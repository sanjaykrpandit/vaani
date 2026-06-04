namespace Vaani.Authentication.Models;

public class DirectSpeechTokenResponse
{
    public bool Success { get; set; }
    public string SpeechToken { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string? Message { get; set; }
}
