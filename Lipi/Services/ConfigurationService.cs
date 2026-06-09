using System.Reflection;
using System.Text.Json;

namespace Lipi.Services;

public class ConfigurationService
{
    private static ConfigurationService? _instance;
    private static readonly object _lock = new();
    private AppConfiguration _config;

    private ConfigurationService()
    {
        _config = LoadConfiguration();
    }

    public static ConfigurationService Instance
    {
        get
        {
            if (_instance != null)
                return _instance;

            lock (_lock)
            {
                _instance ??= new ConfigurationService();
            }

            return _instance;
        }
    }

    public AppConfiguration Config => _config;

    /// <summary>
    /// Re-reads appsettings.json from disk and updates the live config.
    /// Call this whenever ApiBaseUrl or other settings may have changed.
    /// </summary>
    public void Reload()
    {
        _config = LoadConfiguration();
    }

    /// <summary>
    /// Reloads from disk and returns the updated config in one call.
    /// </summary>
    public AppConfiguration ReloadAndGet()
    {
        Reload();
        return _config;
    }

    private static AppConfiguration LoadConfiguration()
    {
        // 1. Disk file wins — lets users place an appsettings.json next to
        //    the EXE to override config without rebuilding.
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (File.Exists(configPath))
        {
            try
            {
                var diskJson = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<AppConfiguration>(diskJson) ?? new AppConfiguration();
            }
            catch
            {
                // Corrupt disk file — fall through to embedded resource
            }
        }

        // 2. Embedded resource — this is where appsettings.json lives when
        //    published as PublishSingleFile=true with <EmbeddedResource>.
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var embeddedJson = reader.ReadToEnd();
                    return JsonSerializer.Deserialize<AppConfiguration>(embeddedJson) ?? new AppConfiguration();
                }
            }
            catch
            {
                // Corrupt embedded resource — fall through to defaults
            }
        }

        return new AppConfiguration();
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
    public int DirectPullQueueCapacitySeconds { get; set; } = 6;
    public int DirectMaxRecognizerRestarts { get; set; } = 5;
    public int DirectRecognizerRestartDelayMs { get; set; } = 500;
    public int DirectMaxQuotaRestarts { get; set; } = 3;
    public int DirectQuotaRetryBaseDelayMs { get; set; } = 3000;
}

public class AuthenticationConfig
{
    public string ApiBaseUrl { get; set; } = "";
    public int ApiTimeout { get; set; } = 30;
}