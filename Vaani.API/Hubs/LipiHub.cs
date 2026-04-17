using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Hubs;

[Authorize]
public class LipiHub : Hub
{
    private readonly ILipiTranslationService _lipiService;
    private readonly ILogger<LipiHub> _logger;
    private readonly int _maxChunkBytes;

    public LipiHub(ILipiTranslationService lipiService, ILogger<LipiHub> logger, IConfiguration configuration)
    {
        _lipiService = lipiService;
        _logger = logger;
        _maxChunkBytes = configuration.GetValue<int>("Translation:MaxAudioChunkBytes", 65536);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _lipiService.CleanupConnectionAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task StartLipi(LipiStartRequest request)
    {
        try
        {
            request.ConnectionId = Context.ConnectionId;
            var jwtToken = GetJwtToken();
            var response = await _lipiService.StartSessionAsync(request, jwtToken);

            if (!response.Success)
            {
                await SendError(response.ErrorCode ?? "START_FAILED", response.Message ?? "Failed to start.");
                return;
            }

            RegisterEventCallback(response.LipiSessionId!);
            await Clients.Caller.SendAsync("SessionStarted", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartLipi error for connection {ConnId}", Context.ConnectionId);
            await SendError("INTERNAL_ERROR", "Unexpected error during Lipi session start.");
        }
    }

    public async Task SendAudioChunk(LipiAudioChunkDto chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk.LipiSessionId) || chunk.Data == null || chunk.Data.Length == 0)
            return;

        if (chunk.Data.Length > _maxChunkBytes)
        {
            await SendError("CHUNK_TOO_LARGE", $"Audio chunk exceeds max size of {_maxChunkBytes} bytes.");
            return;
        }

        await _lipiService.ProcessAudioChunkAsync(chunk.LipiSessionId, chunk);
    }

    public async Task StopLipi(string lipiSessionId)
    {
        if (string.IsNullOrWhiteSpace(lipiSessionId))
        {
            await SendError("INVALID_REQUEST", "LipiSessionId is required.");
            return;
        }

        if (!IsSessionOwnedByConnection(lipiSessionId))
        {
            await SendError("SESSION_ACCESS_DENIED", "You are not authorised to stop this Lipi session.");
            return;
        }

        var stopped = await _lipiService.StopSessionAsync(lipiSessionId);
        if (!stopped)
        {
            await SendError("SESSION_NOT_FOUND", $"No active session found: {lipiSessionId}");
            return;
        }

        await Clients.Caller.SendAsync("SessionStopped", lipiSessionId);
    }

    public async Task GetStatus(string lipiSessionId)
    {
        var status = _lipiService.GetStatus(lipiSessionId);
        if (status == null)
        {
            await SendError("SESSION_NOT_FOUND", $"No active session: {lipiSessionId}");
            return;
        }

        await Clients.Caller.SendAsync("StatusUpdate", status);
    }

    private void RegisterEventCallback(string lipiSessionId)
    {
        var connectionId = Context.ConnectionId;
        var clients = Clients;

        _lipiService.RegisterEventCallback(lipiSessionId, async evt =>
        {
            try
            {
                await clients.Client(connectionId).SendAsync("ReceiveLipiEvent", evt);
            }
            catch
            {
            }
        });
    }

    private Task SendError(string code, string message) =>
        Clients.Caller.SendAsync("ReceiveError", code, message);

    private bool IsSessionOwnedByConnection(string lipiSessionId)
    {
        var status = _lipiService.GetStatus(lipiSessionId);
        if (status == null) return true;
        return _lipiService.IsOwnedByConnection(lipiSessionId, Context.ConnectionId);
    }

    private string GetJwtToken()
    {
        var httpCtx = Context.GetHttpContext();
        if (httpCtx != null &&
            httpCtx.Request.Headers.TryGetValue("Authorization", out var auth) &&
            auth.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth.ToString()["Bearer ".Length..].Trim();
        }

        if (httpCtx != null &&
            httpCtx.Request.Query.TryGetValue("access_token", out var accessToken) &&
            !string.IsNullOrWhiteSpace(accessToken))
        {
            return accessToken.ToString();
        }

        return string.Empty;
    }
}