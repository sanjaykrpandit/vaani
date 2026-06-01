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
    public AudioCaptureConfig AudioCapture { get; set; } = new();
    public RealtimeConfig Realtime { get; set; } = new();
}

public class AudioCaptureConfig
{
    public bool EnableSilenceFiltering { get; set; } = true;
    public int SilenceThresholdLevel { get; set; } = 450;
    public int PreRollChunks { get; set; } = 2;
    public int TrailingSilenceChunks { get; set; } = 4;
    public int SendQueueCapacity { get; set; } = 16;
    public bool DropAudioWhileDisconnected { get; set; } = true;
    public bool EnableDiagnostics { get; set; } = true;
}

public class RealtimeConfig
{
    public string DefaultConnectionMode { get; set; } = "Server";
    public int DirectTokenRefreshLeadSeconds { get; set; } = 90;
    public string DirectProfanityOption { get; set; } = "Removed";
    public int DirectSegmentationSilenceTimeoutMs { get; set; } = 700;
    public int DirectAudioBufferMilliseconds { get; set; } = 50;
    public bool DirectDisableClientSilenceFilter { get; set; } = true;
    public bool DirectEnableConversationalRewrite { get; set; } = true;
    public string DirectConversationalRewriteDomain { get; set; } = "general";
    public bool DirectGenericCleanupEnabled { get; set; } = true;
    public bool DirectAiFallbackEnabled { get; set; } = false;
    public bool DirectRecognizingRewriteEnabled { get; set; } = true;
    public int DirectRecognizingRewriteMinIntervalMs { get; set; } = 300;
    public int DirectRecognizingRewriteMinTextLength { get; set; } = 10;
}

public class AuthenticationConfig
{
    public string ApiBaseUrl { get; set; } = "https://localhost:7020";
    public int ApiTimeout { get; set; } = 30;
}