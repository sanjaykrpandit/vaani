using System.ComponentModel.DataAnnotations;

namespace Vaani.API.Models.DTOs;

public class ConversationalDictionaryItemResponse
{
    public int Id { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string Domain { get; set; } = "general";
    public string FormalText { get; set; } = string.Empty;
    public string ConversationalText { get; set; } = string.Empty;
    public string MatchMode { get; set; } = "Contains";
    public bool IsActive { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateConversationalDictionaryRequest
{
    [Required]
    [MaxLength(20)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Domain { get; set; } = "general";

    [Required]
    [MaxLength(500)]
    public string FormalText { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string ConversationalText { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Exact|Contains|StartsWith)$", ErrorMessage = "MatchMode must be Exact, Contains, or StartsWith.")]
    public string MatchMode { get; set; } = "Contains";
}

public class UpdateConversationalDictionaryRequest
{
    [MaxLength(50)]
    public string Domain { get; set; } = "general";

    [Required]
    [MaxLength(500)]
    public string FormalText { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string ConversationalText { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Exact|Contains|StartsWith)$", ErrorMessage = "MatchMode must be Exact, Contains, or StartsWith.")]
    public string MatchMode { get; set; } = "Contains";

    public bool IsActive { get; set; } = true;
}

public class ConversationalDictionaryListResponse
{
    public bool Success { get; set; }
    public List<ConversationalDictionaryItemResponse> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public string? Message { get; set; }
}

public class ConversationalDictionaryActionResponse
{
    public bool Success { get; set; }
    public ConversationalDictionaryItemResponse? Item { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}
