using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Vaani.Authentication.Services;

/// <summary>
/// Service for loading application configuration
/// </summary>
public class ConfigurationService
{
    private static ConfigurationService? _instance;
    private readonly AppConfiguration _config;

    private ConfigurationService()
    {
        _config = LoadConfiguration();
    }

    public static ConfigurationService Instance => _instance ??= new ConfigurationService();

    public AppConfiguration Config => _config;

    private AppConfiguration LoadConfiguration()
    {
        try
        {
            //var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");            
            //if (!File.Exists(configPath))
            //{
            //    throw new FileNotFoundException("Configuration file not found.", configPath);
            //}
            //var json = File.ReadAllText(configPath);
            //var config = JsonSerializer.Deserialize<AppConfiguration>(json);
            //if (config == null)
            //{
            //    throw new Exception("Configuration deserialization resulted in null.");
            //}
            //return config;

            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            string json;

            if (File.Exists(configPath))
            {
                json = File.ReadAllText(configPath);
            }
            else
            {
                var asm = Assembly.GetExecutingAssembly();
                var resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase));

                if (resourceName is null)
                    throw new FileNotFoundException("Configuration file not found in file system or embedded resources.");

                using var stream = asm.GetManifestResourceStream(resourceName)
                    ?? throw new Exception("Embedded appsettings.json stream is null.");
                using var reader = new StreamReader(stream);
                json = reader.ReadToEnd();
            }

            var config = JsonSerializer.Deserialize<AppConfiguration>(json)
                ?? throw new Exception("Configuration deserialization resulted in null.");
            return config;

        }
        catch
        {
           throw new Exception("Failed to load configuration.");
        }
    }
}

/// <summary>
/// Application configuration model
/// </summary>
public class AppConfiguration
{
    public AuthenticationConfig Authentication { get; set; } = new();
    public SessionConfig Session { get; set; } = new();
    public ApplicationConfig Application { get; set; } = new();
    public RealtimeConfig Realtime { get; set; } = new();
}

public class AuthenticationConfig
{
    public string? ApiBaseUrl { get; set; }
    public int ApiTimeout { get; set; }
}

public class SessionConfig
{
    public int HeartbeatIntervalSeconds { get; set; } = 60;
    public int ExpiryWarningMinutes { get; set; } = 5;
    public bool EnableLocalCache { get; set; } = false;
}

public class ApplicationConfig
{
    public string? Version { get; set; }
    public string? Environment { get; set; }
}

public class RealtimeConfig
{
    public string DefaultConnectionMode { get; set; } = "Server";
    public int DirectTokenRefreshLeadSeconds { get; set; } = 90;
    public int DirectSegmentationSilenceTimeoutMs { get; set; } = 500;
    public int DirectAudioBufferMilliseconds { get; set; } = 50;
}
