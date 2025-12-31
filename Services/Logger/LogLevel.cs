namespace vconsole.Services.Logger;

/// <summary>
/// Defines logging severity levels for the translation service.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Detailed diagnostic information for troubleshooting (most verbose)
    /// </summary>
    Debug = 0,
    
    /// <summary>
    /// General informational messages about normal operation
    /// </summary>
    Info = 1,
    
    /// <summary>
    /// Warning messages for potentially problematic situations
    /// </summary>
    Warning = 2,
    
    /// <summary>
    /// Error messages for failures and exceptions
    /// </summary>
    Error = 3,
    
    /// <summary>
    /// Critical errors that require immediate attention
    /// </summary>
    Critical = 4
}
