using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Controllers;

/// <summary>
/// Controller for session management (heartbeat, end session)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly ISessionService _sessionService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ISessionService sessionService, 
        IJwtTokenService jwtTokenService,
        ILogger<SessionsController> logger)
    {
        _sessionService = sessionService;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new session
    /// </summary>
    /// <param name="request">Session creation request</param>
    /// <returns>Session response with token</returns>
    [HttpPost("start")]
    [Authorize]
    [ProducesResponseType(typeof(HeartbeatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CreateSessionResponse>> StartSession([FromBody] CreateSessionRequest request)
    {
        try
        {
            // Get token from Authorization header
            var token = GetTokenFromHeader();
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized(new HeartbeatResponse
                {
                    Success = false,
                    ErrorCode = "NO_TOKEN",
                    Message = "Authorization token is required"
                });
            }

            if (string.IsNullOrWhiteSpace(request.MeetingId))
            {
                return BadRequest(new CreateSessionResponse
                {
                    Success = false,
                    Message = "Meeting ID is required"
                });
            }

            if (string.IsNullOrWhiteSpace(request.DeviceId))
            {
                return BadRequest(new CreateSessionResponse
                {
                    Success = false,
                    Message = "Device ID is required"
                });
            }

            var (success, message, sessionId) = await _sessionService.StartSessionAsync(request.MeetingId, request.DeviceId);
            if (!success)
            {
                return Ok(new CreateSessionResponse
                {
                    Success = false,
                    Message = message
                });
            }

            var remainingMinutes = await _sessionService.GetRemainingMinutesAsync(request.MeetingId, request.DeviceId);
            var expiresAt = remainingMinutes > 0
                ? DateTime.UtcNow.AddMinutes(remainingMinutes)
                : (DateTime?)null;

            _logger.LogInformation("Session created successfully for meeting: {MeetingId}, device: {DeviceId}, sessionId: {SessionId}", 
                request.MeetingId, request.DeviceId, sessionId);

            return Ok(new CreateSessionResponse
            {
                Success = true,
                SessionId = sessionId,
                Message = message,
                ExpiresAt = expiresAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateSession endpoint");
            return Ok(new CreateSessionResponse
            {
                Success = false,
                Message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// Send heartbeat to keep session alive
    /// </summary>
    /// <param name="request">Heartbeat request with timestamp</param>
    /// <returns>Heartbeat response with remaining time</returns>
    [HttpPost("heartbeat")]
    [Authorize]
    [ProducesResponseType(typeof(HeartbeatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HeartbeatResponse>> Heartbeat([FromBody] HeartbeatRequest request)
    {
        try
        {
            // Get token from Authorization header
            var token = GetTokenFromHeader();
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized(new HeartbeatResponse
                {
                    Success = false,
                    ErrorCode = "NO_TOKEN",
                    Message = "Authorization token is required"
                });
            }

            // Extract meetingId and deviceId from JWT token
            var (isValid, meetingId, deviceId) = _jwtTokenService.ValidateToken(token);
            if (!isValid || meetingId == null || deviceId == null)
            {
                return Unauthorized(new HeartbeatResponse
                {
                    Success = false,
                    ErrorCode = "INVALID_TOKEN",
                    Message = "Invalid authorization token"
                });
            }

            var response = await _sessionService.ProcessHeartbeatAsync(meetingId, deviceId);

            if (!response.Success)
            {
                return response.ErrorCode == "SESSION_EXPIRED" 
                    ? Unauthorized(response) 
                    : Ok(response);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Heartbeat endpoint");
            return Ok(new HeartbeatResponse
            {
                Success = false,
                ErrorCode = "INTERNAL_ERROR",
                Message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// End session gracefully
    /// </summary>
    /// <param name="request">End session request with session ID</param>
    /// <returns>End session response</returns>
    [HttpPost("end")]
    [Authorize]
    [ProducesResponseType(typeof(EndSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<EndSessionResponse>> EndSession([FromBody] EndSessionRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.meetingId) || string.IsNullOrWhiteSpace(request.deviceId))
            {
                return BadRequest(new EndSessionResponse
                {
                    Success = false,
                    ErrorCode = "INVALID_REQUEST",
                    Message = "Meeting ID is required"
                });
            }          

            var response = await _sessionService.EndSessionAsync(request);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in EndSession endpoint");
            return Ok(new EndSessionResponse
            {
                Success = false,
                ErrorCode = "INTERNAL_ERROR",
                Message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// Get remaining minutes for current session
    /// </summary>
    /// <returns>Remaining minutes</returns>
    [HttpGet("remaining")]
    [Authorize]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<int>> GetRemainingMinutes()
    {
        try
        {
            var token = GetTokenFromHeader();
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized();
            }

            // Extract meetingId and deviceId from JWT token
            var (isValid, meetingId, deviceId) = _jwtTokenService.ValidateToken(token);
            if (!isValid || meetingId == null || deviceId == null)
            {
                return Unauthorized();
            }

            var remaining = await _sessionService.GetRemainingMinutesAsync(meetingId, deviceId);
            return Ok(remaining);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetRemainingMinutes endpoint");
            return Ok(0);
        }
    }

    /// <summary>
    /// Extract Bearer token from Authorization header
    /// </summary>
    private string? GetTokenFromHeader()
    {
        var authHeader = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return null;
        }

        return authHeader.Substring("Bearer ".Length).Trim();
    }
}
