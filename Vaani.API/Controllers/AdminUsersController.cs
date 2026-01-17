using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Controllers;

/// <summary>
/// Controller for admin user management (CRUD operations)
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "admin")]
public class AdminUsersController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        IAdminService adminService,
        IJwtTokenService jwtTokenService,
        ILogger<AdminUsersController> logger)
    {
        _adminService = adminService;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Get all admin users
    /// </summary>
    /// <returns>List of all admin users</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AdminUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<AdminUserResponse>>> GetAllAdminUsers()
    {
        try
        {
            if (!ValidateAdminToken())
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            var adminUsers = await _adminService.GetAllAdminUsersAsync();
            return Ok(adminUsers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all admin users");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Get a specific admin user by user ID
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>Admin user details</returns>
    [HttpGet("{userId}")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AdminUserResponse>> GetAdminUser(string userId)
    {
        try
        {
            if (!ValidateAdminToken())
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            var adminUser = await _adminService.GetAdminUserResponseByIdAsync(userId);

            if (adminUser == null)
            {
                return NotFound(new { message = "Admin user not found" });
            }

            return Ok(adminUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving admin user: {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Create a new admin user
    /// </summary>
    /// <param name="request">Admin user creation request</param>
    /// <returns>Created admin user</returns>
    [HttpPost]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserResponse>> CreateAdminUser([FromBody] CreateAdminUserRequest request)
    {
        try
        {
            if (!ValidateAdminToken())
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            // Validation
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return BadRequest(new { message = "User ID is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Password is required" });
            }

            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                return BadRequest(new { message = "Full name is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email is required" });
            }

            var adminUser = await _adminService.CreateAdminUserAsync(request);

            if (adminUser == null)
            {
                return Conflict(new { message = "Admin user with this user ID or email already exists" });
            }

            return CreatedAtAction(
                nameof(GetAdminUser),
                new { userId = adminUser.UserId },
                adminUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating admin user");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Update an existing admin user
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="request">Admin user update request</param>
    /// <returns>Updated admin user</returns>
    [HttpPut("{userId}")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserResponse>> UpdateAdminUser(string userId, [FromBody] UpdateAdminUserRequest request)
    {
        try
        {
            if (!ValidateAdminToken())
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            // Validation
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new { message = "User ID is required" });
            }

            var adminUser = await _adminService.UpdateAdminUserAsync(userId, request);

            if (adminUser == null)
            {
                return NotFound(new { message = "Admin user not found or email already exists" });
            }

            return Ok(adminUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating admin user: {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Validates the admin token from the Authorization header
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    private bool ValidateAdminToken()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return false;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var (isValid, _, _) = _jwtTokenService.ValidateAdminToken(token);

        return isValid;
    }
}
