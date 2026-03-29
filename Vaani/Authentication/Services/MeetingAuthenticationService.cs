using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Vaani.Authentication.Models;
using Vaani.Services;

namespace Vaani.Authentication.Services;

/// <summary>
/// Service for communicating with the backend API for meeting authentication
/// </summary>
public class MeetingAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly EncryptionService _encryptionService;
    private readonly string _apiBaseUrl;  
    private readonly SecureStorageService _storage;
    private const string SessionStorageKey = "current_session";
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
        _storage = new SecureStorageService();
    }

    /// <summary>
    /// Validate a meeting ID and retrieve encrypted configuration
    /// </summary>
    /// <param name="meetingId">Meeting ID provided by user</param>
    /// <param name="deviceId">Unique device identifier</param>
    /// <param name="deviceName">Human-readable device name</param>
    /// <param name="userName">User's name</param>
    /// <param name="password">Meeting password (if required)</param>
    /// <returns>Validation response with encrypted configuration</returns>
    public async Task<MeetingValidationResponse> ValidateMeetingAsync(
        string meetingId,
        string deviceId,
        string deviceName,
        string userName,
        string? password = null)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
            throw new ArgumentException("Meeting ID cannot be empty", nameof(meetingId));
               

        var request = new MeetingValidationRequest
        {
            MeetingId = meetingId.Trim().ToUpperInvariant(),
            DeviceId = deviceId,
            DeviceName = deviceName,
            AppVersion = GetAppVersion(),
            UserName = userName,
            Password = password
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
        

       //return await ValidateMeetingMockAsync(meetingId, deviceId, deviceName);
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
    }


    /// <summary>
    /// Start session gracefully
    /// </summary>   
    public async Task<(bool success, string message, int? sessionId)> StartSessionAsync()
    {
        //return new MeetingSessionInfo();
        try
        {
            var session = LoadCurrentSession();
            if (session == null || session.IsExpired() || !session.IsActive)
                return (false,"Meeting has ended.", null);


            var _deviceid = session.DeviceId;
            var _meetingid = session.Configuration.MeetingId;
            var _token = session.SessionToken;

            if (string.IsNullOrWhiteSpace(_deviceid))
                return (false, "Invalid Device ID.", null);
            if (string.IsNullOrWhiteSpace(_meetingid))
                return (false, "Invalid Meeting ID.", null);
            if (string.IsNullOrWhiteSpace(_token))
                return (false, "Invalid Token.", null);

            var request = new MeetingValidationRequest
            {
                MeetingId = _meetingid,
                DeviceId = _deviceid,
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sessions/start")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Add("Authorization", $"Bearer {_token}");
            var response = await _httpClient.SendAsync(httpRequest);


            if (!response.IsSuccessStatusCode)
            {
               // var errorContent = await response.Content.ReadAsStringAsync();
                return (false, "Invalid response from server.", null);
            }

            var validationResponse = await response.Content.ReadFromJsonAsync<SessionValidationResponse>();
            if (validationResponse == null)
            {
                return (false, "Invalid response from server.", null);
            }

            if (validationResponse.Success && validationResponse.SessionId.HasValue)
            {
                session.SessionId = validationResponse.SessionId.Value;
                _storage.SaveSecure(SessionStorageKey, session);
            }

            return (validationResponse.Success, validationResponse.Message ?? string.Empty, validationResponse.SessionId);
        
        }
        catch
        {
            return (false, "Failed to start.", null);
        }
    }

    /// <summary>
    /// End the session gracefully
    /// </summary>
    /// <param name="sessionToken">Session token</param>
    /// <param name="statistics">Optional usage statistics</param>
    public async Task<bool> EndSessionAsync(string? sessionLog = null, string? sessionTranscript = null)
    {      
        var session = LoadCurrentSession();
        if (session == null || session.IsExpired() || !session.IsActive)
            return false;

        var _deviceid = session.DeviceId;
        var _meetingid = session.Configuration.MeetingId;
        var _token = session.SessionToken;

        if(string.IsNullOrWhiteSpace(_token))
            return false;
      

        try
        {
            var request = new
            {
                meetingId = _meetingid,
                deviceId = _deviceid,
                SessionLog = sessionLog ?? string.Empty,
                SessionTrascript = sessionTranscript ?? string.Empty
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sessions/end")
            {
                Content = JsonContent.Create(request)
            };

            httpRequest.Headers.Add("Authorization", $"Bearer {_token}");
            var response = await _httpClient.SendAsync(httpRequest);
            return response.IsSuccessStatusCode;
        }
        catch(Exception ex)
        {
            return false;
        }
        
    }

    /// <summary>
    /// Get application version
    /// </summary>
    private string GetAppVersion()
    {
       return AppVersionHelper.GetAppVersion();
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
        return Environment.MachineName + "-" + Environment.UserName;
    }

    private SessionInfo? LoadCurrentSession()
    {
        try
        {
            return _storage.LoadSecure<SessionInfo>(SessionStorageKey);
        }
        catch
        {
            return null;
        }
    }
}
