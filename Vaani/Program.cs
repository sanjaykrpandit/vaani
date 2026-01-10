using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Threading;
using System.Threading.Tasks;
using Vaani.Services;

namespace Vaani;

class Program
{
    private static SingleInstanceService? _singleInstance;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Check for single instance
        _singleInstance = new SingleInstanceService();
        
        if (!_singleInstance.IsFirstInstance())
        {
            // Another instance is already running
            Console.WriteLine("Another instance of Vaani is already running.");
            
            // Try to show a visual message
            try
            {
                ShowAlreadyRunningMessage();
            }
            catch
            {
                // Fallback to console message
                Console.WriteLine("═══════════════════════════════════════════════════════");
                Console.WriteLine("  Vaani is already running on this machine.");
                Console.WriteLine("  Please close the existing instance first.");
                Console.WriteLine("═══════════════════════════════════════════════════════");
                Thread.Sleep(3000);
            }
            
            // Exit this instance
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Clean up single instance lock
            _singleInstance?.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void ShowAlreadyRunningMessage()
    {
        var builder = BuildAvaloniaApp();
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose
        };

        builder.SetupWithLifetime(lifetime);
        
        var window = new Views.AlreadyRunningWindow();
        lifetime.MainWindow = window;
        
        var app = (App)Avalonia.Application.Current!;
        app.ApplicationLifetime = lifetime;
        
        lifetime.Start(Array.Empty<string>());
    }
}
