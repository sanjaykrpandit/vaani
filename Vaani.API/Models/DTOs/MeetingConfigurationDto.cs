namespace Vaani.API.Models.DTOs;

/// <summary>
/// Meeting configuration DTO (before encryption)
/// </summary>
public class MeetingConfigurationDto
{
    public string MeetingId { get; set; } = string.Empty;
    public string MeetingName { get; set; } = string.Empty;
    public AzureConfigDto AzureConfig { get; set; } = new();
    public TranslationConfigDto TranslationConfig { get; set; } = new();
    public TimeWindowDto TimeWindow { get; set; } = new();
    public MeetingFeaturesDto Features { get; set; } = new();
    public ConfigMetadataDto Metadata { get; set; } = new();
    public List<Voice> AvailableVoices { get; set; } = new();
    public List<LanguageInfo> AvailableLanguages { get; set; } = new();

    /// <summary>
    /// SignalR hub URL embedded in the encrypted config.
    /// Desktop reads this to connect to the backend translation service.
    /// </summary>
    public string BackendTranslationHubUrl { get; set; } = string.Empty;
}

public class LanguageInfo
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
public class AzureConfigDto
{
    public string SubscriptionKey { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}

public class TranslationConfigDto
{
    public LanguageConfigDto VendorLanguage { get; set; } = new();
    public LanguageConfigDto OrganizerLanguage { get; set; } = new();
}

public class LanguageConfigDto
{
    public string Code { get; set; } = string.Empty;
    public string Voice { get; set; } = string.Empty;
}

public class TimeWindowDto
{
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
}

public class ConfigMetadataDto
{
    public string ApiVersion { get; set; } = "v1";
    public DateTime EncryptedAt { get; set; } = DateTime.UtcNow;
    public int ConfigVersion { get; set; } = 1;
}
public class Voice
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LanguageCode { get; set; } = "";
    public string Gender { get; set; } = "";
}