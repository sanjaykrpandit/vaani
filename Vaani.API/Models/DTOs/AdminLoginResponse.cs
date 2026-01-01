namespace Vaani.API.Models.DTOs;

/// <summary>
/// Response model for admin login
/// </summary>
public class AdminLoginResponse
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? Message { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public AdminUserDto? User { get; set; }
}

/// <summary>
/// Admin user information DTO
/// </summary>
public class AdminUserDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
