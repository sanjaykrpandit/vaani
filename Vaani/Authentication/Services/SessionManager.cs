using System;
using System.Threading;
using System.Threading.Tasks;
using Vaani.Authentication.Models;

namespace Vaani.Authentication.Services;

/// <summary>
/// Service for managing the active meeting session lifecycle
/// </summary>
public class SessionManager
{
    private readonly SecureStorageService _storage;
    private readonly MeetingAuthenticationService _authService;
    private SessionInfo? _currentSession;
    private Timer? _heartbeatTimer;
    private Timer? _expiryCheckTimer;
    
    private const string SessionStorageKey = "current_session";
    
    // Events
    public event EventHandler? SessionExpired;
    public event EventHandler<TimeSpan>? SessionExpiring;
    public event EventHandler? HeartbeatFailed;

    // Warning thresholds
    private static readonly TimeSpan ExpiryWarningThreshold = TimeSpan.FromMinutes(5);
    private bool _expiryWarningShown = false;

    public SessionManager(
        SecureStorageService? storageService = null,
        MeetingAuthenticationService? authService = null)
    {
        _storage = storageService ?? new SecureStorageService();
        _authService = authService ?? new MeetingAuthenticationService();
    }

    /// <summary>
    /// Get the current active session
    /// </summary>
    public SessionInfo? CurrentSession => _currentSession;

    /// <summary>
    /// Check if there is an active valid session
    /// </summary>
    public bool HasActiveSession()
    {
        if (_currentSession == null)
            return false;

        if (_currentSession.IsExpired())
        {
            ClearSession();
            return false;
        }

        return _currentSession.IsActive;
    }

    /// <summary>
    /// Start a new session with validated configuration
    /// </summary>
    /// <param name="configuration">Meeting configuration</param>
    /// <param name="sessionToken">Session token from API</param>
    /// <param name="expiresAt">Session expiration time</param>
    public void StartSession(MeetingConfiguration configuration, string sessionToken, DateTime expiresAt)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Clear any existing session
        StopSession();

        _currentSession = new SessionInfo
        {
            Configuration = configuration,
            StartedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            SessionToken = sessionToken,
            DeviceId = MeetingAuthenticationService.GetDeviceId(),
            LastHeartbeat = DateTime.UtcNow,
            IsActive = true
        };

        // Save to secure storage
        SaveSession();

        // Start heartbeat if configured
        if (configuration.Features.HeartbeatIntervalSeconds > 0)
        {
            StartHeartbeat(TimeSpan.FromSeconds(configuration.Features.HeartbeatIntervalSeconds));
        }

        // Start expiry monitoring
        StartExpiryMonitoring();
    }

    /// <summary>
    /// Stop the current session
    /// </summary>
    //public async Task StopSessionAsync()
    //{
    //    if (_currentSession != null)
    //    {
    //        await _authService.EndSessionAsync();
    //    }
    //    StopSession();
    //}

    /// <summary>
    /// Stop session without API call (local only)
    /// </summary>
    private void StopSession()
    {
        StopHeartbeat();
        StopExpiryMonitoring();
        _currentSession = null;
        ClearStoredSession();
        _expiryWarningShown = false;
    }

    /// <summary>
    /// Load session from secure storage
    /// </summary>
    public bool LoadSession()
    {
        try
        {
            var session = _storage.LoadSecure<SessionInfo>(SessionStorageKey);
            
            if (session == null)
                return false;

            // Check if session is still valid
            if (session.IsExpired())
            {
                ClearStoredSession();
                return false;
            }

            _currentSession = session;
            
            // Restart monitoring
            if (session.Configuration.Features.HeartbeatIntervalSeconds > 0)
            {
                StartHeartbeat(TimeSpan.FromSeconds(session.Configuration.Features.HeartbeatIntervalSeconds));
            }
            StartExpiryMonitoring();

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Save current session to secure storage
    /// </summary>
    private void SaveSession()
    {
        if (_currentSession != null)
        {
            _storage.SaveSecure(SessionStorageKey, _currentSession);
        }
    }

    /// <summary>
    /// Clear session from storage
    /// </summary>
    private void ClearStoredSession()
    {
        _storage.Delete(SessionStorageKey);
    }

    /// <summary>
    /// Clear session completely
    /// </summary>
    public void ClearSession()
    {
        StopSession();
    }

    /// <summary>
    /// Get remaining time in session
    /// </summary>
    public TimeSpan GetRemainingTime()
    {
        return _currentSession?.GetRemainingTime() ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Start periodic heartbeat
    /// </summary>
    private void StartHeartbeat(TimeSpan interval)
    {
        StopHeartbeat();

        //_heartbeatTimer = new Timer(
        //    async _ => await SendHeartbeatAsync(),
        //    null,
        //    interval,
        //    interval
        //);
    }

    /// <summary>
    /// Stop heartbeat timer
    /// </summary>
    private void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    /// <summary>
    /// Send heartbeat to API
    /// </summary>
    private async Task SendHeartbeatAsync()
    {
        if (_currentSession == null || string.IsNullOrEmpty(_currentSession.SessionToken))
            return;

        try
        {
            var success = await _authService.SendHeartbeatAsync(_currentSession.SessionToken);
            
            if (success)
            {
                _currentSession.LastHeartbeat = DateTime.UtcNow;
                SaveSession();
            }
            else
            {
                HeartbeatFailed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            HeartbeatFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Start monitoring for session expiry
    /// </summary>
    private void StartExpiryMonitoring()
    {
        StopExpiryMonitoring();

        // Check every 120 seconds
        _expiryCheckTimer = new Timer(
            _ => CheckExpiry(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(120)
        );
    }

    /// <summary>
    /// Stop expiry monitoring
    /// </summary>
    private void StopExpiryMonitoring()
    {
        _expiryCheckTimer?.Dispose();
        _expiryCheckTimer = null;
    }

    /// <summary>
    /// Check if session is expired or expiring soon
    /// </summary>
    private void CheckExpiry()
    {
        if (_currentSession == null)
            return;

        if (_currentSession.IsExpired())
        {
            StopSession();
            SessionExpired?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Check if expiring soon
        if (!_expiryWarningShown && _currentSession.IsExpiringIn(ExpiryWarningThreshold))
        {
            _expiryWarningShown = true;
            var remainingTime = _currentSession.GetRemainingTime();
            SessionExpiring?.Invoke(this, remainingTime);
        }
    }

    /// <summary>
    /// Get session statistics for reporting
    /// </summary>
    public object GetSessionStatistics()
    {
        if (_currentSession == null)
            return new { };

        var duration = DateTime.UtcNow - _currentSession.StartedAt;
        
        return new
        {
            meetingId = _currentSession.Configuration.MeetingId,
            meetingName = _currentSession.Configuration.MeetingName,
            durationMinutes = (int)duration.TotalMinutes,
            startedAt = _currentSession.StartedAt,
            deviceId = _currentSession.DeviceId
        };
    }

    public (string deviceid, string meetingid, string token) GetSessionToken()
    {
        if (_currentSession == null)
            return ("", "", "");

        return (_currentSession.DeviceId, _currentSession.Configuration.MeetingId, _currentSession.SessionToken);

    }



    /// <summary>
    /// Logout and clear all session data
    /// This will end the session with the API and clear local storage
    /// </summary>
    public async Task LogoutAsync(bool isRunningTranslation=false)
    {
        // Get statistics before clearing session
        var statistics = GetSessionStatistics();

        // End session with API if active
        if (_currentSession != null && !string.IsNullOrEmpty(_currentSession.SessionToken))
        {
            if (isRunningTranslation)
            {
                await _authService.EndSessionAsync();
            }            
        }

        // Stop all timers and clear session data
        StopSession();
    }

    /// <summary>
    /// Clean up resources
    /// </summary>
    public void Dispose()
    {
        StopHeartbeat();
        StopExpiryMonitoring();
    }
}
