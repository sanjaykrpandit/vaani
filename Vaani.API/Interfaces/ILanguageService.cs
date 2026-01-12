using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Interfaces;

/// <summary>
/// Interface for meeting-related operations
/// </summary>
public interface ILanguageService
{
    Task<IEnumerable<Language>> GetAllLanguagesAsync();
    Task<Language> CreateLanguageAsync(LanguageDto language);
    Task<Language> UpdateLanguageAsync(LanguageDto language);
    Task<bool> DeleteLanguageAsync(string languageCode);
}
