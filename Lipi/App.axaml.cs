using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Lipi.ViewModels;
using Lipi.Views;
using ReactiveUI;

namespace Lipi;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new LoginWindow
            {
                DataContext = new LoginViewModel(Program.MeetingId)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}