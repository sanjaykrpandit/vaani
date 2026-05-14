using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Services;

public class ConversationalDictionaryService : IConversationalDictionaryService
{
    private readonly VaaniDbContext _dbContext;
    private readonly ILogger<ConversationalDictionaryService> _logger;

    private static readonly HashSet<string> ValidMatchModes =
        new(StringComparer.OrdinalIgnoreCase) { "Exact", "Contains", "StartsWith" };

    public ConversationalDictionaryService(VaaniDbContext dbContext, ILogger<ConversationalDictionaryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ConversationalDictionaryListResponse> GetAllAsync(string? languageCode)
    {
        try
        {
            var query = _dbContext.ConversationalDictionary.AsQueryable();

            if (!string.IsNullOrWhiteSpace(languageCode))
                query = query.Where(e => e.LanguageCode == languageCode.Trim());

            var items = await query
                .OrderBy(e => e.LanguageCode)
                .ThenBy(e => e.Id)
                .Select(e => MapToResponse(e))
                .ToListAsync();

            return new ConversationalDictionaryListResponse
            {
                Success = true,
                Items = items,
                TotalCount = items.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving conversational dictionary entries");
            return new ConversationalDictionaryListResponse { Success = false, Message = "Failed to retrieve entries." };
        }
    }

    public async Task<ConversationalDictionaryActionResponse> GetByIdAsync(int id)
    {
        try
        {
            var entry = await _dbContext.ConversationalDictionary.FindAsync(id);
            if (entry == null)
                return Fail("NOT_FOUND", $"Entry with id={id} not found.");

            return new ConversationalDictionaryActionResponse { Success = true, Item = MapToResponse(entry) };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving conversational dictionary entry {Id}", id);
            return Fail("INTERNAL_ERROR", "Failed to retrieve entry.");
        }
    }

    public async Task<ConversationalDictionaryActionResponse> CreateAsync(
        CreateConversationalDictionaryRequest request,
        string adminUserId)
    {
        try
        {
            var validationError = ValidateRequest(request.LanguageCode, request.FormalText, request.ConversationalText, request.MatchMode);
            if (validationError != null)
                return Fail("VALIDATION_ERROR", validationError);

            var duplicate = await _dbContext.ConversationalDictionary.AnyAsync(e =>
                e.LanguageCode == request.LanguageCode.Trim() &&
                e.FormalText == request.FormalText.Trim());

            if (duplicate)
                return Fail("DUPLICATE", $"An entry for language '{request.LanguageCode}' with the same formal text already exists.");

            var entry = new ConversationalDictionaryEntry
            {
                LanguageCode = request.LanguageCode.Trim(),
                FormalText = request.FormalText.Trim(),
                ConversationalText = request.ConversationalText.Trim(),
                MatchMode = NormalizeMatchMode(request.MatchMode),
                IsActive = true,
                CreatedBy = adminUserId,
                UpdatedBy = adminUserId,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ConversationalDictionary.Add(entry);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Conversational dictionary entry created: id={Id} lang={Lang} by={Admin}",
                entry.Id, entry.LanguageCode, adminUserId);

            return new ConversationalDictionaryActionResponse { Success = true, Item = MapToResponse(entry), Message = "Entry created." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating conversational dictionary entry");
            return Fail("INTERNAL_ERROR", "Failed to create entry.");
        }
    }

    public async Task<ConversationalDictionaryActionResponse> UpdateAsync(
        int id,
        UpdateConversationalDictionaryRequest request,
        string adminUserId)
    {
        try
        {
            var validationError = ValidateRequest(null, request.FormalText, request.ConversationalText, request.MatchMode);
            if (validationError != null)
                return Fail("VALIDATION_ERROR", validationError);

            var entry = await _dbContext.ConversationalDictionary.FindAsync(id);
            if (entry == null)
                return Fail("NOT_FOUND", $"Entry with id={id} not found.");

            entry.FormalText = request.FormalText.Trim();
            entry.ConversationalText = request.ConversationalText.Trim();
            entry.MatchMode = NormalizeMatchMode(request.MatchMode);
            entry.IsActive = request.IsActive;
            entry.UpdatedBy = adminUserId;
            entry.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Conversational dictionary entry updated: id={Id} by={Admin}", id, adminUserId);

            return new ConversationalDictionaryActionResponse { Success = true, Item = MapToResponse(entry), Message = "Entry updated." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating conversational dictionary entry {Id}", id);
            return Fail("INTERNAL_ERROR", "Failed to update entry.");
        }
    }

    public async Task<ConversationalDictionaryActionResponse> DeleteAsync(int id, string adminUserId)
    {
        try
        {
            var entry = await _dbContext.ConversationalDictionary.FindAsync(id);
            if (entry == null)
                return Fail("NOT_FOUND", $"Entry with id={id} not found.");

            _dbContext.ConversationalDictionary.Remove(entry);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Conversational dictionary entry deleted: id={Id} by={Admin}", id, adminUserId);

            return new ConversationalDictionaryActionResponse { Success = true, Message = "Entry deleted." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting conversational dictionary entry {Id}", id);
            return Fail("INTERNAL_ERROR", "Failed to delete entry.");
        }
    }

    private static string? ValidateRequest(string? languageCode, string formalText, string conversationalText, string matchMode)
    {
        if (languageCode != null && string.IsNullOrWhiteSpace(languageCode))
            return "LanguageCode is required.";

        if (string.IsNullOrWhiteSpace(formalText))
            return "FormalText is required.";

        if (string.IsNullOrWhiteSpace(conversationalText))
            return "ConversationalText is required.";

        if (!ValidMatchModes.Contains(matchMode))
            return $"MatchMode must be one of: {string.Join(", ", ValidMatchModes)}.";

        return null;
    }

    private static string NormalizeMatchMode(string matchMode) =>
        ValidMatchModes.FirstOrDefault(m => m.Equals(matchMode, StringComparison.OrdinalIgnoreCase)) ?? "Contains";

    private static ConversationalDictionaryItemResponse MapToResponse(ConversationalDictionaryEntry e) => new()
    {
        Id = e.Id,
        LanguageCode = e.LanguageCode,
        FormalText = e.FormalText,
        ConversationalText = e.ConversationalText,
        MatchMode = e.MatchMode,
        IsActive = e.IsActive,
        CreatedBy = e.CreatedBy,
        UpdatedBy = e.UpdatedBy,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };

    private static ConversationalDictionaryActionResponse Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };
}
