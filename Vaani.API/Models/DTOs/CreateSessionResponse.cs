namespace Vaani.API.Models.DTOs;

/// <summary>
/// Response model for session creation
/// </summary>
public class CreateSessionResponse
{
    public bool Success { get; set; }
    public int? SessionId { get; set; }
    public string? AccessToken { get; set; }
    public string? Message { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
