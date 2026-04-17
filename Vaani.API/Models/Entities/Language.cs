namespace Vaani.API.Models.Entities;

/// <summary>
/// Meeting entity representing a translation meeting session
/// </summary>
public class Language
{
    public int Id { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public string LanguageMaleNeural { get; set; } = string.Empty;
    public string LanguageFemaleNeural { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
