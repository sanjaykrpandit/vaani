using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

namespace Vaani.API.Services;

public class LipiDirectAccessService : ILipiDirectAccessService
{
    private static readonly TimeSpan SpeechTokenLifetime = TimeSpan.FromMinutes(9);
    private static readonly ConcurrentDictionary<string, DirectSessionLanguageScope> SessionLanguageScopes = new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<LipiDirectAccessService> _logger;
    private readonly TextModerationEngine _moderation;

    public LipiDirectAccessService(
        IServiceScopeFactory scopeFactory,
        IJwtTokenService jwtTokenService,
        ILogger<LipiDirectAccessService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
        _moderation = new TextModerationEngine(configuration, _logger);
    }

    public async Task<LipiDirectTokenResponse> GetDirectTokenAsync(LipiDirectTokenRequest request, string jwtToken)
    {
        try
        {
            var (isValid, tokenMeetingId, tokenDeviceId) = _jwtTokenService.ValidateToken(jwtToken);
            if (!isValid || string.IsNullOrWhiteSpace(tokenMeetingId) || string.IsNullOrWhiteSpace(tokenDeviceId))
                return FailToken("INVALID_TOKEN", "Invalid or expired access token.");

            if (!tokenMeetingId.Equals(request.MeetingId, StringComparison.OrdinalIgnoreCase))
                return FailToken("TOKEN_MEETING_MISMATCH", "Token does not match requested meeting.");

            if (string.IsNullOrWhiteSpace(request.SessionId) || !int.TryParse(request.SessionId, out var sessionId))
                return FailToken("INVALID_REQUEST", "SessionId is required.");

            if (request.TargetLanguages == null || request.TargetLanguages.Count == 0)
                return FailToken("INVALID_REQUEST", "At least one target language is required.");

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<VaaniDbContext>();

            var meeting = await dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .FirstOrDefaultAsync(m => m.MeetingId == request.MeetingId.ToUpperInvariant());

            var session = await dbContext.Sessions
                .FirstOrDefaultAsync(s => s.Id == sessionId
                                       && s.MeetingId == request.MeetingId.ToUpperInvariant()
                                       && s.DeviceId == tokenDeviceId
                                       && (s.Status == "Active" || s.Status == "Initial"));

            if (session == null)
                return FailToken("SESSION_ACCESS_DENIED", "Session does not belong to token context.");

            if (meeting == null || !meeting.IsActive)
                return FailToken("MEETING_NOT_FOUND", "Meeting not found or inactive.");

            if (meeting.AzureSubscription == null || !meeting.AzureSubscription.IsActive)
                return FailToken("NO_SUBSCRIPTION", "No active Azure subscription assigned to this meeting.");

            var speechToken = await IssueSpeechTokenAsync(meeting.AzureSubscription.Region, meeting.AzureSubscription.SubscriptionKey);
            var sourceLanguage = string.IsNullOrWhiteSpace(request.SourceLanguage) ? "en-US" : request.SourceLanguage.Trim();
            var targetLanguages = request.TargetLanguages
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var scopeKey = BuildScopeKey(request.MeetingId, request.SessionId, tokenDeviceId);
            SessionLanguageScopes[scopeKey] = new DirectSessionLanguageScope(
                sourceLanguage,
                targetLanguages,
                DateTime.UtcNow.Add(SpeechTokenLifetime).AddMinutes(5));
            PruneExpiredLanguageScopes();

            return new LipiDirectTokenResponse
            {
                Success = true,
                SpeechToken = speechToken,
                Region = meeting.AzureSubscription.Region,
                SourceLanguage = sourceLanguage,
                TargetLanguages = targetLanguages,
                ExpiresAtUtc = DateTime.UtcNow.Add(SpeechTokenLifetime),
                Message = "Direct Azure access granted."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed issuing direct Lipi token for meeting {MeetingId}", request.MeetingId);
            return FailToken("INTERNAL_ERROR", "Failed to issue direct Azure token.");
        }
    }

    public async Task<LipiDirectTranscriptBatchResponse> PersistTranscriptBatchAsync(LipiDirectTranscriptBatchRequest request, string jwtToken)
    {
        try
        {
            var (isValid, tokenMeetingId, tokenDeviceId) = _jwtTokenService.ValidateToken(jwtToken);
            if (!isValid || string.IsNullOrWhiteSpace(tokenMeetingId) || string.IsNullOrWhiteSpace(tokenDeviceId))
                return FailTranscript("INVALID_TOKEN", "Invalid or expired access token.");

            if (!tokenMeetingId.Equals(request.MeetingId, StringComparison.OrdinalIgnoreCase))
                return FailTranscript("TOKEN_MEETING_MISMATCH", "Token does not match requested meeting.");

            if (string.IsNullOrWhiteSpace(request.SessionId) || !int.TryParse(request.SessionId, out var sessionId))
                return FailTranscript("INVALID_REQUEST", "SessionId is required.");

            if (request.Entries == null || request.Entries.Count == 0)
                return new LipiDirectTranscriptBatchResponse { Success = true, Message = "No transcript entries to persist." };

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<VaaniDbContext>();
            var session = await dbContext.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId
                                                                         && s.MeetingId == request.MeetingId.ToUpperInvariant()
                                                                         && s.DeviceId == tokenDeviceId);
            if (session == null)
                return FailTranscript("SESSION_ACCESS_DENIED", "Session does not belong to token context.");

            var moderatedEntries = request.Entries
                .OrderBy(e => e.RecognizedAtUtc)
                .Select(e => ModerateTranscriptEntry(e, ResolveSourceLanguage(request, tokenDeviceId)))
                .Where(e => e != null)
                .Cast<(string Transcript, Dictionary<string, string> Translations, DateTime RecognizedAtUtc)>()
                .ToList();

            if (moderatedEntries.Count == 0)
                return new LipiDirectTranscriptBatchResponse { Success = true, Message = "All transcript entries were filtered by content policy." };

            var payload = string.Join(Environment.NewLine,
                moderatedEntries.Select(entry => BuildTranscriptEntryText(entry.Transcript, entry.Translations, entry.RecognizedAtUtc)));

            session.SessionTrascript = string.IsNullOrWhiteSpace(session.SessionTrascript)
                ? payload
                : $"{session.SessionTrascript}{Environment.NewLine}{payload}";

            await dbContext.SaveChangesAsync();
            return new LipiDirectTranscriptBatchResponse { Success = true, Message = "Transcript batch persisted." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed persisting direct Lipi transcripts for meeting {MeetingId}", request.MeetingId);
            return FailTranscript("INTERNAL_ERROR", "Failed to persist transcript batch.");
        }
    }

    private static LipiDirectTokenResponse FailToken(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };

    private static LipiDirectTranscriptBatchResponse FailTranscript(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };

    private (string Transcript, Dictionary<string, string> Translations, DateTime RecognizedAtUtc)? ModerateTranscriptEntry(
        LipiDirectTranscriptEntryDto entry,
        string? sourceLanguage)
    {
        var moderatedOriginal = _moderation.ModerateText(
            entry.OriginalText ?? string.Empty,
            string.IsNullOrWhiteSpace(sourceLanguage) ? null : [sourceLanguage]);
        if (moderatedOriginal.Blocked)
            return null;

        var moderatedTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in entry.Translations)
        {
            var moderated = _moderation.ModerateText(pair.Value ?? string.Empty, [pair.Key]);
            if (moderated.Blocked)
                return null;

            if (!string.IsNullOrWhiteSpace(moderated.Text))
                moderatedTranslations[pair.Key] = moderated.Text;
        }

        if (string.IsNullOrWhiteSpace(moderatedOriginal.Text) && moderatedTranslations.Count == 0)
            return null;

        return (moderatedOriginal.Text, moderatedTranslations, entry.RecognizedAtUtc);
    }

    private string? ResolveSourceLanguage(LipiDirectTranscriptBatchRequest request, string deviceId)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceLanguage))
            return request.SourceLanguage.Trim();

        var scopeKey = BuildScopeKey(request.MeetingId, request.SessionId, deviceId);
        if (SessionLanguageScopes.TryGetValue(scopeKey, out var scope) && scope.ExpiresAtUtc > DateTime.UtcNow)
            return scope.SourceLanguage;

        return null;
    }

    private static string BuildScopeKey(string meetingId, string sessionId, string deviceId)
        => $"{meetingId.Trim().ToUpperInvariant()}::{sessionId.Trim()}::{deviceId.Trim()}";

    private static void PruneExpiredLanguageScopes()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in SessionLanguageScopes)
        {
            if (pair.Value.ExpiresAtUtc <= now)
                SessionLanguageScopes.TryRemove(pair.Key, out _);
        }
    }

    private async Task<string> IssueSpeechTokenAsync(string region, string subscriptionKey)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{region}.api.cognitive.microsoft.com/sts/v1.0/issueToken")
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string BuildTranscriptEntryText(string transcript, Dictionary<string, string> translations, DateTime recognizedAtUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{recognizedAtUtc:yyyy-MM-dd HH:mm:ss} UTC]");
        sb.AppendLine(transcript);

        if (translations.Count > 0)
        {
            sb.AppendLine("Translations:");
            foreach (var item in translations.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"{item.Key}: {item.Value}");
        }

        return sb.ToString().TrimEnd();
    }

    private sealed record DirectSessionLanguageScope(
        string SourceLanguage,
        IReadOnlyList<string> TargetLanguages,
        DateTime ExpiresAtUtc);
}