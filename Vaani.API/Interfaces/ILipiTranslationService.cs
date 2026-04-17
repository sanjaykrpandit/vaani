using Vaani.API.Models.DTOs;

namespace Vaani.API.Interfaces;

public interface ILipiTranslationService
{
    Task<LipiStartResponse> StartSessionAsync(LipiStartRequest request, string jwtToken);
    Task<bool> StopSessionAsync(string lipiSessionId);
    LipiStatusResponse? GetStatus(string lipiSessionId);
    Task ProcessAudioChunkAsync(string lipiSessionId, LipiAudioChunkDto chunk);
    Task CleanupConnectionAsync(string connectionId);
    void RegisterEventCallback(string lipiSessionId, Func<LipiEventDto, Task> callback);
    bool IsOwnedByConnection(string lipiSessionId, string connectionId);
}