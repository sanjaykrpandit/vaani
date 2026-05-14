namespace Vaani.API.Models.Entities;

public class ConversationalDictionaryEntry
{
    public int Id { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string FormalText { get; set; } = string.Empty;
    public string ConversationalText { get; set; } = string.Empty;
    public string MatchMode { get; set; } = "Contains"; // Exact | Contains | StartsWith
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = "system";
    public string UpdatedBy { get; set; } = "system";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
