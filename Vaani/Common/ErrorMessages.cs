using System;
using System.Net.Http;
using System.Threading;

namespace Vaani.Common;

/// <summary>
/// Single source of truth for every user-facing error message in Vaani.
///
/// Rules:
///   - Raw exception messages NEVER leave this class.
///   - Every method returns plain, non-technical English.
///   - Add new cases here; never inline error text elsewhere.
///
/// Usage:
///   using static Vaani.Common.ErrorMessages;
///   ShowError(Classify(ex));
/// </summary>
public static class ErrorMessages
{
    // ─────────────────────────────────────────────────────────────────────────
    // Primary classifier — covers every exception type seen across the app
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps any exception to a plain-English user message.
    /// Covers login, meeting join, in-meeting network drops, device errors,
    /// SignalR hub errors, threading/concurrency faults, and serialisation issues.
    /// </summary>
    public static string Classify(Exception ex)
    {
        // Unwrap AggregateException so the real cause is inspected
        if (ex is AggregateException agg && agg.InnerException != null)
            return Classify(agg.InnerException);

        return ex switch
        {
            // ── Slow network / timeout ────────────────────────────────────────
            TaskCanceledException
            or OperationCanceledException
                => "The request timed out. Please check your internet connection and try again.",

            TimeoutException
                => "The connection timed out. Please check your internet connection and try again.",

            // ── HTTP / network ────────────────────────────────────────────────
            HttpRequestException httpEx when IsConnectionFailure(httpEx)
                => "Unable to reach the server. Please check your internet connection and try again.",

            HttpRequestException httpEx when IsServerError(httpEx)
                => "The server is temporarily unavailable. Please try again in a moment.",

            HttpRequestException
                => "A network error occurred. Please check your connection and try again.",

            // ── SignalR hub ───────────────────────────────────────────────────
            Microsoft.AspNetCore.SignalR.HubException hubEx
                => ClassifyHubException(hubEx),

            // ── Threading / concurrency ───────────────────────────────────────
            // Covers the "session thread exceeded" / semaphore fault seen on slow networks
            SemaphoreFullException
                => "The app is still processing a previous request. Please wait a moment and try again.",

            ThreadInterruptedException
                => "A background operation was interrupted. Please try again.",

            // ── Serialisation ─────────────────────────────────────────────────
            System.Text.Json.JsonException
                => "Received an unexpected response from the server. Please try again.",

            // ── IO / socket ───────────────────────────────────────────────────
            System.IO.IOException ioEx
                when ioEx.Message.Contains("socket", StringComparison.OrdinalIgnoreCase)
                || ex.InnerException is System.Net.Sockets.SocketException
                => "Lost connection to the server. Please check your network and try again.",

            System.IO.IOException
                => "A network read/write error occurred. Please try again.",

            // ── Auth / crypto ─────────────────────────────────────────────────
            UnauthorizedAccessException
                => "Authentication failed. Please re-enter your meeting details and try again.",

            InvalidOperationException invEx
                when invEx.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                || invEx.Message.Contains("decrypt", StringComparison.OrdinalIgnoreCase)
                => "Failed to process the meeting configuration. Please try again.",

            InvalidOperationException invEx
                when invEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
                || invEx.Message.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
                => "The translation connection was lost. Attempting to reconnect…",

            // ── Everything else ───────────────────────────────────────────────
            _ => "Something went wrong. Please try again. If the problem persists, restart the app."
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Audio device errors (WASAPI / COM / NAudio)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps audio-device exceptions (WASAPI/COM HRESULT, NAudio) to plain English.
    /// Prevents raw HRESULT codes like 0x88890003 reaching the UI.
    /// </summary>
    public static string ClassifyDevice(Exception ex)
    {
        if (ex is AggregateException agg && agg.InnerException != null)
            return ClassifyDevice(agg.InnerException);

        var msg = ex.Message;

        // WASAPI / COM HRESULT codes — e.g. AUDCLNT_E_DEVICE_IN_USE (0x88890001)
        if (msg.Contains("AUDCLNT", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("0x8")
            || ex.GetType().Name.Contains("COMException", StringComparison.OrdinalIgnoreCase))
            return "Audio device is busy or not responding. Please check your audio setup.";

        if (msg.Contains("format", StringComparison.OrdinalIgnoreCase))
            return "The audio device does not support the required format (16 kHz mono PCM).";

        if (msg.Contains("device", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("endpoint", StringComparison.OrdinalIgnoreCase))
            return "Audio device unavailable. Please check that the device is connected.";

        // Fall back to general classifier for network/threading causes
        return Classify(ex) == GenericFallback
            ? "An audio device error occurred. Please check your audio setup."
            : Classify(ex);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Backend error codes (from SignalR ReceiveError / API responses)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps backend hub / REST error codes to plain English.
    /// The <paramref name="rawMessage"/> is inspected only as a fallback keyword scan
    /// and is never forwarded directly to the UI.
    /// </summary>
    public static string MapBackendCode(string code, string rawMessage = "")
    {
        return code switch
        {
            "MAX_SESSIONS"
                => "The translation server has reached its session limit. Please try again later.",
            "MEETING_NOT_FOUND" or "NO_SUBSCRIPTION"
                => "Meeting configuration is invalid. Please re-join the meeting.",
            "SESSION_ACCESS_DENIED"
                => "Access denied. Your session may have expired — please restart translation.",
            "CHUNK_TOO_LARGE"
                => "Audio chunk rejected by server. Please contact support.",
            "INVALID_REQUEST" or "INVALID_CHUNK" or "INVALID_CONTROL"
                => "An invalid request was sent to the server. Please restart translation.",
            "INTERNAL_ERROR"
                => "The translation server encountered an internal error. Please try again.",
            "PIPELINE_ERROR"
                => "A translation pipeline error occurred. Translation may resume automatically.",
            "TIMEOUT" or "HTTP_408"
                => "The server took too long to respond. Please check your internet connection and try again.",
            "NETWORK_ERROR"
                => "Cannot reach the server. Please check your internet connection and try again.",
            "HTTP_503" or "HTTP_504"
                => "The server is temporarily unavailable. Please try again in a moment.",
            "UNKNOWN_ERROR"
                => "Something went wrong while contacting the server. Please try again.",
            // Keyword scan on raw message for unrecognised codes
            _ when rawMessage.Contains("semaphore", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("thread", StringComparison.OrdinalIgnoreCase)
                => "A server-side concurrency error occurred. Please restart translation.",
            _ when rawMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                => "The translation server request timed out.",
            _   => "A translation error occurred. Translation will attempt to recover automatically."
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Predicates used by callers to make routing decisions
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when an <see cref="HttpRequestException"/> indicates a transport-level
    /// failure (no route to host, DNS failure, socket refused, etc.).
    /// </summary>
    public static bool IsConnectionFailure(HttpRequestException ex)
    {
        var msg = ex.Message + (ex.InnerException?.Message ?? string.Empty);
        return msg.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("refused", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("unreachable", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("resolve", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("socket", StringComparison.OrdinalIgnoreCase)
            || ex.InnerException is System.Net.Sockets.SocketException;
    }

    /// <summary>
    /// Returns true when the HTTP status code indicates a server-side (5xx) error.
    /// </summary>
    public static bool IsServerError(HttpRequestException ex) =>
        ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500;

    /// <summary>
    /// Returns true for errors that are expected during a brief connection blip.
    /// These should be logged but NOT surface as UI notifications (avoids noise on short drops).
    /// </summary>
    public static bool IsTransient(Exception ex)
    {
        if (ex is OperationCanceledException or TaskCanceledException) return true;
        if (ex is InvalidOperationException inv &&
            inv.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    // Used to detect when Classify() returned the generic fallback so ClassifyDevice
    // can substitute a more specific device message.
    private const string GenericFallback =
        "Something went wrong. Please try again. If the problem persists, restart the app.";

    private static string ClassifyHubException(Microsoft.AspNetCore.SignalR.HubException ex)
    {
        var msg = ex.Message;
        if (msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) || msg.Contains("401"))
            return "Session expired or unauthorised. Please restart translation.";
        if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "The translation server timed out. Please check your connection.";
        return "The translation server reported an error. Please restart translation.";
    }
}
