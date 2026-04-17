using System;
using Vaani.Services;

namespace Vaani.Authentication.Models;

/// <summary>
/// Decrypted meeting configuration containing all necessary details for translation
/// </summary>
public class MeetingConfiguration
{
    /// <summary>
    /// Unique meeting identifier
    /// </summary>
    public string MeetingId { get; set; } = string.Empty;

    /// <summary>
    /// Meeting display name
    /// </summary>
    public string MeetingName { get; set; } = string.Empty;

    /// <summary>
    /// Azure Cognitive Services configuration
    /// </summary>
    public AzureConfiguration AzureConfig { get; set; } = new();

    /// <summary>
    /// Translation configuration for the meeting
    /// </summary>
    public TranslationConfiguration TranslationConfig { get; set; } = new();

    /// <summary>
    /// Time window during which the meeting is valid
    /// </summary>
    public TimeWindow TimeWindow { get; set; } = new();

    /// <summary>
    /// Feature flags for this meeting
    /// </summary>
    public MeetingFeatures Features { get; set; } = new();

    /// <summary>
    /// Session token for API communication
    /// </summary>
    public string SessionToken { get; set; } = string.Empty;

    /// <summary>
    /// SignalR hub URL for backend translation (replaces direct Azure credentials)
    /// e.g. https://api.vaani.com/hubs/translation
    /// </summary>
    public string BackendTranslationHubUrl { get; set; } = string.Empty;

    /// <summary>
    /// Metadata about the configuration
    /// </summary>
    public ConfigurationMetadata Metadata { get; set; } = new();

    public List<Voice> AvailableVoices { get; set; } = new();
    public List<LanguageInfo> AvailableLanguages { get; set; } = new();
}

/// <summary>
/// Azure Cognitive Services credentials and endpoint.
/// DEPRECATED: No longer sent to client. Azure credentials are now stored on server only.
/// Retained for backward-compatibility deserialization only; fields will be empty strings.
/// </summary>
public class AzureConfiguration
{
    /// <summary>Deprecated — always empty in backend-translation mode.</summary>
    [Obsolete("Azure credentials are no longer sent to the client. Use BackendTranslationHubUrl instead.")]
    public string SubscriptionKey { get; set; } = string.Empty;

    /// <summary>Deprecated — always empty in backend-translation mode.</summary>
    [Obsolete("Azure credentials are no longer sent to the client. Use BackendTranslationHubUrl instead.")]
    public string Region { get; set; } = string.Empty;
}

/// <summary>
/// Translation settings for both directions
/// </summary>
public class TranslationConfiguration
{
    /// <summary>
    /// Vendor's language configuration (typically the participant)
    /// </summary>
    public LanguageConfiguration VendorLanguage { get; set; } = new();

    /// <summary>
    /// Organizer's language configuration
    /// </summary>
    public LanguageConfiguration OrganizerLanguage { get; set; } = new();
}

/// <summary>
/// Language and voice settings for one direction
/// </summary>
public class LanguageConfiguration
{
    /// <summary>
    /// Language code (e.g., en-US, hi-IN)
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Azure Neural TTS voice name (e.g., en-US-GuyNeural)
    /// </summary>
    public string Voice { get; set; } = string.Empty;
}

/// <summary>
/// Time window for meeting validity
/// </summary>
public class TimeWindow
{
    /// <summary>
    /// UTC timestamp when meeting access starts (including grace period)
    /// </summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>
    /// UTC timestamp when meeting access expires (including grace period)
    /// </summary>
    public DateTime ValidUntil { get; set; }

    /// <summary>
    /// Check if the current time is within the valid window
    /// </summary>
    public bool IsCurrentlyValid()
    {
        var now = DateTime.UtcNow;
        return now >= ValidFrom && now <= ValidUntil;
    }

    /// <summary>
    /// Get remaining time until expiration
    /// </summary>
    public TimeSpan GetRemainingTime()
    {
        return ValidUntil - DateTime.UtcNow;
    }
}

/// <summary>
/// Metadata about the configuration
/// </summary>
public class ConfigurationMetadata
{
    /// <summary>
    /// API version used for encryption
    /// </summary>
    public string ApiVersion { get; set; } = "v1";

    /// <summary>
    /// UTC timestamp when configuration was encrypted
    /// </summary>
    public DateTime EncryptedAt { get; set; }

    /// <summary>
    /// Configuration format version
    /// </summary>
    public int ConfigVersion { get; set; } = 1;
}
