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
        // Debug: Log all arguments
        Console.WriteLine("=== Vaani Startup Debug ===");
        Console.WriteLine($"Total args: {args?.Length ?? 0}");
        if (args != null && args.Length > 0)
        {
            for (int i = 0; i < args.Length; i++)
            {
                Console.WriteLine($"  Arg[{i}]: {args[i]}");
            }
        }

        // Try to get meetingId from ClickOnce activation URI first, then fallback to command-line args
        MeetingId = GetMeetingIdFromClickOnce() ?? ParseMeetingIdFromArgs(args);
        
        Console.WriteLine($"Final MeetingId: {MeetingId ?? "(null)"}");
        Console.WriteLine("===========================");

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

    /// <summary>
    /// Extracts meetingId from ClickOnce activation URI query parameters.
    /// For .NET 8 ClickOnce, activation data is passed through Environment variables or AppContext.
    /// Example: vaani.application?meetingId=VM-2025-1220-A7B3
    /// </summary>
    private static string? GetMeetingIdFromClickOnce()
    {
        try
        {
            Console.WriteLine("Checking ClickOnce activation data...");

            // Method 1: Check AppContext for activation data
            var activationData = AppContext.GetData("ActivationArguments.ActivationData") as string[];
            Console.WriteLine($"AppContext ActivationData: {activationData?.Length ?? 0} items");
            
            if (activationData != null && activationData.Length > 0)
            {
                // The activation URI is typically in the first element
                foreach (var data in activationData)
                {
                    Console.WriteLine($"  Activation data item: {data}");
                    
                    if (Uri.TryCreate(data, UriKind.Absolute, out var uri))
                    {
                        Console.WriteLine($"  Parsed URI: {uri}");
                        Console.WriteLine($"  Query: {uri.Query}");
                        
                        if (!string.IsNullOrWhiteSpace(uri.Query))
                        {
                            var query = uri.Query.TrimStart('?');
                            var meetingId = ParseQueryParameter(query, "meetingId");
                            
                            if (!string.IsNullOrWhiteSpace(meetingId))
                            {
                                Console.WriteLine($"✓ ClickOnce meetingId detected from AppContext: {meetingId}");
                                return meetingId.Trim();
                            }
                        }
                    }
                }
            }

            // Method 2: Check environment variables
            Console.WriteLine("Checking environment variables...");
            var envActivationUrl = Environment.GetEnvironmentVariable("ClickOnce_ActivationUrl");
            Console.WriteLine($"ClickOnce_ActivationUrl env: {envActivationUrl ?? "(null)"}");
            
            if (!string.IsNullOrWhiteSpace(envActivationUrl))
            {
                if (Uri.TryCreate(envActivationUrl, UriKind.Absolute, out var uri))
                {
                    Console.WriteLine($"  Parsed env URI: {uri}");
                    Console.WriteLine($"  Query: {uri.Query}");
                    
                    if (!string.IsNullOrWhiteSpace(uri.Query))
                    {
                        var query = uri.Query.TrimStart('?');
                        var meetingId = ParseQueryParameter(query, "meetingId");
                        
                        if (!string.IsNullOrWhiteSpace(meetingId))
                        {
                            Console.WriteLine($"✓ ClickOnce meetingId from env detected: {meetingId}");
                            return meetingId.Trim();
                        }
                    }
                }
            }

            // Method 3: Check all environment variables for anything related to activation
            Console.WriteLine("Checking all environment variables for activation info...");
            foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
            {
                var key = env.Key?.ToString() ?? "";
                if (key.Contains("Click", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("Activation", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("Deploy", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  {key} = {env.Value}");
                }
            }

            Console.WriteLine("No ClickOnce activation data found.");
        }
        catch (Exception ex)
        {
            // Not a ClickOnce deployment or error accessing deployment info
            Console.WriteLine($"ClickOnce detection failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Parses a query parameter from a query string.
    /// Example: "meetingId=VM-2025&other=value" -> returns "VM-2025"
    /// </summary>
    private static string? ParseQueryParameter(string query, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var parameters = query.Split('&');
        foreach (var param in parameters)
        {
            var keyValue = param.Split('=', 2);
            if (keyValue.Length == 2 && 
                keyValue[0].Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            {
                // URL decode the value
                return Uri.UnescapeDataString(keyValue[1]);
            }
        }

        return null;
    }

    private static string? ParseMeetingIdFromArgs(string[] args)
    {
        if (args == null || args.Length == 0)
            return null;

        Console.WriteLine("Parsing command-line arguments for meetingId...");

        foreach (var arg in args)
        {
            Console.WriteLine($"  Checking arg: {arg}");

            // Check if the entire arg is a URL with query parameters
            if (Uri.TryCreate(arg, UriKind.Absolute, out var uri))
            {
                Console.WriteLine($"    Arg is a valid URL: {uri}");
                if (!string.IsNullOrWhiteSpace(uri.Query))
                {
                    var query = uri.Query.TrimStart('?');
                    var meetingId = ParseQueryParameter(query, "meetingId");
                    if (!string.IsNullOrWhiteSpace(meetingId))
                    {
                        Console.WriteLine($"✓ MeetingId found in URL arg: {meetingId}");
                        return meetingId.Trim();
                    }
                }
            }

            // Support formats: meetingId=123, ?meetingId=123, --meetingId=123, /meetingId=123
            var cleanArg = arg.TrimStart('?', '-', '/');

            if (cleanArg.StartsWith("meetingId=", StringComparison.OrdinalIgnoreCase))
            {
                var value = cleanArg.Substring("meetingId=".Length).Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    Console.WriteLine($"✓ MeetingId found in arg format 'meetingId=': {value}");
                    return value;
                }
            }

            // Also support: --meetingId 123 (space-separated)
            if (cleanArg.Equals("meetingId", StringComparison.OrdinalIgnoreCase))
            {
                var index = Array.IndexOf(args, arg);
                if (index + 1 < args.Length)
                {
                    var value = args[index + 1].Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        Console.WriteLine($"✓ MeetingId found in space-separated format: {value}");
                        return value;
                    }
                }
            }
        }

        Console.WriteLine("No meetingId found in arguments.");
        return null;
    }
}