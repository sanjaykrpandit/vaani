using System;

namespace Vaani.Models;

public class TranslationSettings
{
    public string AzureSubscriptionKey { get; set; } = string.Empty;
    public string AzureRegion { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string SourceVoice { get; set; } = string.Empty;
    public string TargetVoice { get; set; } = string.Empty;

    // Meeting-based authentication properties
    public string? MeetingId { get; set; }
    public string? MeetingName { get; set; }
    public DateTime? SessionExpiresAt { get; set; }
    public bool IsFromMeetingSession { get; set; }

    /// <summary>
    /// Create TranslationSettings from meeting configuration
    /// </summary>
    public static TranslationSettings FromMeetingConfiguration(
        Authentication.Models.MeetingConfiguration config,
        bool isVendor = true)
    {
        var sourceConfig = isVendor ? config.TranslationConfig.VendorLanguage : config.TranslationConfig.OrganizerLanguage;
        var targetConfig = isVendor ? config.TranslationConfig.OrganizerLanguage : config.TranslationConfig.VendorLanguage;

        return new TranslationSettings
        {
            AzureSubscriptionKey = config.AzureConfig.SubscriptionKey,
            AzureRegion = config.AzureConfig.Region,
            SourceLanguage = sourceConfig.Code,
            TargetLanguage = targetConfig.Code,
            SourceVoice = sourceConfig.Voice,
            TargetVoice = targetConfig.Voice,
            MeetingId = config.MeetingId,
            MeetingName = config.MeetingName,
            SessionExpiresAt = config.TimeWindow.ValidUntil,
            IsFromMeetingSession = true
        };
    }
}
