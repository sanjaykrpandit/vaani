using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Vaani.API.Services;

/// <summary>
/// Service for Language-related operations
/// </summary>
public class LanguageService : ILanguageService
{
    private readonly VaaniDbContext _dbContext;
    private readonly ILogger<LanguageService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LanguageService(
        VaaniDbContext dbContext,
        ILogger<LanguageService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    private string GetCurrentUserId()
    {
        try
        {
            var ctx = _httpContextAccessor?.HttpContext;
            var user = ctx?.User;
            if (user == null)
                return "system";

            // Try common claim types in order
            var claim = user.FindFirst(ClaimTypes.Name)?.Value
                        ?? user.FindFirst("name")?.Value
                        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? user.FindFirst("sub")?.Value
                        ?? user.FindFirst("email")?.Value;

            return string.IsNullOrWhiteSpace(claim) ? "system" : claim!;
        }
        catch
        {
            return "system";
        }
    }

    public async Task<IEnumerable<Language>> GetAllLanguagesAsync()
    {
        return await _dbContext.Languages.ToListAsync();
    }
    public async Task<Language> CreateLanguageAsync(LanguageDto language)
    {
        var existingLanguage = await _dbContext.Languages
            .FirstOrDefaultAsync(l => l.LanguageCode == language.LanguageCode);

        if (existingLanguage != null)
        {
            throw new InvalidOperationException($"Language with code {language.LanguageCode} already exists.");
        }

        var currentUser = GetCurrentUserId();

        Language newLanguage = new Language
        {
            LanguageCode = language.LanguageCode,
            LanguageName = language.LanguageName,
            IsActive = language.IsActive,
            LanguageFemaleNeural = language.LanguageFemaleNeural,
            LanguageMaleNeural = language.LanguageMaleNeural,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = string.IsNullOrWhiteSpace(language.CreatedBy) ? currentUser : language.CreatedBy,
            UpdatedBy = string.IsNullOrWhiteSpace(language.UpdatedBy) ? currentUser : language.UpdatedBy
        };

        _dbContext.Languages.Add(newLanguage);
        await _dbContext.SaveChangesAsync();
        return newLanguage;
    }
    public async Task<Language> UpdateLanguageAsync(LanguageDto language)
    {

        var existingLanguage = await _dbContext.Languages
            .FirstOrDefaultAsync(l => l.LanguageCode == language.LanguageCode);

        if (existingLanguage == null)
        {
            throw new KeyNotFoundException($"Language with code {language.LanguageCode} not found.");
        }

        var currentUser = GetCurrentUserId();

        existingLanguage.LanguageName = language.LanguageName;
        existingLanguage.IsActive = language.IsActive;
        existingLanguage.LanguageCode = language.LanguageCode;
        existingLanguage.LanguageFemaleNeural = language.LanguageFemaleNeural;
        existingLanguage.LanguageMaleNeural = language.LanguageMaleNeural;
        existingLanguage.UpdatedAt = DateTime.UtcNow;
        existingLanguage.UpdatedBy = string.IsNullOrWhiteSpace(language.UpdatedBy) ? currentUser : language.UpdatedBy;
        _dbContext.Languages.Update(existingLanguage);

        await _dbContext.SaveChangesAsync();

        return existingLanguage;
    }
    public async Task<bool> DeleteLanguageAsync(string languageCode)
    {
        var existingLanguage = await _dbContext.Languages
            .FirstOrDefaultAsync(l => l.LanguageCode == languageCode);


        if (existingLanguage == null)
        {
            throw new KeyNotFoundException($"Language with code {languageCode} not found.");
        }

        _dbContext.Languages.Remove(existingLanguage);
        await _dbContext.SaveChangesAsync();
        return true;
    }
 }
