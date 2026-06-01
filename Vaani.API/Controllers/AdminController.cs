using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Controllers;

/// <summary>
/// Controller for admin authentication
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IAdminService adminService, IConfiguration configuration, ILogger<AdminController> logger)
    {
        _adminService = adminService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Admin login endpoint
    /// </summary>
    /// <param name="request">Login credentials</param>
    /// <returns>Login response with access token</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AdminLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AdminLoginResponse>> Login([FromBody] AdminLoginRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return BadRequest(new AdminLoginResponse
                {
                    Success = false,
                    Message = "User ID is required"
                });
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new AdminLoginResponse
                {
                    Success = false,
                    Message = "Password is required"
                });
            }

            var response = await _adminService.AuthenticateAsync(request.UserId, request.Password);

            if (!response.Success)
            {
                return Unauthorized(response);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in admin login endpoint");
            return StatusCode(StatusCodes.Status500InternalServerError, new AdminLoginResponse
            {
                Success = false,
                Message = "An unexpected error occurred"
            });
        }
    }

    [Authorize]
    [HttpGet("app-download-links")]
    public ActionResult<object> GetAppDownloadLinks()
    {
        return Ok(new
        {
            link = _configuration["AppDownload:Link"] ?? string.Empty,
            subtitleLink = _configuration["AppDownload:SubtitleLink"] ?? string.Empty
        });
    }
}
