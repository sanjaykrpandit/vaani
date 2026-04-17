using System.Text.Json;
using Lipi.Models;
using Microsoft.AspNetCore.SignalR.Client;
using NAudio.Wave;

namespace Lipi.Services;

public class LipiRealtimeService : IDisposable
{
    private HubConnection? _connection;
    private WaveInEvent? _waveIn;
    private string? _lipiSessionId;
    private long _seq;

    public event Action<string, Dictionary<string, string>>? RecognizingReceived;
    public event Action<string, Dictionary<string, string>>? RecognizedReceived;
    public event Action<string>? ErrorReceived;
    public event Action<bool>? RunningStateChanged;

    public async Task StartAsync(
        string hubUrl,
        string sessionToken,
        string meetingId,
        string sessionId,
        string sourceLanguage,
        IEnumerable<string> targetLanguages,
        int inputDeviceNumber)
    {
        if (_connection != null)
            await StopAsync();

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, o => o.AccessTokenProvider = () => Task.FromResult<string?>(sessionToken))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<object>("SessionStarted", payload =>
        {
            var json = JsonSerializer.Serialize(payload);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lipiSessionId", out var idEl))
            {
                _lipiSessionId = idEl.GetString();
                StartCapture(inputDeviceNumber);
                RunningStateChanged?.Invoke(true);
            }
        });

        _connection.On<object>("ReceiveLipiEvent", payload =>
        {
            var json = JsonSerializer.Serialize(payload);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var eventType = root.TryGetProperty("eventType", out var et) ? et.GetInt32() : 0;
            var originalText = root.TryGetProperty("originalText", out var ot) ? ot.GetString() ?? string.Empty : string.Empty;

            if (eventType == 1)
            {
                var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("translations", out var tr) && tr.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in tr.EnumerateObject())
                    {
                        var value = p.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            translations[p.Name] = value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(originalText))
                    RecognizingReceived?.Invoke(originalText, translations);
                return;
            }

            if (eventType == 2)
            {
                var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("translations", out var tr) && tr.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in tr.EnumerateObject())
                    {
                        var value = p.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            translations[p.Name] = value;
                    }
                }

                RecognizedReceived?.Invoke(originalText, translations);
                return;
            }

            if (eventType == 5)
            {
                var message = root.TryGetProperty("systemMessage", out var sm) ? sm.GetString() : "Unknown error";
                ErrorReceived?.Invoke(message ?? "Unknown error");
            }
        });

        _connection.On<string, string>("ReceiveError", (code, message) =>
        {
            ErrorReceived?.Invoke($"{code}: {message}");
        });

        _connection.On<string>("SessionStopped", _ =>
        {
            StopCapture();
            _lipiSessionId = null;
            RunningStateChanged?.Invoke(false);
        });

        await _connection.StartAsync();

        await _connection.InvokeAsync("StartLipi", new
        {
            MeetingId = meetingId,
            SessionId = sessionId,
            SourceLanguage = sourceLanguage,
            TargetLanguages = targetLanguages.ToList(),
            AudioFormat = "Raw16Khz16BitMonoPcm"
        });
    }

    public async Task StopAsync()
    {
        StopCapture();

        if (_connection != null)
        {
            if (!string.IsNullOrWhiteSpace(_lipiSessionId))
            {
                try { await _connection.InvokeAsync("StopLipi", _lipiSessionId); } catch { }
            }

            try { await _connection.StopAsync(); } catch { }
            await _connection.DisposeAsync();
        }

        _connection = null;
        _lipiSessionId = null;
        RunningStateChanged?.Invoke(false);
    }

    private void StartCapture(int inputDeviceNumber)
    {
        StopCapture();

        _waveIn = new WaveInEvent
        {
            DeviceNumber = inputDeviceNumber,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 100
        };

        _waveIn.DataAvailable += async (_, e) =>
        {
            if (_connection?.State != HubConnectionState.Connected || string.IsNullOrWhiteSpace(_lipiSessionId))
                return;

            var bytes = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, bytes, 0, e.BytesRecorded);

            try
            {
                await _connection.SendAsync("SendAudioChunk", new
                {
                    LipiSessionId = _lipiSessionId,
                    Data = bytes,
                    SequenceNumber = Interlocked.Increment(ref _seq),
                    CapturedAt = DateTime.UtcNow
                });
            }
            catch
            {
            }
        };

        _waveIn.StartRecording();
    }

    private void StopCapture()
    {
        try { _waveIn?.StopRecording(); } catch { }
        _waveIn?.Dispose();
        _waveIn = null;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}