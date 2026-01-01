using System;
using System.IO;
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
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            
            if (!File.Exists(configPath))
            {
                // Return default configuration
                return GetDefaultConfiguration();
            }

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<AppConfiguration>(json);
            
            return config ?? GetDefaultConfiguration();
        }
        catch
        {
            // If config loading fails, return defaults
            return GetDefaultConfiguration();
        }
    }

    private AppConfiguration GetDefaultConfiguration()
    {
        return new AppConfiguration
        {
            Authentication = new AuthenticationConfig
            {
                ApiBaseUrl = "https://api.vaani.com",
                ApiTimeout = 30
            },
            Session = new SessionConfig
            {
                HeartbeatIntervalSeconds = 60,
                ExpiryWarningMinutes = 5,
                EnableLocalCache = false
            },
            Application = new ApplicationConfig
            {
                Version = "1.0.0",
                Environment = "Production"
            }
        };
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
}

public class AuthenticationConfig
{
    public string ApiBaseUrl { get; set; } = "https://api.vaani.com";
    public int ApiTimeout { get; set; } = 30;
}

public class SessionConfig
{
    public int HeartbeatIntervalSeconds { get; set; } = 60;
    public int ExpiryWarningMinutes { get; set; } = 5;
    public bool EnableLocalCache { get; set; } = false;
}

public class ApplicationConfig
{
    public string Version { get; set; } = "1.0.0";
    public string Environment { get; set; } = "Production";
}
