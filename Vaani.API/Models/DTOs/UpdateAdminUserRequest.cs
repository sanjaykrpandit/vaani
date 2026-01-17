namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request model for updating an admin user
/// </summary>
public class UpdateAdminUserRequest
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public bool? IsActive { get; set; }
}
