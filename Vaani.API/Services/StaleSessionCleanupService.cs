using Vaani.API.Interfaces;

namespace Vaani.API.Services;

/// <summary>
/// Background service that periodically sweeps for stale translation sessions and tears
/// them down, preventing Azure SDK resource leaks when clients disconnect without a
/// clean SignalR close (e.g. network drop, process kill).
///
/// A session is considered stale when:
///   - Its meeting's ValidUntil + 15-min grace has passed, OR
///   - The session has been Running for longer than StaleSessionTimeoutMinutes with no
///     audio written (future: wire LastAudioAt into TranslationSessionState).
/// </summary>
public class StaleSessionCleanupService : BackgroundService
{
    private readonly ITranslationService _translationService;
    private readonly ILogger<StaleSessionCleanupService> _logger;
    private readonly TimeSpan _interval;

    public StaleSessionCleanupService(
        ITranslationService translationService,
        ILogger<StaleSessionCleanupService> logger,
        IConfiguration configuration)
    {
        _translationService = translationService;
        _logger = logger;
        var intervalMinutes = configuration.GetValue<int>("Translation:StaleSessionTimeoutMinutes", 10);
        _interval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StaleSessionCleanupService started (interval: {Interval})", _interval);

        // Stagger the first run so startup isn't impacted
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupStaleSessionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stale session cleanup sweep");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CleanupStaleSessionsAsync()
    {
        var staleIds = _translationService.GetStaleSessionIds();
        if (staleIds.Count == 0) return;

        _logger.LogInformation("Cleaning up {Count} stale translation session(s)", staleIds.Count);

        foreach (var id in staleIds)
        {
            try
            {
                await _translationService.StopSessionAsync(id);
                _logger.LogInformation("Stale session torn down: {SessionId}", id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop stale session {SessionId}", id);
            }
        }
    }
}
