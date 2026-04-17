using Vaani.Models;

namespace Vaani.Services;

/// <summary>
/// Interface for translation services (both full translation and bypass mode)
/// </summary>
public interface ITranslationService : IDisposable
{
    #region Properties
    
    /// <summary>
    /// Indicates whether the service is currently running
    /// </summary>
    bool IsRunning { get; }
    
    #endregion

    #region Events
    
    /// <summary>
    /// Raised when a log message is generated
    /// </summary>
    event EventHandler<string>? LogMessage;
    
    /// <summary>
    /// Raised when a message is received (recognizing or recognized)
    /// </summary>
    event EventHandler<MessageEventArgs>? MessageReceived;
    
    /// <summary>
    /// Raised when a translation is received
    /// </summary>
    event EventHandler<TranslationEventArgs>? TranslationReceived;
    
    /// <summary>
    /// Raised when a system message is generated
    /// </summary>
    event EventHandler<SystemMessageEventArgs>? SystemMessage;
    
    /// <summary>
    /// Raised when synthesis status changes
    /// </summary>
    event EventHandler<SynthesizingEventArgs>? SynthesizingStatusChanged;
    
    #endregion

    #region Methods
    
    /// <summary>
    /// Start the translation service
    /// </summary>
    Task StartTranslationAsync(TranslationSettings settings);
    
    /// <summary>
    /// Stop the translation service
    /// </summary>
    Task StopTranslationAsync();
    
    /// <summary>
    /// Mute or unmute the microphone
    /// </summary>
    void SetMicrophoneMute(bool muted);
    
    /// <summary>
    /// Mute or unmute the speaker
    /// </summary>
    void SetSpeakerMute(bool muted);
    
    #endregion
}
