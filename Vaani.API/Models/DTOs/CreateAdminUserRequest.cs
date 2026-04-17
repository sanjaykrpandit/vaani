namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request model for creating a new admin user
/// </summary>
public class CreateAdminUserRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
