using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;
using Vaani.DriverInstallation.Models;
using Vaani.DriverInstallation.Services;

namespace Vaani.DriverInstallation.ViewModels;

public class DriverInstallationViewModel : ReactiveObject
{
    private readonly DriverInstallationService _driverService;
    
    private InstallationStatus _currentStatus = InstallationStatus.AwaitingConsent;
    private string _statusMessage = "VB-CABLE Driver Installation";
    private string _progressMessage = "VB-CABLE is required for audio routing in Vaani.";
    private int _progressPercentage;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private bool _requiresRestart;
    private bool _isProcessing;
    private bool _canInstall = true;
    private bool _canClose;
    private bool _showConsentScreen = true;
    private bool _showProgressScreen;
    private bool _showCompletionScreen;

    public DriverInstallationViewModel()
    {
        _driverService = new DriverInstallationService();
        
        // Initialize commands
        InstallCommand = ReactiveCommand.CreateFromTask(StartInstallationAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        CloseCommand = ReactiveCommand.Create(Close);
        RestartLaterCommand = ReactiveCommand.Create(RestartLater);
    }

    #region Properties

    public InstallationStatus CurrentStatus
    {
        get => _currentStatus;
        set => this.RaiseAndSetIfChanged(ref _currentStatus, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        set => this.RaiseAndSetIfChanged(ref _progressMessage, value);
    }

    public int ProgressPercentage
    {
        get => _progressPercentage;
        set => this.RaiseAndSetIfChanged(ref _progressPercentage, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => this.RaiseAndSetIfChanged(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool RequiresRestart
    {
        get => _requiresRestart;
        set => this.RaiseAndSetIfChanged(ref _requiresRestart, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
    }

    public bool CanInstall
    {
        get => _canInstall;
        set => this.RaiseAndSetIfChanged(ref _canInstall, value);
    }

    public bool CanClose
    {
        get => _canClose;
        set => this.RaiseAndSetIfChanged(ref _canClose, value);
    }

    public bool ShowConsentScreen
    {
        get => _showConsentScreen;
        set => this.RaiseAndSetIfChanged(ref _showConsentScreen, value);
    }

    public bool ShowProgressScreen
    {
        get => _showProgressScreen;
        set => this.RaiseAndSetIfChanged(ref _showProgressScreen, value);
    }

    public bool ShowCompletionScreen
    {
        get => _showCompletionScreen;
        set => this.RaiseAndSetIfChanged(ref _showCompletionScreen, value);
    }

    #endregion

    #region Commands

    public ICommand InstallCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand RestartLaterCommand { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Checks if driver is already installed and updates UI accordingly
    /// </summary>
    public async Task CheckDriverStatusAsync()
    {
        CurrentStatus = InstallationStatus.Checking;
        StatusMessage = "Checking VB-CABLE Installation";
        ProgressMessage = "Checking if VB-CABLE driver is already installed...";

        await Task.Delay(500); // Brief delay for UX

        if (_driverService.IsVBCableInstalled())
        {
            CurrentStatus = InstallationStatus.AlreadyInstalled;
            StatusMessage = "Driver Already Installed";
            ProgressMessage = "VB-CABLE driver is already installed on your system.";
            ShowConsentScreen = false;
            ShowProgressScreen = false;
            ShowCompletionScreen = true;
            CanClose = true;
        }
        else
        {
            CurrentStatus = InstallationStatus.AwaitingConsent;
            StatusMessage = "VB-CABLE Driver Installation";
            ProgressMessage = "VB-CABLE is required for audio routing in Vaani.";
            ShowConsentScreen = true;
        }
    }

    private async Task StartInstallationAsync()
    {
        if (!CanInstall || IsProcessing) return;

        // Switch to progress screen
        ShowConsentScreen = false;
        ShowProgressScreen = true;
        ShowCompletionScreen = false;
        
        IsProcessing = true;
        CanInstall = false;
        CanClose = false;
        HasError = false;

        try
        {
            // Step 1: Download
            CurrentStatus = InstallationStatus.Downloading;
            StatusMessage = "Downloading Driver";
            ProgressMessage = "Downloading VB-CABLE driver package...";
            ProgressPercentage = 0;

            var progress = new Progress<int>(percent =>
            {
                ProgressPercentage = percent;
                ProgressMessage = $"Downloading VB-CABLE driver package... {percent}%";
            });

            var (downloadSuccess, downloadMessage) = await _driverService.DownloadDriverAsync(progress);
            
            if (!downloadSuccess)
            {
                ShowError("Download Failed", downloadMessage);
                return;
            }

            await Task.Delay(500); // Brief pause for UX

            // Step 2: Extract
            CurrentStatus = InstallationStatus.Extracting;
            StatusMessage = "Extracting Files";
            ProgressMessage = "Extracting driver files...";
            ProgressPercentage = 0;

            var extractProgress = new Progress<int>(percent =>
            {
                ProgressPercentage = percent;
                ProgressMessage = $"Extracting driver files... {percent}%";
            });

            var (extractSuccess, extractMessage) = await _driverService.ExtractDriverAsync(extractProgress);
            
            if (!extractSuccess)
            {
                ShowError("Extraction Failed", extractMessage);
                return;
            }

            await Task.Delay(500); // Brief pause for UX

            // Step 3: Install
            CurrentStatus = InstallationStatus.Installing;
            StatusMessage = "Installing Driver";
            ProgressMessage = "Installing VB-CABLE driver. Please approve the administrator prompt...";
            ProgressPercentage = 50;

            var (installSuccess, installMessage, requiresRestart) = await _driverService.InstallDriverAsync();
            
            if (!installSuccess)
            {
                ShowError("Installation Failed", installMessage);
                return;
            }

            // Step 4: Complete
            CurrentStatus = InstallationStatus.Completed;
            StatusMessage = "Installation Complete";
            ProgressPercentage = 100;
            RequiresRestart = requiresRestart;

            // Verify installation
            await Task.Delay(1000);
            bool isInstalled = _driverService.IsVBCableInstalled();

            if (isInstalled)
            {
                ProgressMessage = "VB-CABLE driver has been installed successfully!";
                RequiresRestart = false;
            }
            else
            {
                ProgressMessage = "Installation completed. Please restart your computer to complete the setup.";
                RequiresRestart = true;
            }

            // Show completion screen
            ShowProgressScreen = false;
            ShowCompletionScreen = true;
            CanClose = true;
        }
        catch (Exception ex)
        {
            ShowError("Unexpected Error", $"An unexpected error occurred: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
            
            // Cleanup temporary files
            try
            {
                _driverService.Cleanup();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private void ShowError(string title, string message)
    {
        HasError = true;
        StatusMessage = title;
        ErrorMessage = message;
        ProgressMessage = "Installation could not be completed.";
        ShowProgressScreen = false;
        ShowCompletionScreen = true;
        CanClose = true;
        
        CurrentStatus = InstallationStatus.Failed;
    }

    private void Cancel()
    {
        if (IsProcessing) return;

        CurrentStatus = InstallationStatus.Cancelled;
        CloseWindow();
    }

    private void Close()
    {
        CloseWindow();
    }

    private void RestartLater()
    {
        CloseWindow();
    }

    private void CloseWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.Windows?.FirstOrDefault(w => w.GetType().Name == "DriverInstallationWindow");
            window?.Close();
        }
    }

    #endregion
}
