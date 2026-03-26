namespace Vaani.API.Models.DTOs;

/// <summary>
/// Request to control an active translation session (mute, unmute, etc.)
/// </summary>
public class TranslationControlRequest
{
    public string TranslationSessionId { get; set; } = string.Empty;
    public TranslationControlAction Action { get; set; }
}

public enum TranslationControlAction
{
    MuteMicrophone = 1,
    UnmuteMicrophone = 2,
    MuteSpeaker = 3,
    UnmuteSpeaker = 4,
    Stop = 5
}
