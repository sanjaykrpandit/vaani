using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Vaani.Authentication.Services;
using Vaani.Authentication.Views;
using Vaani.DriverInstallation.Services;
using Vaani.Views;

namespace Vaani;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Register Avalonia scheduler for ReactiveUI
        RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MeetingLoginWindow();

            // Check for existing session
            //var sessionManager = new SessionManager();
            //bool hasValidSession = sessionManager.LoadSession() && sessionManager.HasActiveSession();
            //if (hasValidSession)
            //{
            //    // Valid session exists, check if driver is installed
            //    var driverService = new DriverInstallationService();
            //    if (!driverService.IsVBCableInstalled())
            //    {
            //        // Show driver installation window first
            //        var driverWindow = new Vaani.DriverInstallation.Views.DriverInstallationWindow();
            //        desktop.MainWindow = driverWindow;

            //        // After driver window closes, show main window
            //        driverWindow.Closed += (s, e) =>
            //        {
            //            desktop.MainWindow = new MainWindow();
            //            desktop.MainWindow.Show();
            //        };
            //    }
            //    else
            //    {
            //        desktop.MainWindow = new MainWindow();
            //    }
            //}
            //else
            //{
            //    // No valid session, show login window (driver check will happen after successful login)
            //    desktop.MainWindow = new MeetingLoginWindow();
            //}
        }

        base.OnFrameworkInitializationCompleted();
    }
}
