using System;

namespace Vaani.Models;

public class TranslationSettings
{
    // ── Kept for backward compatibility during feature-flag transition ───────
    /// <summary>Set only in legacy direct-Azure mode. Will be removed after full cutover.</summary>
    public string? AzureSubscriptionKey { get; set; }
    /// <summary>Set only in legacy direct-Azure mode. Will be removed after full cutover.</summary>
    public string? AzureRegion { get; set; }

    // ── Backend translation service settings (new) ───────────────────────────
    /// <summary>SignalR hub URL, e.g. https://api.vaani.com/hubs/translation</summary>
    public string BackendTranslationHubUrl { get; set; } = string.Empty;

    /// <summary>When true the desktop uses BackendTranslationService instead of local TranslationService</summary>
    public bool UseBackendTranslation { get; set; } = true;

    // ── Language / voice config ──────────────────────────────────────────────
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string SourceVoice { get; set; } = string.Empty;
    public string TargetVoice { get; set; } = string.Empty;

    // ── Session metadata ─────────────────────────────────────────────────────
    public string? MeetingId { get; set; }
    public string? SessionId { get; set; }
    public string? MeetingName { get; set; }
    public string? SessionToken { get; set; }
    public DateTime? SessionExpiresAt { get; set; }
    public bool IsFromMeetingSession { get; set; }

    /// <summary>
    /// Create TranslationSettings from meeting configuration.
    /// Azure credentials are NOT included — they stay on the backend.
    /// </summary>
    public static TranslationSettings FromMeetingConfiguration(
        Authentication.Models.MeetingConfiguration config,
        bool isVendor = true,
        string? apiBaseUrl = null)
    {
        var sourceConfig = isVendor ? config.TranslationConfig.VendorLanguage : config.TranslationConfig.OrganizerLanguage;
        var targetConfig = isVendor ? config.TranslationConfig.OrganizerLanguage : config.TranslationConfig.VendorLanguage;

        var hubUrl = !string.IsNullOrWhiteSpace(config.BackendTranslationHubUrl)
            ? config.BackendTranslationHubUrl
            : $"{apiBaseUrl?.TrimEnd('/')}/hubs/translation";

        return new TranslationSettings
        {
            BackendTranslationHubUrl = hubUrl,
            UseBackendTranslation = true,
            SourceLanguage = sourceConfig.Code,
            TargetLanguage = targetConfig.Code,
            SourceVoice = sourceConfig.Voice,
            TargetVoice = targetConfig.Voice,
            MeetingId = config.MeetingId,
            MeetingName = config.MeetingName,
            SessionToken = config.SessionToken,
            SessionExpiresAt = config.TimeWindow.ValidUntil,
            IsFromMeetingSession = true
        };
    }
}
