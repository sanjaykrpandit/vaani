using Vaani.API.Models.DTOs;

namespace Vaani.API.Interfaces;

public interface ILipiDirectAccessService
{
    Task<LipiDirectTokenResponse> GetDirectTokenAsync(LipiDirectTokenRequest request, string jwtToken);
    Task<LipiDirectTranscriptBatchResponse> PersistTranscriptBatchAsync(LipiDirectTranscriptBatchRequest request, string jwtToken);
    Task<LipiDirectDictionaryResponse> GetConversationalDictionaryAsync(IReadOnlyList<string> languages, string jwtToken);
}