using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vaani.Authentication.ViewModels;
using Vaani.Authentication.Views;
using Vaani.Services;

namespace Vaani;

class Program
{
    private static SingleInstanceService? _singleInstance;
    private static CancellationTokenSource? _activationListenerCts;
    private static StreamWriter? _startupLogWriter;
    public static event Action<string>? MeetingIdUpdated;
    public static string? MeetingId { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        InitializeStartupLogging();

        // Debug: Log all arguments
        Console.WriteLine("=== Vaani Startup Debug ===");
        Console.WriteLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        Console.WriteLine($"Total args: {args?.Length ?? 0}");
        if (args != null && args.Length > 0)
        {
            for (int i = 0; i < args.Length; i++)
            {
                Console.WriteLine($"  Arg[{i}]: {args[i]}");
            }
        }

        // Try to get meetingId from ClickOnce activation URI first, then fallback to command-line args
        MeetingId = GetMeetingIdFromClickOnce()
                    ?? ParseMeetingIdFromArgs(args)
                    ?? GetMeetingIdFromRawCommandLine();
        
        Console.WriteLine($"Final MeetingId: {MeetingId ?? "(null)"}");
        Console.WriteLine("===========================");

        // Check for single instance
        _singleInstance = new SingleInstanceService();

        if (!_singleInstance.IsFirstInstance())
        {
            // Another instance is already running
            Console.WriteLine("Another instance of Vaani is already running.");

            if (!string.IsNullOrWhiteSpace(MeetingId))
            {
                // Pass latest activation meetingId to the already running instance
                if (SingleInstanceService.TryNotifyFirstInstance(MeetingId))
                {
                    return;
                }
            }

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

        _activationListenerCts = SingleInstanceService.StartActivationListener(HandleSecondaryActivation);
        var deviceService = DeviceService.Instance;

        // Do not force VB-CABLE as Windows default.
        // Always try to restore user-facing physical defaults on launch.
        try { deviceService.TryRestorePhysicalDefaults(); } catch { }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Restore physical defaults again on app exit.
            try { deviceService.TryRestorePhysicalDefaults(); } catch { }

            _activationListenerCts?.Cancel();
            _activationListenerCts?.Dispose();

            // Clean up single instance lock
            _singleInstance?.Dispose();

            _startupLogWriter?.Dispose();
            _startupLogWriter = null;
        }
    }

    private static void InitializeStartupLogging()
    {
        try
        {
            var startupDebugEnabled =
                string.Equals(Environment.GetEnvironmentVariable("VAANI_STARTUP_DEBUG"), "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Environment.GetEnvironmentVariable("VAANI_STARTUP_DEBUG"), "true", StringComparison.OrdinalIgnoreCase);

            if (!startupDebugEnabled)
            {
                return;
            }

            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vaani");
            Directory.CreateDirectory(logDir);

            var logPath = Path.Combine(logDir, "startup.log");
            _startupLogWriter = new StreamWriter(logPath, append: true)
            {
                AutoFlush = true
            };

            Console.SetOut(_startupLogWriter);
            Console.SetError(_startupLogWriter);
        }
        catch
        {
            // If file logging fails, continue app startup normally.
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

            // Try a few AppContext keys that have been observed in different runtimes
            object? activationObj = null;

            activationObj = AppContext.GetData("ActivationArguments.ActivationData");
            Console.WriteLine($"AppContext['ActivationArguments.ActivationData'] = {activationObj?.GetType().Name ?? "(null)"}");

            if (activationObj == null)
            {
                activationObj = AppContext.GetData("ActivationArguments");
                Console.WriteLine($"AppContext['ActivationArguments'] = {activationObj?.GetType().Name ?? "(null)"}");
            }

            if (activationObj == null)
            {
                activationObj = AppContext.GetData("ActivationData");
                Console.WriteLine($"AppContext['ActivationData'] = {activationObj?.GetType().Name ?? "(null)"}");
            }

            // If we have an object, try to extract string[] items from it
            string[]? activationData = null;

            if (activationObj is string[] sd)
            {
                activationData = sd;
            }
            else if (activationObj is string s)
            {
                activationData = new[] { s };
            }
            else if (activationObj != null)
            {
                // Try reflection to get ActivationData property (some runtimes expose an ActivationArguments object)
                var prop = activationObj.GetType().GetProperty("ActivationData");
                if (prop != null)
                {
                    var val = prop.GetValue(activationObj);
                    if (val is string[] sd2)
                    {
                        activationData = sd2;
                    }
                    else if (val is System.Collections.IEnumerable ie)
                    {
                        var list = new System.Collections.Generic.List<string>();
                        foreach (var item in ie)
                        {
                            if (item != null)
                                list.Add(item.ToString()!);
                        }
                        activationData = list.ToArray();
                    }
                }
            }

            if (activationData != null && activationData.Length > 0)
            {
                Console.WriteLine($"AppContext ActivationData: {activationData.Length} items");
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
                    else
                    {
                        // If not a full URI, try to treat it as query string
                        var trimmed = data.Trim();
                        if (trimmed.StartsWith("?")) trimmed = trimmed.TrimStart('?');
                        var meetingId = ParseQueryParameter(trimmed, "meetingId");
                        if (!string.IsNullOrWhiteSpace(meetingId))
                        {
                            Console.WriteLine($"✓ ClickOnce meetingId detected in AppContext item: {meetingId}");
                            return meetingId.Trim();
                        }
                    }
                }
            }

            // Method 2: Check environment variables with common ClickOnce keys
            Console.WriteLine("Checking environment variables...");
            var envKeys = new[] { "ClickOnce_ActivationUrl", "CLICKONCE_ACTIVATIONURL", "ActivationUrl", "AppActivationArguments", "APPX_ACTIVATION_ARGS", "ActivationArguments" };
            foreach (var key in envKeys)
            {
                var envActivationUrl = Environment.GetEnvironmentVariable(key);
                Console.WriteLine($"{key} env: {envActivationUrl ?? "(null)"}");
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
                    else
                    {
                        // Not a URI, try parse as query string
                        var meetingId = ParseQueryParameter(envActivationUrl, "meetingId");
                        if (!string.IsNullOrWhiteSpace(meetingId))
                        {
                            Console.WriteLine($"✓ ClickOnce meetingId from env detected (query): {meetingId}");
                            return meetingId.Trim();
                        }
                    }
                }
            }

            // Method 3: Scan all environment variables for anything related to activation
            Console.WriteLine("Checking all environment variables for activation info...");
            foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
            {
                var key = env.Key?.ToString() ?? "";
                if (key.Contains("Click", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("Activation", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("Deploy", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("APPX", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  {key} = {env.Value}");

                    var val = env.Value?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        // try parse uri
                        if (Uri.TryCreate(val, UriKind.Absolute, out var uri2))
                        {
                            var meetingId = ParseQueryParameter(uri2.Query.TrimStart('?'), "meetingId");
                            if (!string.IsNullOrWhiteSpace(meetingId))
                                return meetingId.Trim();
                        }

                        // try parse raw string
                        var meetingId2 = ParseQueryParameter(val, "meetingId");
                        if (!string.IsNullOrWhiteSpace(meetingId2))
                            return meetingId2.Trim();
                    }
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

    private static bool TryExtractMeetingIdFromUrl(string? rawUrl, out string meetingId)
    {
        meetingId = string.Empty;

        if (string.IsNullOrWhiteSpace(rawUrl))
            return false;

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return false;

        if (string.IsNullOrWhiteSpace(uri.Query))
            return false;

        var query = uri.Query.TrimStart('?');
        var parsed = ParseQueryParameter(query, "meetingId");
        if (string.IsNullOrWhiteSpace(parsed))
            return false;

        meetingId = parsed.Trim();
        return true;
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

    private static string? GetMeetingIdFromRawCommandLine()
    {
        try
        {
            var raw = Environment.CommandLine;
            Console.WriteLine($"Raw command line: {raw}");

            // Try direct extraction first: ?meetingId=123 or &meetingId=123
            var direct = Regex.Match(raw, "(?:\\?|&|\\s)meetingId=([^&\\s\"']+)", RegexOptions.IgnoreCase);
            if (direct.Success)
            {
                var value = Uri.UnescapeDataString(direct.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    Console.WriteLine($"✓ MeetingId found in raw command line: {value}");
                    return value.Trim();
                }
            }

            // Some launchers encode query parts, so decode and retry
            var decoded = Uri.UnescapeDataString(raw);
            var encodedMatch = Regex.Match(decoded, "(?:\\?|&|\\s)meetingId=([^&\\s\"']+)", RegexOptions.IgnoreCase);
            if (encodedMatch.Success)
            {
                var value = Uri.UnescapeDataString(encodedMatch.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    Console.WriteLine($"✓ MeetingId found in decoded command line: {value}");
                    return value.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Raw command line parsing failed: {ex.Message}");
        }

        return null;
    }

    private static void HandleSecondaryActivation(string latestMeetingId)
    {
        if (string.IsNullOrWhiteSpace(latestMeetingId))
            return;

        MeetingId = latestMeetingId.Trim();
        Console.WriteLine($"Received activation meetingId from secondary launch: {MeetingId}");
        MeetingIdUpdated?.Invoke(MeetingId);

        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var loginWindow = desktop.Windows?.FirstOrDefault(w => w is MeetingLoginWindow) as MeetingLoginWindow;

            if (loginWindow?.DataContext is MeetingLoginViewModel loginVm)
            {
                loginVm.MeetingId = MeetingId;
            }

            if (loginWindow != null)
            {
                if (!loginWindow.IsVisible)
                {
                    try { loginWindow.Show(); } catch { }
                }

                loginWindow.Activate();
            }
        });
    }
}