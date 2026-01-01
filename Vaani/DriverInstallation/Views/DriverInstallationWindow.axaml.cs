using Avalonia.Controls;
using Vaani.DriverInstallation.ViewModels;

namespace Vaani.DriverInstallation.Views;

public partial class DriverInstallationWindow : Window
{
    public DriverInstallationWindow()
    {
        InitializeComponent();
        var viewModel = new DriverInstallationViewModel();
        DataContext = viewModel;
        
        // Check driver status when window opens
        Opened += async (s, e) =>
        {
            await viewModel.CheckDriverStatusAsync();
        };
    }

    public DriverInstallationWindow(DriverInstallationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // Check driver status when window opens
        Opened += async (s, e) =>
        {
            await viewModel.CheckDriverStatusAsync();
        };
    }
}
