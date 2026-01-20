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
    public static string? MeetingId { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Parse meetingId from args (optional)
        MeetingId = ParseMeetingIdFromArgs(args);

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

    private static string? ParseMeetingIdFromArgs(string[] args)
    {
        if (args == null || args.Length == 0)
            return null;

        foreach (var arg in args)
        {
            // Support formats: meetingId=123, ?meetingId=123, --meetingId=123, /meetingId=123
            var cleanArg = arg.TrimStart('?', '-', '/');

            if (cleanArg.StartsWith("meetingId=", StringComparison.OrdinalIgnoreCase))
            {
                var value = cleanArg.Substring("meetingId=".Length).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            // Also support: --reqId 123 (space-separated)
            if (cleanArg.Equals("meetingId", StringComparison.OrdinalIgnoreCase))
            {
                var index = Array.IndexOf(args, arg);
                if (index + 1 < args.Length)
                {
                    var value = args[index + 1].Trim();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
        }

        return null;
    }
}