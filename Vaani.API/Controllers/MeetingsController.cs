using Microsoft.AspNetCore.Mvc;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Controllers;

/// <summary>
/// Controller for meeting validation and management
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MeetingsController : ControllerBase
{
    private readonly IMeetingService _meetingService;
    private readonly ILogger<MeetingsController> _logger;

    public MeetingsController(IMeetingService meetingService, ILogger<MeetingsController> logger)
    {
        _meetingService = meetingService;
        _logger = logger;
    }

    /// <summary>
    /// Validate a meeting ID and create a session
    /// </summary>
    /// <param name="request">Meeting validation request</param>
    /// <returns>Meeting validation response with encrypted configuration</returns>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(MeetingValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MeetingValidationResponse>> ValidateMeeting([FromBody] MeetingValidationRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.MeetingId))
            {
                return BadRequest(new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "INVALID_REQUEST",
                    Message = "Meeting ID is required"
                });
            }

            if (string.IsNullOrWhiteSpace(request.DeviceId))
            {
                return BadRequest(new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "INVALID_REQUEST",
                    Message = "Device ID is required"
                });
            }

            var response = await _meetingService.ValidateMeetingAsync(request);

            if (!response.IsValid)
            {
                return Ok(response); // Return 200 with IsValid=false for business logic errors
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ValidateMeeting endpoint");
            return StatusCode(StatusCodes.Status500InternalServerError, new MeetingValidationResponse
            {
                IsValid = false,
                ErrorCode = "INTERNAL_ERROR",
                Message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// Check if a meeting is valid (for health checks)
    /// </summary>
    /// <param name="meetingId">Meeting ID to check</param>
    /// <returns>Boolean indicating if meeting is valid</returns>
    [HttpGet("{meetingId}/valid")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<ActionResult<bool>> IsMeetingValid(string meetingId)
    {
        try
        {
            var isValid = await _meetingService.IsMeetingValidAsync(meetingId);
            return Ok(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking meeting validity: {MeetingId}", meetingId);
            return Ok(false);
        }
    }
}
