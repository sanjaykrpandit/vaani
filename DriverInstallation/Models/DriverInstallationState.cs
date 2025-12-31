using System;

namespace Vaani.DriverInstallation.Models;

/// <summary>
/// Represents the current state of the driver installation process.
/// </summary>
public class DriverInstallationState
{
    /// <summary>
    /// Current installation status
    /// </summary>
    public InstallationStatus Status { get; set; }
    
    /// <summary>
    /// Current status message to display to user
    /// </summary>
    public string StatusMessage { get; set; } = string.Empty;
    
    /// <summary>
    /// Detailed progress message
    /// </summary>
    public string ProgressMessage { get; set; } = string.Empty;
    
    /// <summary>
    /// Download/installation progress percentage (0-100)
    /// </summary>
    public int ProgressPercentage { get; set; }
    
    /// <summary>
    /// Indicates if an error occurred
    /// </summary>
    public bool HasError { get; set; }
    
    /// <summary>
    /// Error message if HasError is true
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicates if machine restart is required
    /// </summary>
    public bool RequiresRestart { get; set; }
    
    /// <summary>
    /// Indicates if the process is currently running
    /// </summary>
    public bool IsProcessing { get; set; }
    
    /// <summary>
    /// Timestamp when installation started
    /// </summary>
    public DateTime? StartedAt { get; set; }
    
    /// <summary>
    /// Timestamp when installation completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }
}
