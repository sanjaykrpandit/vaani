using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Controllers;

[ApiController]
[Route("api/lipi/direct")]
[Authorize]
public class LipiDirectController : ControllerBase
{
    private readonly ILipiDirectAccessService _lipiDirectAccessService;
    private readonly ILogger<LipiDirectController> _logger;

    public LipiDirectController(ILipiDirectAccessService lipiDirectAccessService, ILogger<LipiDirectController> logger)
    {
        _lipiDirectAccessService = lipiDirectAccessService;
        _logger = logger;
    }

    [HttpPost("token")]
    [ProducesResponseType(typeof(LipiDirectTokenResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LipiDirectTokenResponse>> GetDirectToken([FromBody] LipiDirectTokenRequest request)
    {
        try
        {
            var response = await _lipiDirectAccessService.GetDirectTokenAsync(request, GetJwtToken());
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in Lipi direct token endpoint.");
            return Ok(new LipiDirectTokenResponse { Success = false, ErrorCode = "INTERNAL_ERROR", Message = "Unexpected error issuing direct token." });
        }
    }

    [HttpPost("transcripts")]
    [ProducesResponseType(typeof(LipiDirectTranscriptBatchResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LipiDirectTranscriptBatchResponse>> PersistTranscripts([FromBody] LipiDirectTranscriptBatchRequest request)
    {
        try
        {
            var response = await _lipiDirectAccessService.PersistTranscriptBatchAsync(request, GetJwtToken());
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in Lipi direct transcript endpoint.");
            return Ok(new LipiDirectTranscriptBatchResponse { Success = false, ErrorCode = "INTERNAL_ERROR", Message = "Unexpected error persisting transcripts." });
        }
    }

    private string GetJwtToken()
    {
        if (Request.Headers.TryGetValue("Authorization", out var auth) &&
            auth.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth.ToString()["Bearer ".Length..].Trim();
        }

        return string.Empty;
    }
}