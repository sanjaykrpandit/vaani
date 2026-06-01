using Vaani.API.Models.DTOs;

namespace Vaani.API.Interfaces;

public interface IConversationalRewriteFallbackService
{
    Task<LipiDirectConversationalRewriteResponse> RewriteAsync(
        string sourceLanguage,
        string? domain,
        string? originalText,
        IReadOnlyDictionary<string, string> translations,
        CancellationToken cancellationToken = default);
}
