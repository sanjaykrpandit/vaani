using Avalonia.Controls;
using Vaani.Authentication.ViewModels;

namespace Vaani.Authentication.Views;

public partial class MeetingLoginWindow : Window
{
    public MeetingLoginWindow()
    {
        InitializeComponent();
        var viewModel = new MeetingLoginViewModel();
        DataContext = viewModel;
        
        // Check driver status when window loads
        Opened += async (s, e) =>
        {
            await viewModel.OnWindowLoadedAsync();
        };

        Closed += (s, e) =>
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        };
    }

    public MeetingLoginWindow(MeetingLoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // Check driver status when window loads
        Opened += async (s, e) =>
        {
            await viewModel.OnWindowLoadedAsync();
        };

        Closed += (s, e) =>
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        };
    }
}
