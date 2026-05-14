namespace Vaani.API.Models.DTOs;

public class ConversationalDictionaryItemResponse
{
    public int Id { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
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
    public string LanguageCode { get; set; } = string.Empty;
    public string FormalText { get; set; } = string.Empty;
    public string ConversationalText { get; set; } = string.Empty;
    public string MatchMode { get; set; } = "Contains"; // Exact | Contains | StartsWith
}

public class UpdateConversationalDictionaryRequest
{
    public string FormalText { get; set; } = string.Empty;
    public string ConversationalText { get; set; } = string.Empty;
    public string MatchMode { get; set; } = "Contains";
    public bool IsActive { get; set; } = true;
}

public class ConversationalDictionaryListResponse
{
    public bool Success { get; set; }
    public List<ConversationalDictionaryItemResponse> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public string? Message { get; set; }
}

public class ConversationalDictionaryActionResponse
{
    public bool Success { get; set; }
    public ConversationalDictionaryItemResponse? Item { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}
