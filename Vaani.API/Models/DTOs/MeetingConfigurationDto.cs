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
