using Vaani.API.Models.DTOs;

namespace Vaani.API.Interfaces;

public interface IConversationalDictionaryService
{
    Task<ConversationalDictionaryListResponse> GetAllAsync(string? languageCode);
    Task<ConversationalDictionaryActionResponse> GetByIdAsync(int id);
    Task<ConversationalDictionaryActionResponse> CreateAsync(CreateConversationalDictionaryRequest request, string adminUserId);
    Task<ConversationalDictionaryActionResponse> UpdateAsync(int id, UpdateConversationalDictionaryRequest request, string adminUserId);
    Task<ConversationalDictionaryActionResponse> DeleteAsync(int id, string adminUserId);
}
