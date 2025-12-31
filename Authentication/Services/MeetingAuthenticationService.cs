using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Vaani.Authentication.Models;

namespace Vaani.Authentication.Services;

/// <summary>
/// Service for communicating with the backend API for meeting authentication
/// </summary>
public class MeetingAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly EncryptionService _encryptionService;
    private readonly string _apiBaseUrl;

    // TODO: Set to false when backend API is ready
    private const bool USE_MOCK_MODE = true;
    
    public MeetingAuthenticationService(string? apiBaseUrl = null)
    {
        // Use provided URL or load from configuration
        _apiBaseUrl = apiBaseUrl ?? ConfigurationService.Instance.Config.Authentication.ApiBaseUrl;
        
        var timeout = ConfigurationService.Instance.Config.Authentication.ApiTimeout;
        
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_apiBaseUrl),
            Timeout = TimeSpan.FromSeconds(timeout)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"Vaani-Desktop/{GetAppVersion()}");
        
        _encryptionService = new EncryptionService();
    }

    /// <summary>
    /// Validate a meeting ID and retrieve encrypted configuration
    /// </summary>
    /// <param name="meetingId">Meeting ID provided by user</param>
    /// <param name="deviceId">Unique device identifier</param>
    /// <param name="deviceName">Human-readable device name</param>
    /// <returns>Validation response with encrypted configuration</returns>
    public async Task<MeetingValidationResponse> ValidateMeetingAsync(
        string meetingId,
        string deviceId,
        string deviceName)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
            throw new ArgumentException("Meeting ID cannot be empty", nameof(meetingId));

        // ============================================================
        // MOCK MODE - Remove/comment when API is ready
        // ============================================================
        if (USE_MOCK_MODE)
        {
            return await ValidateMeetingMockAsync(meetingId, deviceId, deviceName);
        }

        // ============================================================
        // REAL API CALL - Uncomment when backend is ready
        // ============================================================
        /*
        var request = new MeetingValidationRequest
        {
            MeetingId = meetingId.Trim().ToUpperInvariant(),
            DeviceId = deviceId,
            DeviceName = deviceName,
            AppVersion = GetAppVersion(),
            Timestamp = DateTime.UtcNow
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/meetings/validate", request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                
                // Try to parse error response
                try
                {
                    var errorResponse = JsonSerializer.Deserialize<MeetingValidationResponse>(errorContent);
                    if (errorResponse != null)
                        return errorResponse;
                }
                catch
                {
                    // If parsing fails, create generic error
                }

                return new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = $"HTTP_{(int)response.StatusCode}",
                    Message = $"Server returned error: {response.ReasonPhrase}"
                };
            }

            var validationResponse = await response.Content.ReadFromJsonAsync<MeetingValidationResponse>();
            
            if (validationResponse == null)
            {
                return new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "INVALID_RESPONSE",
                    Message = "Invalid response from server"
                };
            }

            return validationResponse;
        }
        catch (HttpRequestException ex)
        {
            return new MeetingValidationResponse
            {
                IsValid = false,
                ErrorCode = "NETWORK_ERROR",
                Message = $"Network error: {ex.Message}"
            };
        }
        catch (TaskCanceledException)
        {
            return new MeetingValidationResponse
            {
                IsValid = false,
                ErrorCode = "TIMEOUT",
                Message = "Request timed out. Please check your internet connection."
            };
        }
        catch (Exception ex)
        {
            return new MeetingValidationResponse
            {
                IsValid = false,
                ErrorCode = "UNKNOWN_ERROR",
                Message = $"Unexpected error: {ex.Message}"
            };
        }
        */

        return await ValidateMeetingMockAsync(meetingId, deviceId, deviceName);
    }

    /// <summary>
    /// Mock validation for development/testing (remove when API is ready)
    /// </summary>
    private async Task<MeetingValidationResponse> ValidateMeetingMockAsync(
        string meetingId,
        string deviceId,
        string deviceName)
    {
        // Simulate network delay
        await Task.Delay(1000);

        // Valid test meeting IDs
        var validMeetingIds = new[]
        {
            "VAANI-TEST-001",
            "VAANI-TEST-002",
            "VAANI-DEMO-123",
            "VM-2025-1220-A7B3"
        };

        var normalizedId = meetingId.Trim().ToUpperInvariant();

        if (!validMeetingIds.Contains(normalizedId))
        {
            return new MeetingValidationResponse
            {
                IsValid = false,
                ErrorCode = "MEETING_NOT_FOUND",
                Message = "Meeting ID not found. Try: VAANI-TEST-001"
            };
        }

        // Return successful validation with mock data
        return new MeetingValidationResponse
        {
            IsValid = true,
            MeetingName = GetMockMeetingName(normalizedId),
            EncryptedConfig = "MOCK_ENCRYPTED_CONFIG", // Not used in mock mode
            ValidUntil = DateTime.UtcNow.AddHours(2), // 2 hour session
            RemainingMinutes = 120,
            SessionToken = $"mock_session_token_{Guid.NewGuid():N}",
            Features = new MeetingFeatures
            {
                AllowReconnect = true,
                HeartbeatIntervalSeconds = 60,
                EnableLocalCache = false
            }
        };
    }

    /// <summary>
    /// Get mock meeting name based on ID
    /// </summary>
    private string GetMockMeetingName(string meetingId)
    {
        return meetingId switch
        {
            "VAANI-TEST-001" => "Test Meeting - English to Hindi",
            "VAANI-TEST-002" => "Test Meeting - Sales Call",
            "VAANI-DEMO-123" => "Demo Meeting - Product Presentation",
            "VM-2025-1220-A7B3" => "Vendor Discussion Meeting",
            _ => "Test Meeting"
        };
    }

    /// <summary>
    /// Decrypt the configuration received from the API
    /// Note: In production, the encryption key should be derived from API response
    /// For now, using a placeholder approach
    /// </summary>
    /// <param name="encryptedConfig">Encrypted configuration string</param>
    /// <param name="encryptionKey">Encryption key (typically embedded in response or derived)</param>
    /// <returns>Decrypted meeting configuration</returns>
    public MeetingConfiguration DecryptConfiguration(string encryptedConfig, byte[] encryptionKey)
    {
        return _encryptionService.DecryptConfiguration(encryptedConfig, encryptionKey);
    }

    /// <summary>
    /// Send heartbeat to keep session alive
    /// </summary>
    /// <param name="sessionToken">Session token from validation</param>
    /// <returns>True if heartbeat successful</returns>
    public async Task<bool> SendHeartbeatAsync(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return false;

        // MOCK MODE: Always return success
        if (USE_MOCK_MODE)
        {
            await Task.Delay(100);
            return true;
        }

        // REAL API CALL - Uncomment when backend is ready
        /*
        try
        {
            var request = new { timestamp = DateTime.UtcNow };
            
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sessions/heartbeat")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Add("Authorization", $"Bearer {sessionToken}");

            var response = await _httpClient.SendAsync(httpRequest);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
        */

        return true;
    }

    /// <summary>
    /// End the session gracefully
    /// </summary>
    /// <param name="sessionToken">Session token</param>
    /// <param name="statistics">Optional usage statistics</param>
    /// <returns>True if successfully ended</returns>
    public async Task<bool> EndSessionAsync(string sessionToken, object? statistics = null)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return false;

        // MOCK MODE: Always return success
        if (USE_MOCK_MODE)
        {
            await Task.Delay(100);
            return true;
        }

        // REAL API CALL - Uncomment when backend is ready
        /*
        try
        {
            var request = new
            {
                timestamp = DateTime.UtcNow,
                statistics = statistics ?? new { }
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sessions/end")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Add("Authorization", $"Bearer {sessionToken}");

            var response = await _httpClient.SendAsync(httpRequest);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
        */

        return true;
    }

    /// <summary>
    /// Get application version
    /// </summary>
    private string GetAppVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "1.0.0";
    }

    /// <summary>
    /// Get unique device identifier
    /// </summary>
    public static string GetDeviceId()
    {
        // Use machine name + user name as device identifier
        // In production, consider using more robust hardware-based identifier
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineName}_{userName}";
        
        // Generate consistent hash
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToBase64String(hashBytes)[..16]; // Take first 16 chars
    }

    /// <summary>
    /// Get human-readable device name
    /// </summary>
    public static string GetDeviceName()
    {
        return Environment.MachineName;
    }
}
