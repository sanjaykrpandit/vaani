using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Controllers;

/// <summary>
/// Controller for admin meeting management (CRUD operations)
/// </summary>
[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "admin,subadmin")]
public class AdminMeetingsController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminMeetingsController> _logger;

    public AdminMeetingsController(
        IAdminService adminService,
        ILogger<AdminMeetingsController> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }

    /// <summary>
    /// Get all meetings
    /// </summary>
    /// <returns>List of all meetings</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<MeetingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<MeetingResponse>>> GetAllMeetings()
    {
        try
        {
            var meetings = await _adminService.GetAllMeetingsAsync(GetCurrentUserId(), GetCurrentUserRole());
            return Ok(meetings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all meetings");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Get a specific meeting by ID
    /// </summary>
    /// <param name="meetingId">Meeting ID</param>
    /// <returns>Meeting details</returns>
    [HttpGet("{meetingId}")]
    [ProducesResponseType(typeof(MeetingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MeetingResponse>> GetMeeting(string meetingId)
    {
        try
        {          

            var meeting = await _adminService.GetMeetingByIdAsync(meetingId, GetCurrentUserId(), GetCurrentUserRole());

            if (meeting == null)
            {
                return NotFound(new { message = "Meeting not found" });
            }

            return Ok(meeting);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving meeting: {MeetingId}", meetingId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Create a new meeting
    /// </summary>
    /// <param name="request">Meeting creation request</param>
    /// <returns>Created meeting</returns>
    [HttpPost]
    [ProducesResponseType(typeof(MeetingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MeetingResponse>> CreateMeeting([FromBody] CreateMeetingRequest request)
    {
        try
        {            

            if (string.IsNullOrWhiteSpace(request.MeetingId))
            {
                return BadRequest(new { message = "Meeting ID is required" });
            }

            if (string.IsNullOrWhiteSpace(request.MeetingName))
            {
                return BadRequest(new { message = "Meeting name is required" });
            }

            if (request.ValidUntil <= request.ValidFrom)
            {
                return BadRequest(new { message = "ValidUntil must be after ValidFrom" });
            }

            var meeting = await _adminService.CreateMeetingAsync(request, GetCurrentUserId());

            if (meeting == null)
            {
                return Conflict(new { message = "Meeting with this ID already exists" });
            }

            _logger.LogInformation("Meeting created: {MeetingId}", meeting.MeetingId);

            return CreatedAtAction(nameof(GetMeeting), new { meetingId = meeting.MeetingId }, meeting);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating meeting");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Update an existing meeting
    /// </summary>
    /// <param name="meetingId">Meeting ID</param>
    /// <param name="request">Meeting update request</param>
    /// <returns>Updated meeting</returns>
    [HttpPut("{meetingId}")]
    [ProducesResponseType(typeof(MeetingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MeetingResponse>> UpdateMeeting(string meetingId, [FromBody] UpdateMeetingRequest request)
    {
        try
        {           

            if (request.ValidFrom.HasValue && request.ValidUntil.HasValue && request.ValidUntil <= request.ValidFrom)
            {
                return BadRequest(new { message = "ValidUntil must be after ValidFrom" });
            }

            var meeting = await _adminService.UpdateMeetingAsync(meetingId, request, GetCurrentUserId(), GetCurrentUserRole());

            if (meeting == null)
            {
                return NotFound(new { message = "Meeting not found" });
            }

            _logger.LogInformation("Meeting updated: {MeetingId}", meetingId);

            return Ok(meeting);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating meeting: {MeetingId}", meetingId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Delete a meeting
    /// </summary>
    /// <param name="meetingId">Meeting ID</param>
    /// <returns>Success status</returns>
    [HttpDelete("{meetingId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteMeeting(string meetingId)
    {
        try
        {          

            var result = await _adminService.DeleteMeetingAsync(meetingId, GetCurrentUserId(), GetCurrentUserRole());

            if (!result)
            {
                return NotFound(new { message = "Meeting not found" });
            }

            _logger.LogInformation("Meeting deleted: {MeetingId}", meetingId);

            return Ok(new { message = "Meeting deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting meeting: {MeetingId}", meetingId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Get session metrics for a meeting
    /// </summary>
    /// <param name="meetingId">Meeting ID</param>
    /// <returns>Session metrics and analytics</returns>
    [HttpGet("{meetingId}/metrics")]
    [ProducesResponseType(typeof(SessionMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionMetricsDto>> GetSessionMetrics(string meetingId)
    {
        try
        {          

            var metrics = await _adminService.GetSessionMetricsAsync(meetingId, GetCurrentUserId(), GetCurrentUserRole());

            if (metrics == null)
            {
                return NotFound(new { message = "Meeting not found" });
            }

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving session metrics for meeting: {MeetingId}", meetingId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Get session logs for a specific session
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>List of session logs</returns>
    [HttpGet("sessions/{sessionId}/logs")]
    [ProducesResponseType(typeof(IEnumerable<SessionLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<SessionLogDto>>> GetSessionLogs(int sessionId)
    {
        try
        {          

            var logs = await _adminService.GetSessionLogsAsync(sessionId, GetCurrentUserId(), GetCurrentUserRole());
            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving session logs for session: {SessionId}", sessionId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    /// <summary>
    /// Generate encrypted token for meeting app download
    /// </summary>
    /// <param name="meetingId">Meeting ID</param>
    /// <returns>Encrypted token</returns>
    [HttpPost("{meetingId}/generate-token")]
    [ProducesResponseType(typeof(GenerateMeetingTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GenerateMeetingTokenResponse>> GenerateMeetingToken(string meetingId)
    {
        try
        {
            var response = await _adminService.GenerateMeetingTokenAsync(meetingId, GetCurrentUserId(), GetCurrentUserRole());

            if (response == null)
            {
                return NotFound(new { message = "Meeting not found or inactive" });
            }

            _logger.LogInformation("Token generated for meeting: {MeetingId}", meetingId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating token for meeting: {MeetingId}", meetingId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred" });
        }
    }

    //private bool ValidateAdminToken()
    //{
    //    var authHeader = Request.Headers.Authorization.FirstOrDefault();
    //    if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
    //    {
    //        return false;
    //    }

    //    var token = authHeader.Substring("Bearer ".Length).Trim();
    //    var (isValid, _, _) = _jwtTokenService.ValidateAdminToken(token);

    //    return isValid;
    //}

    private string GetCurrentUserId()
    {
        return User.FindFirstValue("userId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    }

    private string GetCurrentUserRole()
    {
        return User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role") ?? "admin";
    }
}
