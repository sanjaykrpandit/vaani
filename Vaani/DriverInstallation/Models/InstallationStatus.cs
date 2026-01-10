namespace Vaani.DriverInstallation.Models;

/// <summary>
/// Represents the current status of the driver installation process.
/// </summary>
public enum InstallationStatus
{
    /// <summary>
    /// Initial state - waiting for user consent
    /// </summary>
    AwaitingConsent,
    
    /// <summary>
    /// Checking if driver is already installed
    /// </summary>
    Checking,
    
    /// <summary>
    /// Downloading driver package
    /// </summary>
    Downloading,
    
    /// <summary>
    /// Extracting driver files
    /// </summary>
    Extracting,
    
    /// <summary>
    /// Installing driver
    /// </summary>
    Installing,
    
    /// <summary>
    /// Installation completed successfully
    /// </summary>
    Completed,
    
    /// <summary>
    /// Installation failed
    /// </summary>
    Failed,
    
    /// <summary>
    /// Driver already installed
    /// </summary>
    AlreadyInstalled,
    
    /// <summary>
    /// User cancelled installation
    /// </summary>
    Cancelled
}
