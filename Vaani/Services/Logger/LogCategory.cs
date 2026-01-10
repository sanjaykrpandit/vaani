namespace vconsole.Services.Logger;

/// <summary>
/// Defines logging categories for better log organization and filtering.
/// </summary>
public enum LogCategory
{
    /// <summary>
    /// General system messages and status updates
    /// </summary>
    System,
    
    /// <summary>
    /// Outgoing translation flow (your speech -> meeting)
    /// </summary>
    Outgoing,
    
    /// <summary>
    /// Incoming translation flow (meeting -> your speakers)
    /// </summary>
    Incoming,
    
    /// <summary>
    /// Speech recognition events
    /// </summary>
    Recognition,
    
    /// <summary>
    /// Speech synthesis events
    /// </summary>
    Synthesis,
    
    /// <summary>
    /// Audio playback operations
    /// </summary>
    Playback,
    
    /// <summary>
    /// Audio capture and monitoring
    /// </summary>
    AudioCapture,
    
    /// <summary>
    /// Duplicate detection and transcript management
    /// </summary>
    Duplicate,
    
    /// <summary>
    /// Performance metrics and statistics
    /// </summary>
    Metrics,
    
    /// <summary>
    /// Queue management and processing
    /// </summary>
    Queue,
    
    /// <summary>
    /// Device detection and configuration
    /// </summary>
    Device,
    
    /// <summary>
    /// Connection and initialization
    /// </summary>
    Connection,

    EchoPrevention
}
