using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using System.Security.Claims;

namespace Vaani.API.Controllers;

/// <summary>
/// Admin controller for managing the conversational dictionary
/// </summary>
[ApiController]
[Route("api/admin/conversationaldictionary")]
[Authorize(Roles = "admin")]
public class AdminConversationalDictionaryController : ControllerBase
{
    private readonly IConversationalDictionaryService _dictionaryService;
    private readonly ILogger<AdminConversationalDictionaryController> _logger;

    public AdminConversationalDictionaryController(
        IConversationalDictionaryService dictionaryService,
        ILogger<AdminConversationalDictionaryController> logger)
    {
        _dictionaryService = dictionaryService;
        _logger = logger;
    }

    /// <summary>
    /// Get all dictionary entries, optionally filtered by language code
    /// </summary>
    /// <param name="languageCode">Optional ISO language code (e.g. hi-IN, mr-IN)</param>
    [HttpGet]
    [ProducesResponseType(typeof(ConversationalDictionaryListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConversationalDictionaryListResponse>> GetAll([FromQuery] string? languageCode)
    {
        try
        {
            var result = await _dictionaryService.GetAllAsync(languageCode);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving conversational dictionary entries");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred." });
        }
    }

    /// <summary>
    /// Get a single dictionary entry by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ConversationalDictionaryActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConversationalDictionaryActionResponse>> GetById(int id)
    {
        try
        {
            var result = await _dictionaryService.GetByIdAsync(id);
            if (!result.Success && result.ErrorCode == "NOT_FOUND")
                return NotFound(result);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving conversational dictionary entry {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred." });
        }
    }

    /// <summary>
    /// Add a new conversational dictionary entry
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ConversationalDictionaryActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConversationalDictionaryActionResponse>> Create(
        [FromBody] CreateConversationalDictionaryRequest request)
    {
        try
        {
            var adminUserId = GetAdminUserId();
            var result = await _dictionaryService.CreateAsync(request, adminUserId);

            if (!result.Success)
                return BadRequest(result);

            return CreatedAtAction(nameof(GetById), new { id = result.Item!.Id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating conversational dictionary entry");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred." });
        }
    }

    /// <summary>
    /// Update an existing conversational dictionary entry
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ConversationalDictionaryActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConversationalDictionaryActionResponse>> Update(
        int id,
        [FromBody] UpdateConversationalDictionaryRequest request)
    {
        try
        {
            var adminUserId = GetAdminUserId();
            var result = await _dictionaryService.UpdateAsync(id, request, adminUserId);

            if (!result.Success && result.ErrorCode == "NOT_FOUND")
                return NotFound(result);

            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating conversational dictionary entry {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred." });
        }
    }

    /// <summary>
    /// Delete a conversational dictionary entry
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(typeof(ConversationalDictionaryActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConversationalDictionaryActionResponse>> Delete(int id)
    {
        try
        {
            var adminUserId = GetAdminUserId();
            var result = await _dictionaryService.DeleteAsync(id, adminUserId);

            if (!result.Success && result.ErrorCode == "NOT_FOUND")
                return NotFound(result);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting conversational dictionary entry {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An error occurred." });
        }
    }

    private string GetAdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? "unknown-admin";
}
