using System.Text.Json;

namespace Lipi.Services;

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

    private static AppConfiguration LoadConfiguration()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
            return new AppConfiguration();

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
    }
}

public class AppConfiguration
{
    public AuthenticationConfig Authentication { get; set; } = new();
}

public class AuthenticationConfig
{
    public string ApiBaseUrl { get; set; } = "https://localhost:7020";
    public int ApiTimeout { get; set; } = 30;
}