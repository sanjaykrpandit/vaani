namespace Vaani.API.Models.DTOs
{
    public class LanguageDto
    {
        public int Id { get; set; } = 0;
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
}