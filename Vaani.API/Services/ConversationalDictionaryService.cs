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

    public async Task<ConversationalDictionaryListResponse> GetAllAsync(string? languageCode, string? domain, int page = 1, int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _dbContext.ConversationalDictionary.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(languageCode))
            query = query.Where(e => e.LanguageCode == languageCode.Trim());

        if (!string.IsNullOrWhiteSpace(domain))
            query = query.Where(e => e.Domain == NormalizeDomain(domain));

        var totalCount = await query.CountAsync();

        var rawItems = await query
            .OrderBy(e => e.LanguageCode)
            .ThenBy(e => e.Domain)
            .ThenBy(e => e.MatchMode == "Exact" ? 0 : e.MatchMode == "StartsWith" ? 1 : 2)
            .ThenByDescending(e => e.FormalText.Length)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new ConversationalDictionaryListResponse
        {
            Success = true,
            Items = rawItems.Select(MapToResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<ConversationalDictionaryActionResponse> GetByIdAsync(int id)
    {
        var entry = await _dbContext.ConversationalDictionary.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entry == null)
            return Fail("NOT_FOUND", $"Entry with id={id} not found.");

        return new ConversationalDictionaryActionResponse { Success = true, Item = MapToResponse(entry) };
    }

    public async Task<ConversationalDictionaryActionResponse> CreateAsync(
        CreateConversationalDictionaryRequest request,
        string adminUserId)
    {
        // DTO validation attributes already enforce [Required]/[MaxLength]/[RegularExpression] via [ApiController]
        var duplicate = await _dbContext.ConversationalDictionary.AnyAsync(e =>
            e.LanguageCode == request.LanguageCode.Trim() &&
            e.Domain == NormalizeDomain(request.Domain) &&
            e.FormalText == request.FormalText.Trim());

        if (duplicate)
            return Fail("DUPLICATE", $"An entry for '{request.LanguageCode}' in domain '{NormalizeDomain(request.Domain)}' with the same formal text already exists.");

        var entry = new ConversationalDictionaryEntry
        {
            LanguageCode = request.LanguageCode.Trim(),
            Domain = NormalizeDomain(request.Domain),
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

        _logger.LogInformation("Dictionary entry created: id={Id} lang={Lang} by={Admin}",
            entry.Id, entry.LanguageCode, adminUserId);

        return new ConversationalDictionaryActionResponse
        {
            Success = true,
            Item = MapToResponse(entry),
            Message = "Entry created."
        };
    }

    public async Task<ConversationalDictionaryActionResponse> UpdateAsync(
        int id,
        UpdateConversationalDictionaryRequest request,
        string adminUserId)
    {
        var entry = await _dbContext.ConversationalDictionary.FindAsync(id);
        if (entry == null)
            return Fail("NOT_FOUND", $"Entry with id={id} not found.");

        var normalizedDomain = NormalizeDomain(request.Domain);
        var duplicate = await _dbContext.ConversationalDictionary.AnyAsync(e =>
            e.Id != id &&
            e.LanguageCode == entry.LanguageCode &&
            e.Domain == normalizedDomain &&
            e.FormalText == request.FormalText.Trim());

        if (duplicate)
            return Fail("DUPLICATE", $"Another entry for '{entry.LanguageCode}' in domain '{normalizedDomain}' with the same formal text already exists.");

        entry.Domain = normalizedDomain;
        entry.FormalText = request.FormalText.Trim();
        entry.ConversationalText = request.ConversationalText.Trim();
        entry.MatchMode = NormalizeMatchMode(request.MatchMode);
        entry.IsActive = request.IsActive;
        entry.UpdatedBy = adminUserId;
        entry.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Dictionary entry updated: id={Id} by={Admin}", id, adminUserId);

        return new ConversationalDictionaryActionResponse
        {
            Success = true,
            Item = MapToResponse(entry),
            Message = "Entry updated."
        };
    }

    public async Task<ConversationalDictionaryActionResponse> DeleteAsync(int id, string adminUserId)
    {
        var entry = await _dbContext.ConversationalDictionary.FindAsync(id);
        if (entry == null)
            return Fail("NOT_FOUND", $"Entry with id={id} not found.");

        // Soft delete — preserves audit history; clients skip inactive entries
        entry.IsActive = false;
        entry.UpdatedBy = adminUserId;
        entry.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Dictionary entry soft-deleted: id={Id} by={Admin}", id, adminUserId);

        return new ConversationalDictionaryActionResponse { Success = true, Message = "Entry deleted." };
    }

    private static string NormalizeMatchMode(string matchMode) =>
        ValidMatchModes.FirstOrDefault(m => m.Equals(matchMode, StringComparison.OrdinalIgnoreCase)) ?? "Contains";

    private static string NormalizeDomain(string? domain) =>
        string.IsNullOrWhiteSpace(domain) ? "general" : domain.Trim().ToLowerInvariant();

    private static ConversationalDictionaryItemResponse MapToResponse(ConversationalDictionaryEntry e) => new()
    {
        Id = e.Id,
        LanguageCode = e.LanguageCode,
        Domain = e.Domain,
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
