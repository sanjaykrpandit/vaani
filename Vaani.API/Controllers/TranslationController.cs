using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Controllers;

/// <summary>
/// REST controller for translation session management.
/// For real-time audio streaming use the /hubs/translation SignalR hub instead.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TranslationController : ControllerBase
{
    private readonly ITranslationService _translationService;
    private readonly ILogger<TranslationController> _logger;

    public TranslationController(
        ITranslationService translationService,
        ILogger<TranslationController> logger)
    {
        _translationService = translationService;
        _logger = logger;
    }

    /// <summary>
    /// Start a backend translation session.
    /// Returns a TranslationSessionId and the SignalR hub URL to connect to.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(TranslationStartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TranslationStartResponse>> StartTranslation(
        [FromBody] TranslationStartRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(request.MeetingId))
            return BadRequest(new TranslationStartResponse
            {
                Success = false,
                ErrorCode = "INVALID_REQUEST",
                Message = "MeetingId is required."
            });

        var jwtToken = GetJwtToken();
        var response = await _translationService.StartSessionAsync(request, jwtToken);

        if (!response.Success)
        {
            _logger.LogWarning("Translation start failed: {Code} – {Msg}", response.ErrorCode, response.Message);
            return Ok(response); // Return 200 with Success=false for business errors
        }

        _logger.LogInformation("Translation session started via REST: {SessionId}", response.TranslationSessionId);
        return Ok(response);
    }

    /// <summary>
    /// Stop an active translation session.
    /// </summary>
    [HttpPost("stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> StopTranslation([FromBody] TranslationControlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TranslationSessionId))
            return BadRequest(new { error = "TranslationSessionId is required." });

        var stopped = await _translationService.StopSessionAsync(request.TranslationSessionId);

        if (!stopped)
            return NotFound(new { error = $"No active session found: {request.TranslationSessionId}" });

        return Ok(new { success = true, message = "Translation session stopped." });
    }

    /// <summary>
    /// Get the current status of a translation session.
    /// </summary>
    [HttpGet("status/{translationSessionId}")]
    [ProducesResponseType(typeof(TranslationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<TranslationStatusResponse> GetStatus(string translationSessionId)
    {
        if (string.IsNullOrWhiteSpace(translationSessionId))
            return BadRequest(new { error = "translationSessionId is required." });

        var status = _translationService.GetStatus(translationSessionId);

        if (status == null)
            return NotFound(new { error = $"No active session: {translationSessionId}" });

        return Ok(status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private string GetJwtToken()
    {
        if (Request.Headers.TryGetValue("Authorization", out var auth) &&
            auth.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth.ToString()["Bearer ".Length..].Trim();
        return string.Empty;
    }
}
