using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Controllers;

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

    /// <summary>Get all entries with optional language filter and pagination</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ConversationalDictionaryListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ConversationalDictionaryListResponse>> GetAll(
        [FromQuery] string? languageCode,
        [FromQuery] string? domain,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _dictionaryService.GetAllAsync(languageCode, domain, page, pageSize);
        return Ok(result);
    }

    /// <summary>Get a single entry by ID</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ConversationalDictionaryActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationalDictionaryActionResponse>> GetById(int id)
    {
        var result = await _dictionaryService.GetByIdAsync(id);
        if (!result.Success && result.ErrorCode == "NOT_FOUND")
            return NotFound(result);

        return Ok(result);
    }

    /// <summary>Create a new dictionary entry</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ConversationalDictionaryActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConversationalDictionaryActionResponse>> Create(
        [FromBody] CreateConversationalDictionaryRequest request)
    {
        // [ApiController] enforces [Required]/[MaxLength]/[RegularExpression] — returns 400 before reaching here
        var result = await _dictionaryService.CreateAsync(request, GetAdminUserId());

        if (!result.Success)
            return BadRequest(result);

        return CreatedAtAction(nameof(GetById), new { id = result.Item!.Id }, result);
    }

    /// <summary>Update an existing dictionary entry</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ConversationalDictionaryActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationalDictionaryActionResponse>> Update(
        int id,
        [FromBody] UpdateConversationalDictionaryRequest request)
    {
        var result = await _dictionaryService.UpdateAsync(id, request, GetAdminUserId());

        if (!result.Success && result.ErrorCode == "NOT_FOUND")
            return NotFound(result);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>Soft-delete a dictionary entry (sets IsActive = false)</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(typeof(ConversationalDictionaryActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationalDictionaryActionResponse>> Delete(int id)
    {
        var result = await _dictionaryService.DeleteAsync(id, GetAdminUserId());

        if (!result.Success && result.ErrorCode == "NOT_FOUND")
            return NotFound(result);

        return Ok(result);
    }

    private string GetAdminUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? "unknown-admin";
}
