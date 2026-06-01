using Microsoft.AspNetCore.Mvc;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;
using Microsoft.AspNetCore.Authorization;

namespace Vaani.API.Controllers;

/// <summary>
/// Controller for language management
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin,subadmin")]
public class LanguageController : ControllerBase
{
    private readonly ILanguageService _languageService;
    private readonly ILogger<LanguageController> _logger;

    public LanguageController(ILanguageService languageService, ILogger<LanguageController> logger)
    {
        _languageService = languageService;
        _logger = logger;
    }


    /// <summary> get all languages</summary>     
    [HttpGet]
    public async Task<ActionResult<IEnumerable<LanguageDto>>> GetAllLanguages()
    {
        var languages = await _languageService.GetAllLanguagesAsync();
        var languageDtos = languages.Select(language => new LanguageDto
        {
            Id = language.Id,
            LanguageCode = language.LanguageCode,
            LanguageName = language.LanguageName,
            LanguageMaleNeural = language.LanguageMaleNeural,
            LanguageFemaleNeural = language.LanguageFemaleNeural,
            IsActive = language.IsActive,
            CreatedAt = language.CreatedAt,
            UpdatedAt = language.UpdatedAt,
            CreatedBy = language.CreatedBy,
            UpdatedBy = language.UpdatedBy
        });
        return Ok(languageDtos);
    }

    /// <summary> create language </summary>
    [HttpPost]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(LanguageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LanguageDto>> CreateLanguage([FromBody] LanguageDto request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.LanguageName)
                || string.IsNullOrWhiteSpace(request.LanguageCode)
                || string.IsNullOrWhiteSpace(request.LanguageMaleNeural)
                || string.IsNullOrWhiteSpace(request.LanguageFemaleNeural))
            {
                return BadRequest(new { Success = false, Message = "All mandatory fields are required!" });
            }

            var created = await _languageService.CreateLanguageAsync(request);

            var dto = new LanguageDto
            {
                Id = created.Id,
                LanguageCode = created.LanguageCode,
                LanguageName = created.LanguageName,
                LanguageMaleNeural = created.LanguageMaleNeural,
                LanguageFemaleNeural = created.LanguageFemaleNeural,
                IsActive = created.IsActive,
                CreatedAt = created.CreatedAt,
                UpdatedAt = created.UpdatedAt,
                CreatedBy = created.CreatedBy,
                UpdatedBy = created.UpdatedBy
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateLanguage endpoint");
            return StatusCode(StatusCodes.Status500InternalServerError, new { Success = false, Message = "An unexpected error occurred" });
        }
    }

    /// <summary> update language </summary>
    [HttpPut]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(LanguageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LanguageDto>> UpdateLanguage([FromBody] LanguageDto request)
    {
        try
        {
            if (request == null || string.IsNullOrWhiteSpace(request.LanguageName)
                || string.IsNullOrWhiteSpace(request.LanguageCode)
                || string.IsNullOrWhiteSpace(request.LanguageMaleNeural)
                || string.IsNullOrWhiteSpace(request.LanguageFemaleNeural))
            {
                return BadRequest(new { Success = false, Message = "All mandatory fields are required!" });
            }

            var updated = await _languageService.UpdateLanguageAsync(request);

            var dto = new LanguageDto
            {
                Id = updated.Id,
                LanguageCode = updated.LanguageCode,
                LanguageName = updated.LanguageName,
                LanguageMaleNeural = updated.LanguageMaleNeural,
                LanguageFemaleNeural = updated.LanguageFemaleNeural,
                IsActive = updated.IsActive,
                CreatedAt = updated.CreatedAt,
                UpdatedAt = updated.UpdatedAt,
                CreatedBy = updated.CreatedBy,
                UpdatedBy = updated.UpdatedBy
            };

            return Ok(dto);
        }
        catch (KeyNotFoundException knf)
        {
            return NotFound(new { Success = false, Message = knf.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UpdateLanguage endpoint");
            return StatusCode(StatusCodes.Status500InternalServerError, new { Success = false, Message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// delete language by code
    /// </summary>
    /// <param name="languageCode"></param>
    /// <returns></returns>
    [HttpDelete("{languageCode}")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteLanguage([FromRoute] string languageCode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return BadRequest(new { Success = false, Message = "Language code is required" });
            }

            var response = await _languageService.DeleteLanguageAsync(languageCode);
            if (!response)
            {
                return NotFound(new { Success = false, Message = "Language not found" });
            }

            return Ok(new { Success = true });
        }
        catch (KeyNotFoundException knf)
        {
            return NotFound(new { Success = false, Message = knf.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DeleteLanguage endpoint");
            return StatusCode(StatusCodes.Status500InternalServerError, new { Success = false, Message = "An unexpected error occurred" });
        }
    }

}
