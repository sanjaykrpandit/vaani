using Vaani.API.Models.DTOs;

namespace Vaani.API.Interfaces;

/// <summary>
/// Backend translation service managing per-session Azure Cognitive Services lifecycle
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Start a new translation session for the given meeting and direction
    /// </summary>
    Task<TranslationStartResponse> StartSessionAsync(TranslationStartRequest request, string jwtToken);

    /// <summary>
    /// Stop an active translation session
    /// </summary>
    Task<bool> StopSessionAsync(string translationSessionId);

    /// <summary>
    /// Get the current status of a translation session
    /// </summary>
    TranslationStatusResponse? GetStatus(string translationSessionId);

    /// <summary>
    /// Feed an incoming audio chunk into the correct pipeline for a session
    /// </summary>
    Task ProcessAudioChunkAsync(string translationSessionId, AudioChunkDto chunk);

    /// <summary>
    /// Mute or unmute the microphone pipeline for a session
    /// </summary>
    Task SetMicrophoneMuteAsync(string translationSessionId, bool muted);

    /// <summary>
    /// Mute or unmute the speaker pipeline for a session
    /// </summary>
    Task SetSpeakerMuteAsync(string translationSessionId, bool muted);

    /// <summary>
    /// Cleanup all sessions owned by a hub connection (called on disconnect)
    /// </summary>
    Task CleanupConnectionAsync(string connectionId);
}
