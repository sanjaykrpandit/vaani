using Avalonia;
using System.Text.RegularExpressions;

namespace Lipi;

internal static class Program
{
    public static string? MeetingId { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        MeetingId = GetMeetingIdFromClickOnce()
                    ?? ParseMeetingIdFromArgs(args)
                    ?? GetMeetingIdFromRawCommandLine();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static string? GetMeetingIdFromClickOnce()
    {
        try
        {
            var activationData = AppContext.GetData("ActivationArguments.ActivationData") as string[];
            if (activationData is { Length: > 0 })
            {
                foreach (var data in activationData)
                {
                    if (TryExtractMeetingIdFromUrl(data, out var id))
                        return id;
                }
            }

            var knownVars = new[]
            {
                "ClickOnce_ActivationUri",
                "ClickOnce_ActivationData_0",
                "ClickOnce_UpdateLocation",
                "ClickOnce_ActivationUrl"
            };

            foreach (var envVar in knownVars)
            {
                var value = Environment.GetEnvironmentVariable(envVar);
                if (TryExtractMeetingIdFromUrl(value, out var id))
                    return id;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ParseMeetingIdFromArgs(string[] args)
    {
        if (args is not { Length: > 0 })
            return null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // 1) Direct regex pickup from any arg text (absolute/relative/raw)
            var match = Regex.Match(arg, @"(?:\?|&|^|[\s""'])meetingId=([^&\s""']+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var id = Uri.UnescapeDataString(match.Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }

            // 2) URL parse (absolute or relative)
            if (Uri.TryCreate(arg, UriKind.RelativeOrAbsolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Query))
            {
                var fromUrl = ParseQueryParameter(uri.Query.TrimStart('?'), "meetingId");
                if (!string.IsNullOrWhiteSpace(fromUrl))
                    return fromUrl.Trim();
            }

            // 3) meetingId=123 format
            var cleanArg = arg.TrimStart('?', '-', '/');
            if (cleanArg.StartsWith("meetingId=", StringComparison.OrdinalIgnoreCase))
            {
                var value = cleanArg["meetingId=".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            // 4) --meetingId 123 format
            if (cleanArg.Equals("meetingId", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var value = args[i + 1].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static string? GetMeetingIdFromRawCommandLine()
    {
        try
        {
            var raw = Environment.CommandLine;
            var direct = Regex.Match(raw, @"(?:\?|&|\s)meetingId=([^&\s""']+)", RegexOptions.IgnoreCase);
            if (direct.Success)
            {
                var value = Uri.UnescapeDataString(direct.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            var decoded = Uri.UnescapeDataString(raw);
            var encodedMatch = Regex.Match(decoded, @"(?:\?|&|\s)meetingId=([^&\s""']+)", RegexOptions.IgnoreCase);
            if (encodedMatch.Success)
            {
                var value = Uri.UnescapeDataString(encodedMatch.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }
        catch
        {
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

        var parsed = ParseQueryParameter(uri.Query.TrimStart('?'), "meetingId");
        if (string.IsNullOrWhiteSpace(parsed))
            return false;

        meetingId = parsed.Trim();
        return true;
    }

    private static string? ParseQueryParameter(string query, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var parameters = query.Split('&');
        foreach (var param in parameters)
        {
            var keyValue = param.Split('=', 2);
            if (keyValue.Length == 2 && keyValue[0].Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(keyValue[1]);
        }

        return null;
    }
}