namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request model for admin login
/// </summary>
public class AdminLoginRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
