using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Services;

public class ConversationalRewriteFallbackService : IConversationalRewriteFallbackService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConversationalRewriteFallbackService> _logger;

    public ConversationalRewriteFallbackService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ConversationalRewriteFallbackService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LipiDirectConversationalRewriteResponse> RewriteAsync(
        string sourceLanguage,
        string? domain,
        string? originalText,
        IReadOnlyDictionary<string, string> translations,
        CancellationToken cancellationToken = default)
    {
        var fallbackSection = _configuration.GetSection("ConversationalRewriteFallback");
        var enabled = fallbackSection.GetValue<bool>("Enabled");
        if (!enabled)
        {
            return Noop(translations, "Fallback rewrite is disabled.");
        }

        var endpoint = fallbackSection["Endpoint"];
        var apiKey = fallbackSection["ApiKey"];
        var model = fallbackSection["Model"] ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Conversational rewrite fallback is enabled but endpoint/api key is not configured.");
            return Noop(translations, "Fallback rewrite is not configured.");
        }

        try
        {
            var payload = BuildRequest(sourceLanguage, domain, originalText, translations, model);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Conversational rewrite fallback returned HTTP {StatusCode}: {Body}", (int)response.StatusCode, body);
                return Noop(translations, "Fallback rewrite request failed.");
            }

            var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken);
            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                return Noop(translations, "Fallback rewrite returned empty response.");

            var rewritten = System.Text.Json.JsonSerializer.Deserialize<RewriteResultPayload>(content);
            if (rewritten?.Translations == null || rewritten.Translations.Count == 0)
                return Noop(translations, "Fallback rewrite returned no translations.");

            var applied = false;
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in translations)
            {
                if (rewritten.Translations.TryGetValue(pair.Key, out var rewrittenText) && !string.IsNullOrWhiteSpace(rewrittenText))
                {
                    var finalText = rewrittenText.Trim();
                    result[pair.Key] = finalText;
                    if (!finalText.Equals(pair.Value, StringComparison.Ordinal))
                        applied = true;
                }
                else
                {
                    result[pair.Key] = pair.Value;
                }
            }

            return new LipiDirectConversationalRewriteResponse
            {
                Success = true,
                Applied = applied,
                Message = applied ? "Fallback rewrite applied." : "Fallback rewrite made no changes.",
                Translations = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversational rewrite fallback failed.");
            return Noop(translations, "Fallback rewrite failed.");
        }
    }

    private static LipiDirectConversationalRewriteResponse Noop(IReadOnlyDictionary<string, string> translations, string message) =>
        new()
        {
            Success = true,
            Applied = false,
            Message = message,
            Translations = new Dictionary<string, string>(translations, StringComparer.OrdinalIgnoreCase)
        };

    private static ChatCompletionRequest BuildRequest(
        string sourceLanguage,
        string? domain,
        string? originalText,
        IReadOnlyDictionary<string, string> translations,
        string model)
    {
        var domainText = string.IsNullOrWhiteSpace(domain) ? "general" : domain.Trim();
        var original = string.IsNullOrWhiteSpace(originalText) ? "(not provided)" : originalText.Trim();
        var translationLines = string.Join("\n", translations.Select(t => $"- {t.Key}: {t.Value}"));

        return new ChatCompletionRequest
        {
            Model = model,
            Temperature = 0.2m,
            Messages =
            [
                new ChatMessage
                {
                    Role = "system",
                    Content = "You rewrite translated text into natural conversational wording. Preserve meaning exactly. Do not summarize. Do not add information. Keep names, numbers, and facts unchanged. Return only valid JSON in the shape {\"translations\":{\"locale\":\"text\"}}."
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = $"Source language: {sourceLanguage}\nDomain: {domainText}\nOriginal text: {original}\nRewrite these translations into natural conversational wording and return JSON only:\n{translationLines}"
                }
            ]
        };
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("temperature")]
        public decimal Temperature { get; set; }

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }

    private sealed class RewriteResultPayload
    {
        [JsonPropertyName("translations")]
        public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
