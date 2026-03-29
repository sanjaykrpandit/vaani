using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Hubs;

/// <summary>
/// SignalR hub for real-time bidirectional audio streaming and translation events.
/// 
/// Client → Server methods:
///   StartTranslation(request)      – initialise a translation session
///   SendAudioChunk(chunk)          – push a raw PCM audio chunk
///   ControlTranslation(request)    – mute/unmute/stop
///   StopTranslation(sessionId)     – gracefully stop a session
///
/// Server → Client methods:
///   ReceiveTranslationEvent(event) – translation/recognition/audio event
///   ReceiveError(code, message)    – error notification
///   SessionStopped(sessionId)      – teardown notification
/// </summary>
[Authorize]
public class TranslationHub : Hub
{
    private readonly ITranslationService _translationService;
    private readonly ILogger<TranslationHub> _logger;
    private readonly int _maxChunkBytes;

    public TranslationHub(
        ITranslationService translationService,
        ILogger<TranslationHub> logger,
        IConfiguration configuration)
    {
        _translationService = translationService;
        _logger = logger;
        // Default max audio chunk: 64 KB (≈ 2 seconds at 16kHz/16-bit/mono)
        _maxChunkBytes = configuration.GetValue<int>("Translation:MaxAudioChunkBytes", 65536);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Connection lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("TranslationHub: client connected {ConnId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("TranslationHub: client disconnected {ConnId} | Reason: {Ex}",
            Context.ConnectionId, exception?.Message ?? "clean");

        await _translationService.CleanupConnectionAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Client → Server methods
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Start a new translation session.</summary>
    public async Task StartTranslation(TranslationStartRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.MeetingId))
            {
                await SendError("INVALID_REQUEST", "MeetingId is required.");
                return;
            }

            // Attach current SignalR connection ID so service can route events back
            request.ConnectionId = Context.ConnectionId;

            // Extract raw JWT from Claims principal for downstream use
            var jwtToken = GetJwtToken();

            var response = await _translationService.StartSessionAsync(request, jwtToken);

            if (!response.Success)
            {
                await SendError(response.ErrorCode ?? "START_FAILED", response.Message ?? "Failed to start.");
                return;
            }

            // Register event callback: service emits → hub forwards to this connection
            RegisterEventCallback(response.TranslationSessionId!);

            await Clients.Caller.SendAsync("SessionStarted", response);

            _logger.LogInformation("Translation session started for connection {ConnId}: {SessionId}",
                Context.ConnectionId, response.TranslationSessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartTranslation error for connection {ConnId}", Context.ConnectionId);
            await SendError("INTERNAL_ERROR", "Unexpected error during session start.");
        }
    }

    /// <summary>Send an audio chunk for the active translation session.</summary>
    public async Task SendAudioChunk(AudioChunkDto chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk.TranslationSessionId))
        {
            await SendError("INVALID_CHUNK", "TranslationSessionId is required in chunk.");
            return;
        }

        // Enforce chunk size guard
        if (chunk.Data == null || chunk.Data.Length == 0)
            return;

        if (chunk.Data.Length > _maxChunkBytes)
        {
            await SendError("CHUNK_TOO_LARGE",
                $"Audio chunk exceeds max size of {_maxChunkBytes} bytes.");
            return;
        }

        // Throttled confirmation: seq 1 and every 100th chunk (~10 s at 100 ms intervals)
        if (chunk.SequenceNumber == 1 || chunk.SequenceNumber % 100 == 0)
            _logger.LogDebug("[{Id}] {Pipeline} chunk received — seq {Seq}, {Bytes}B",
                chunk.TranslationSessionId, chunk.Pipeline, chunk.SequenceNumber, chunk.Data.Length);

        await _translationService.ProcessAudioChunkAsync(chunk.TranslationSessionId, chunk);
    }

    /// <summary>Apply a control action (mute, unmute, stop) to an active session.</summary>
    public async Task ControlTranslation(TranslationControlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TranslationSessionId))
        {
            await SendError("INVALID_CONTROL", "TranslationSessionId is required.");
            return;
        }

        // ✅ Session ownership guard: reject if session is owned by a different connection
        if (!IsSessionOwnedByConnection(request.TranslationSessionId))
        {
            await SendError("SESSION_ACCESS_DENIED",
                "You are not authorised to control this translation session.");
            return;
        }

        switch (request.Action)
        {
            case TranslationControlAction.MuteMicrophone:
                await _translationService.SetMicrophoneMuteAsync(request.TranslationSessionId, true);
                break;
            case TranslationControlAction.UnmuteMicrophone:
                await _translationService.SetMicrophoneMuteAsync(request.TranslationSessionId, false);
                break;
            case TranslationControlAction.MuteSpeaker:
                await _translationService.SetSpeakerMuteAsync(request.TranslationSessionId, true);
                break;
            case TranslationControlAction.UnmuteSpeaker:
                await _translationService.SetSpeakerMuteAsync(request.TranslationSessionId, false);
                break;
            case TranslationControlAction.Stop:
                await StopTranslation(request.TranslationSessionId);
                break;
            default:
                await SendError("UNKNOWN_ACTION", $"Unknown action: {request.Action}");
                break;
        }
    }

    /// <summary>Gracefully stop a translation session.</summary>
    public async Task StopTranslation(string translationSessionId)
    {
        if (string.IsNullOrWhiteSpace(translationSessionId))
        {
            await SendError("INVALID_REQUEST", "TranslationSessionId is required.");
            return;
        }

        // ✅ Session ownership guard
        if (!IsSessionOwnedByConnection(translationSessionId))
        {
            await SendError("SESSION_ACCESS_DENIED",
                "You are not authorised to stop this translation session.");
            return;
        }

        var stopped = await _translationService.StopSessionAsync(translationSessionId);

        if (!stopped)
        {
            await SendError("SESSION_NOT_FOUND", $"No active session found: {translationSessionId}");
            return;
        }

        await Clients.Caller.SendAsync("SessionStopped", translationSessionId);
        _logger.LogInformation("Session stopped: {SessionId}", translationSessionId);
    }

    /// <summary>Get status of an active session.</summary>
    public async Task GetStatus(string translationSessionId)
    {
        var status = _translationService.GetStatus(translationSessionId);
        if (status == null)
        {
            await SendError("SESSION_NOT_FOUND", $"No active session: {translationSessionId}");
            return;
        }
        await Clients.Caller.SendAsync("StatusUpdate", status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void RegisterEventCallback(string translationSessionId)
    {
        // Use a weak reference via connectionId so disposed connections don't hold state
        var connectionId = Context.ConnectionId;
        var clients = Clients;

        _translationService.RegisterEventCallback(translationSessionId, async (evt) =>
        {
            try
            {
                await clients.Client(connectionId).SendAsync("ReceiveTranslationEvent", evt);
            }
            catch (Exception ex)
            {
                // Client may have disconnected; log and continue
                _ = ex; // suppress CS0168
            }
        });
    }

    private Task SendError(string code, string message) =>
        Clients.Caller.SendAsync("ReceiveError", code, message);

    /// <summary>
    /// Returns true when the session's status record indicates it was opened by this connection.
    /// Falls through (permissive) when the session is not found — StopSession handles that case.
    /// </summary>
    private bool IsSessionOwnedByConnection(string translationSessionId)
    {
        var status = _translationService.GetStatus(translationSessionId);
        if (status == null) return true; // let downstream report not-found
        return _translationService.IsOwnedByConnection(translationSessionId, Context.ConnectionId);
    }

    private string GetJwtToken()
    {
        // Extract from Authorization header first
        var httpCtx = Context.GetHttpContext();
        if (httpCtx != null &&
            httpCtx.Request.Headers.TryGetValue("Authorization", out var auth) &&
            auth.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth.ToString()["Bearer ".Length..].Trim();
        }

        // Fallback for WebSocket/SignalR query-token auth path
        if (httpCtx != null &&
            httpCtx.Request.Query.TryGetValue("access_token", out var accessToken) &&
            !string.IsNullOrWhiteSpace(accessToken))
        {
            return accessToken.ToString();
        }

        return string.Empty;
    }
}
