using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Lipi.Models;
using Lipi.ViewModels;

namespace Lipi.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            ApplyVerticalPresetSize();

            if (DataContext is LoginViewModel vm)
            {
                vm.LoginSucceeded += OnLoginSucceeded;

                if (!string.IsNullOrWhiteSpace(vm.MeetingId))
                {
                    vm.IsAutoLoginProcessing = true;
                    vm.UserName = vm.MeetingId;
                    if (vm.LoginCommand.CanExecute(null))
                        vm.LoginCommand.Execute(null);
                }
            }
        };

        Closed += (_, _) =>
        {
            if (DataContext is LoginViewModel vm)
                vm.LoginSucceeded -= OnLoginSucceeded;
        };
    }

    private void OnLoginSucceeded(LipiSessionContext context)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var mainVm = new MainViewModel(context);
            var mainWindow = new MainWindow
            {
                DataContext = mainVm
            };

            // Keep startup size/position consistent with login window
            mainWindow.Width = Width;
            mainWindow.Height = Height;
            mainWindow.Position = Position;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = mainWindow;
            }

            mainWindow.Show();
            mainWindow.Activate();
            Close();
        });
    }

    private void Header_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyVerticalPresetSize()
    {
        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen == null)
            return;

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var workHeightDip = screen.WorkingArea.Height / scaling;

        Width = 300;
        Height = workHeightDip * 0.8;
    }
}