namespace vconsole.Services.Logger;

/// <summary>
/// Structured logger with filtering capabilities for translation service.
/// </summary>
public class TranslationLogger
{
    private readonly Action<string> _logAction;
    private LogLevel _minLogLevel;
    private HashSet<LogCategory>? _enabledCategories;
    private readonly object _lock = new();

    public TranslationLogger(Action<string> logAction, LogLevel minLogLevel = LogLevel.Info)
    {
        _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
        _minLogLevel = minLogLevel;
    }

    /// <summary>
    /// Sets the minimum log level. Messages below this level will be filtered out.
    /// </summary>
    public void SetMinLogLevel(LogLevel level)
    {
        lock (_lock)
        {
            _minLogLevel = level;
        }
    }

    /// <summary>
    /// Enables specific log categories. If null, all categories are enabled.
    /// </summary>
    public void SetEnabledCategories(params LogCategory[]? categories)
    {
        lock (_lock)
        {
            _enabledCategories = categories != null ? new HashSet<LogCategory>(categories) : null;
        }
    }

    /// <summary>
    /// Logs a message with the specified level and category.
    /// </summary>
    public void Log(LogLevel level, LogCategory category, string message)
    {
        if (!ShouldLog(level, category))
            return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var levelIcon = GetLevelIcon(level);
        var categoryPrefix = GetCategoryPrefix(category);
        
        _logAction($"[{timestamp}] {levelIcon} {categoryPrefix} {message}");
    }

    /// <summary>
    /// Logs a debug message (most verbose).
    /// </summary>
    public void Debug(LogCategory category, string message)
        => Log(LogLevel.Debug, category, message);

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    public void Info(LogCategory category, string message)
        => Log(LogLevel.Info, category, message);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public void Warning(LogCategory category, string message)
        => Log(LogLevel.Warning, category, message);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    public void Error(LogCategory category, string message)
        => Log(LogLevel.Error, category, message);

    /// <summary>
    /// Logs a critical error message.
    /// </summary>
    public void Critical(LogCategory category, string message)
        => Log(LogLevel.Critical, category, message);

    /// <summary>
    /// Legacy method for backward compatibility - logs as Info with System category.
    /// </summary>
    public void Log(string message)
        => Log(LogLevel.Info, LogCategory.System, message);

    private bool ShouldLog(LogLevel level, LogCategory category)
    {
        lock (_lock)
        {
            // Check log level
            if (level < _minLogLevel)
                return false;

            // Check category filter
            if (_enabledCategories != null && !_enabledCategories.Contains(category))
                return false;

            return true;
        }
    }

    private static string GetLevelIcon(LogLevel level) => level switch
    {
        LogLevel.Debug => "🔍",
        LogLevel.Info => "ℹ️",
        LogLevel.Warning => "⚠️",
        LogLevel.Error => "❌",
        LogLevel.Critical => "🚨",
        _ => "•"
    };

    private static string GetCategoryPrefix(LogCategory category) => category switch
    {
        LogCategory.System => "[SYS]",
        LogCategory.Outgoing => "[OUT]",
        LogCategory.Incoming => "[IN]",
        LogCategory.Recognition => "[REC]",
        LogCategory.Synthesis => "[SYN]",
        LogCategory.Playback => "[PLY]",
        LogCategory.AudioCapture => "[AUD]",
        LogCategory.Duplicate => "[DUP]",
        LogCategory.Metrics => "[MET]",
        LogCategory.Queue => "[QUE]",
        LogCategory.Device => "[DEV]",
        LogCategory.Connection => "[CON]",
        _ => "[???]"
    };
}
