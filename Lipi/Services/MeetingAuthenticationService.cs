using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Lipi.Models;

namespace Lipi.Services;

public class MeetingAuthenticationService
{
    private readonly HttpClient _httpClient;

    public MeetingAuthenticationService()
    {
        var cfg = ConfigurationService.Instance.Config.Authentication;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(cfg.ApiTimeout)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Lipi-Desktop/1.0.0");
    }

    // Always reads from config so any Reload() call is reflected immediately.
    public string ApiBaseUrl => ConfigurationService.Instance.Config.Authentication.ApiBaseUrl;

    private string BuildUrl(string path)
    {
        var baseUrl = ConfigurationService.Instance.Config.Authentication.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}{path}";
    }

    public async Task<MeetingValidationResponse> ValidateMeetingAsync(string meetingId, string userName, string? password)
    {
        var request = new MeetingValidationRequest
        {
            MeetingId = meetingId.Trim().ToUpperInvariant(),
            DeviceId = GetDeviceId(),
            DeviceName = GetDeviceName(),
            UserName = userName,
            AppVersion = "1.0.0",
            Password = password
        };

        var response = await _httpClient.PostAsJsonAsync(BuildUrl("/api/meetings/validate"), request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            return new MeetingValidationResponse
            {
                IsValid = false,
                ErrorCode = $"HTTP_{(int)response.StatusCode}",
                Message = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body
            };
        }

        return await response.Content.ReadFromJsonAsync<MeetingValidationResponse>()
            ?? new MeetingValidationResponse { IsValid = false, ErrorCode = "INVALID_RESPONSE", Message = "Invalid server response." };
    }

    public async Task<SessionStartResponse> StartSessionAsync(string meetingId, string sessionToken)
    {
        var request = new
        {
            MeetingId = meetingId,
            DeviceId = GetDeviceId()
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/api/sessions/start"))
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {sessionToken}");

        var response = await _httpClient.SendAsync(httpRequest);
        if (!response.IsSuccessStatusCode)
            return new SessionStartResponse { Success = false, Message = "Failed to start session." };

        return await response.Content.ReadFromJsonAsync<SessionStartResponse>()
            ?? new SessionStartResponse { Success = false, Message = "Invalid start session response." };
    }

    public async Task<LipiDirectTokenResponse> GetDirectSpeechTokenAsync(
        string meetingId,
        string sessionId,
        string sourceLanguage,
        IEnumerable<string> targetLanguages,
        string sessionToken)
    {
        var request = new
        {
            MeetingId = meetingId,
            SessionId = sessionId,
            SourceLanguage = sourceLanguage,
            TargetLanguages = targetLanguages.ToList()
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/api/lipi/direct/token"))
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {sessionToken}");

        var response = await _httpClient.SendAsync(httpRequest);
        if (!response.IsSuccessStatusCode)
            return new LipiDirectTokenResponse { Success = false, Message = "Failed to acquire direct Azure token." };

        return await response.Content.ReadFromJsonAsync<LipiDirectTokenResponse>()
            ?? new LipiDirectTokenResponse { Success = false, Message = "Invalid direct token response." };
    }

    public async Task<LipiDirectTranscriptBatchResponse> SubmitDirectTranscriptBatchAsync(
        LipiDirectTranscriptBatchRequest request,
        string sessionToken)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/api/lipi/direct/transcripts"))
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {sessionToken}");

        var response = await _httpClient.SendAsync(httpRequest);
        if (!response.IsSuccessStatusCode)
            return new LipiDirectTranscriptBatchResponse { Success = false, Message = "Failed to persist direct transcript." };

        return await response.Content.ReadFromJsonAsync<LipiDirectTranscriptBatchResponse>()
            ?? new LipiDirectTranscriptBatchResponse { Success = false, Message = "Invalid transcript persistence response." };
    }

    public async Task<LipiDirectDictionaryResponse> GetConversationalDictionaryAsync(
        IReadOnlyList<string> languages,
        string? domain,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        var query = string.Join(",", languages
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => Uri.EscapeDataString(l.Trim())));

        var domainQuery = string.IsNullOrWhiteSpace(domain)
            ? string.Empty
            : $"&domain={Uri.EscapeDataString(domain.Trim())}";

        var httpRequest = new HttpRequestMessage(HttpMethod.Get, BuildUrl($"/api/lipi/direct/dictionary?languages={query}{domainQuery}"));
        httpRequest.Headers.Add("Authorization", $"Bearer {sessionToken}");

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new LipiDirectDictionaryResponse { Success = false, Message = "Failed to load conversational dictionary." };

        return await response.Content.ReadFromJsonAsync<LipiDirectDictionaryResponse>(cancellationToken: cancellationToken)
            ?? new LipiDirectDictionaryResponse { Success = false, Message = "Invalid dictionary response." };
    }

    public async Task<LipiDirectConversationalRewriteResponse> RewriteTranslationsAsync(
        LipiDirectConversationalRewriteRequest request,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/api/lipi/direct/rewrite"))
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {sessionToken}");

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new LipiDirectConversationalRewriteResponse
            {
                Success = false,
                Applied = false,
                Translations = new Dictionary<string, string>(request.Translations, StringComparer.OrdinalIgnoreCase),
                Message = "Failed to rewrite translations."
            };
        }

        return await response.Content.ReadFromJsonAsync<LipiDirectConversationalRewriteResponse>(cancellationToken: cancellationToken)
            ?? new LipiDirectConversationalRewriteResponse
            {
                Success = false,
                Applied = false,
                Translations = new Dictionary<string, string>(request.Translations, StringComparer.OrdinalIgnoreCase),
                Message = "Invalid rewrite response."
            };
    }

    public async Task<LipiDirectClientErrorReportResponse> ReportClientErrorAsync(
        LipiDirectClientErrorReportRequest request,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/api/lipi/direct/errors"))
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("Authorization", $"Bearer {sessionToken}");

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new LipiDirectClientErrorReportResponse
            {
                Success = false,
                ErrorCode = $"HTTP_{(int)response.StatusCode}",
                Message = "Failed to report client error."
            };
        }

        return await response.Content.ReadFromJsonAsync<LipiDirectClientErrorReportResponse>(cancellationToken: cancellationToken)
            ?? new LipiDirectClientErrorReportResponse
            {
                Success = false,
                ErrorCode = "INVALID_RESPONSE",
                Message = "Invalid client error response."
            };
    }

    public static string GetDeviceId()
    {
        var combined = $"{Environment.MachineName}_{Environment.UserName}";
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToBase64String(hashBytes)[..16];
    }

    public static string GetDeviceName() => $"{Environment.MachineName}-{Environment.UserName}";
}