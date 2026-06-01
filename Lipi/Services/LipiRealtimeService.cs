using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using Lipi.Models;
using Microsoft.AspNetCore.SignalR.Client;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Lipi.Services;

public class LipiRealtimeService : ILipiRealtimeClient
{
    private readonly AudioCaptureConfig _audioCaptureConfig;
    private readonly Queue<byte[]> _preSpeechBuffer = new();
    private readonly Queue<QueuedAudioChunk> _sendQueue = new();
    private readonly object _sendQueueLock = new();
    private readonly SemaphoreSlim _sendQueueSignal = new(0);
    private HubConnection? _connection;
    private WaveInEvent? _waveIn;
    private WasapiLoopbackCapture? _loopbackCapture;
    private MMDevice? _loopbackDevice;
    private string? _lipiSessionId;
    private CancellationTokenSource? _sendLoopCts;
    private Task? _sendLoopTask;
    private long _seq;
    private int? _currentInputDeviceNumber;
    private string? _currentOutputDeviceId;
    private AudioSourceMode _currentSourceMode = AudioSourceMode.Microphone;
    private int _remainingTrailingSilenceChunks;
    private bool _speechDetected;
    private long _capturedChunkCount;
    private long _sentChunkCount;
    private long _silenceDroppedChunkCount;
    private long _queueDroppedChunkCount;
    private long _disconnectedDroppedChunkCount;
    private long _preRollFlushedChunkCount;

    public LipiRealtimeService()
    {
        _audioCaptureConfig = ConfigurationService.Instance.Config.AudioCapture;
    }

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
        AudioCaptureSelection captureSelection)
    {
        if (_connection != null)
            await StopAsync();

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, o => o.AccessTokenProvider = () => Task.FromResult<string?>(sessionToken))
            .WithAutomaticReconnect()
            .Build();

        _connection.Reconnecting += error =>
        {
            ClearSendQueue("connection reconnecting");
            LogDiagnostics("SignalR reconnecting", error);
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            LogDiagnostics($"SignalR reconnected ({connectionId ?? "no-connection-id"})");
            return Task.CompletedTask;
        };

        _connection.Closed += error =>
        {
            ClearSendQueue("connection closed");
            LogDiagnostics("SignalR closed", error);
            return Task.CompletedTask;
        };

        _connection.On<object>("SessionStarted", payload =>
        {
            var json = JsonSerializer.Serialize(payload);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lipiSessionId", out var idEl))
            {
                _lipiSessionId = idEl.GetString();
                Interlocked.Exchange(ref _seq, 0);
                ResetSilenceFilterState();
                ResetDiagnostics();
                EnsureSendLoopStarted();
                try
                {
                    StartCapture(captureSelection);
                    RunningStateChanged?.Invoke(true);
                }
                catch (Exception ex)
                {
                    ErrorReceived?.Invoke($"Audio capture start failed: {ex.Message}");
                }
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
            ErrorReceived?.Invoke($"Server realtime error [{code}]: {message}");
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
        await StopSendLoopAsync();

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
        Interlocked.Exchange(ref _seq, 0);
        ClearSendQueue("stop requested");
        ResetSilenceFilterState();
        LogDiagnostics("Lipi realtime stopped");
        RunningStateChanged?.Invoke(false);
    }

    public Task SwitchCaptureDeviceAsync(AudioCaptureSelection captureSelection)
    {
        var sameSelection = _currentSourceMode == captureSelection.SourceMode
            && _currentInputDeviceNumber == captureSelection.InputDeviceNumber
            && string.Equals(_currentOutputDeviceId, captureSelection.OutputDeviceId, StringComparison.OrdinalIgnoreCase);

        if (sameSelection && ((_waveIn != null && captureSelection.SourceMode == AudioSourceMode.Microphone)
            || (_loopbackCapture != null && captureSelection.SourceMode == AudioSourceMode.Speaker)))
            return Task.CompletedTask;

        if (!string.IsNullOrWhiteSpace(_lipiSessionId))
            StartCapture(captureSelection);

        return Task.CompletedTask;
    }

    private void StartCapture(AudioCaptureSelection captureSelection)
    {
        StopCapture();

        _currentSourceMode = captureSelection.SourceMode;
        _currentInputDeviceNumber = captureSelection.InputDeviceNumber;
        _currentOutputDeviceId = captureSelection.OutputDeviceId;

        if (captureSelection.SourceMode == AudioSourceMode.Speaker)
        {
            StartLoopbackCapture(captureSelection.OutputDeviceId);
            return;
        }

        if (!captureSelection.InputDeviceNumber.HasValue)
            throw new InvalidOperationException("No microphone device selected.");

        _waveIn = new WaveInEvent
        {
            DeviceNumber = captureSelection.InputDeviceNumber.Value,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 100
        };

        _waveIn.DataAvailable += async (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(_lipiSessionId))
                return;

            var bytes = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, bytes, 0, e.BytesRecorded);
            Interlocked.Increment(ref _capturedChunkCount);

            try
            {
                var chunks = GetChunksToSend(bytes);
                if (chunks.Count == 0)
                {
                    Interlocked.Increment(ref _silenceDroppedChunkCount);
                    return;
                }

                if (_connection?.State != HubConnectionState.Connected)
                {
                    if (_audioCaptureConfig.DropAudioWhileDisconnected)
                    {
                        Interlocked.Add(ref _disconnectedDroppedChunkCount, chunks.Count);
                        return;
                    }
                }

                foreach (var chunk in chunks)
                {
                    EnqueueAudioChunk(chunk, DateTime.UtcNow);
                }
            }
            catch
            {
            }
        };

        _waveIn.StartRecording();
    }

    private void StartLoopbackCapture(string? outputDeviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        _loopbackDevice = string.IsNullOrWhiteSpace(outputDeviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
            : enumerator.GetDevice(outputDeviceId);

        _currentOutputDeviceId = _loopbackDevice.ID;
        _loopbackCapture = new WasapiLoopbackCapture(_loopbackDevice);
        var captureFormat = _loopbackCapture.WaveFormat;

        _loopbackCapture.DataAvailable += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(_lipiSessionId))
                return;

            var pcm = AudioPcmConverter.ToTarget16kHz(e.Buffer, e.BytesRecorded, captureFormat);
            if (pcm.Length == 0)
                return;

            Interlocked.Increment(ref _capturedChunkCount);

            try
            {
                var chunks = GetChunksToSend(pcm);
                if (chunks.Count == 0)
                {
                    Interlocked.Increment(ref _silenceDroppedChunkCount);
                    return;
                }

                if (_connection?.State != HubConnectionState.Connected && _audioCaptureConfig.DropAudioWhileDisconnected)
                {
                    Interlocked.Add(ref _disconnectedDroppedChunkCount, chunks.Count);
                    return;
                }

                foreach (var chunk in chunks)
                    EnqueueAudioChunk(chunk, DateTime.UtcNow);
            }
            catch
            {
            }
        };

        _loopbackCapture.StartRecording();
    }

    private void StopCapture()
    {
        try { _waveIn?.StopRecording(); } catch { }
        _waveIn?.Dispose();
        _waveIn = null;

        try { _loopbackCapture?.StopRecording(); } catch { }
        _loopbackCapture?.Dispose();
        _loopbackCapture = null;
        _loopbackDevice?.Dispose();
        _loopbackDevice = null;

        _currentInputDeviceNumber = null;
        _currentOutputDeviceId = null;
        _currentSourceMode = AudioSourceMode.Microphone;
        ResetSilenceFilterState();
    }

    private IReadOnlyList<byte[]> GetChunksToSend(byte[] bytes)
    {
        if (!_audioCaptureConfig.EnableSilenceFiltering)
            return [bytes];

        var rmsLevel = CalculateRootMeanSquareLevel(bytes);
        var threshold = Math.Max(0, _audioCaptureConfig.SilenceThresholdLevel);
        if (rmsLevel >= threshold)
        {
            var chunksToSend = new List<byte[]>(_preSpeechBuffer.Count + 1);
            if (!_speechDetected)
            {
                while (_preSpeechBuffer.Count > 0)
                {
                    chunksToSend.Add(_preSpeechBuffer.Dequeue());
                    Interlocked.Increment(ref _preRollFlushedChunkCount);
                }
            }

            _speechDetected = true;
            _remainingTrailingSilenceChunks = Math.Max(0, _audioCaptureConfig.TrailingSilenceChunks);
            chunksToSend.Add(bytes);
            return chunksToSend;
        }

        if (_speechDetected && _remainingTrailingSilenceChunks > 0)
        {
            _remainingTrailingSilenceChunks--;
            return [bytes];
        }

        if (_speechDetected)
            _speechDetected = false;

        BufferPreSpeechChunk(bytes);
        return [];
    }

    private void BufferPreSpeechChunk(byte[] bytes)
    {
        var maxChunks = Math.Max(0, _audioCaptureConfig.PreRollChunks);
        if (maxChunks == 0)
        {
            _preSpeechBuffer.Clear();
            return;
        }

        _preSpeechBuffer.Enqueue(bytes);
        while (_preSpeechBuffer.Count > maxChunks)
            _preSpeechBuffer.Dequeue();
    }

    private void ResetSilenceFilterState()
    {
        _remainingTrailingSilenceChunks = 0;
        _speechDetected = false;
        _preSpeechBuffer.Clear();
    }

    private void ResetDiagnostics()
    {
        Interlocked.Exchange(ref _capturedChunkCount, 0);
        Interlocked.Exchange(ref _sentChunkCount, 0);
        Interlocked.Exchange(ref _silenceDroppedChunkCount, 0);
        Interlocked.Exchange(ref _queueDroppedChunkCount, 0);
        Interlocked.Exchange(ref _disconnectedDroppedChunkCount, 0);
        Interlocked.Exchange(ref _preRollFlushedChunkCount, 0);
    }

    private void EnsureSendLoopStarted()
    {
        if (_sendLoopTask is { IsCompleted: false })
            return;

        _sendLoopCts?.Dispose();
        _sendLoopCts = new CancellationTokenSource();
        _sendLoopTask = Task.Run(() => RunSendLoopAsync(_sendLoopCts.Token));
    }

    private async Task StopSendLoopAsync()
    {
        if (_sendLoopCts == null)
            return;

        try { _sendLoopCts.Cancel(); } catch { }
        _sendQueueSignal.Release();

        if (_sendLoopTask != null)
        {
            try { await _sendLoopTask; } catch (OperationCanceledException) { } catch { }
        }

        _sendLoopTask = null;
        _sendLoopCts.Dispose();
        _sendLoopCts = null;
    }

    private async Task RunSendLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _sendQueueSignal.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            QueuedAudioChunk? queuedChunk;
            lock (_sendQueueLock)
            {
                queuedChunk = _sendQueue.Count > 0 ? _sendQueue.Dequeue() : null;
            }

            if (queuedChunk == null)
                continue;

            if (_connection?.State != HubConnectionState.Connected || string.IsNullOrWhiteSpace(_lipiSessionId))
            {
                Interlocked.Increment(ref _disconnectedDroppedChunkCount);
                continue;
            }

            try
            {
                await _connection.SendAsync("SendAudioChunk", new
                {
                    LipiSessionId = _lipiSessionId,
                    Data = queuedChunk.Data,
                    SequenceNumber = Interlocked.Increment(ref _seq),
                    CapturedAt = queuedChunk.CapturedAtUtc
                }, cancellationToken);

                var sent = Interlocked.Increment(ref _sentChunkCount);
                if (_audioCaptureConfig.EnableDiagnostics && sent % 100 == 0)
                    LogDiagnostics($"Sent {sent} audio chunks");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _disconnectedDroppedChunkCount);
                LogDiagnostics("Audio chunk send failed", ex);
            }
        }
    }

    private void EnqueueAudioChunk(byte[] bytes, DateTime capturedAtUtc)
    {
        lock (_sendQueueLock)
        {
            var capacity = Math.Max(1, _audioCaptureConfig.SendQueueCapacity);
            while (_sendQueue.Count >= capacity)
            {
                _sendQueue.Dequeue();
                Interlocked.Increment(ref _queueDroppedChunkCount);
            }

            _sendQueue.Enqueue(new QueuedAudioChunk(bytes, capturedAtUtc));
        }

        _sendQueueSignal.Release();
    }

    private void ClearSendQueue(string reason)
    {
        lock (_sendQueueLock)
        {
            if (_sendQueue.Count == 0)
                return;

            Interlocked.Add(ref _queueDroppedChunkCount, _sendQueue.Count);
            _sendQueue.Clear();
        }

        LogDiagnostics($"Cleared queued audio ({reason})");
    }

    private void LogDiagnostics(string message, Exception? exception = null)
    {
        if (!_audioCaptureConfig.EnableDiagnostics)
            return;

        var payload = $"[LipiRealtimeService] {message} | captured={Interlocked.Read(ref _capturedChunkCount)} sent={Interlocked.Read(ref _sentChunkCount)} silenceDropped={Interlocked.Read(ref _silenceDroppedChunkCount)} queueDropped={Interlocked.Read(ref _queueDroppedChunkCount)} disconnectedDropped={Interlocked.Read(ref _disconnectedDroppedChunkCount)} preRollFlushed={Interlocked.Read(ref _preRollFlushedChunkCount)}";
        if (exception != null)
            payload += $" | error={exception.Message}";

        Debug.WriteLine(payload);
        Trace.WriteLine(payload);
    }

    private static int CalculateRootMeanSquareLevel(byte[] bytes)
    {
        if (bytes.Length < 2)
            return 0;

        double totalSquares = 0;
        var sampleCount = 0;

        for (var i = 0; i <= bytes.Length - 2; i += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i, 2));
            totalSquares += (double)sample * sample;
            sampleCount++;
        }

        return sampleCount == 0 ? 0 : (int)Math.Sqrt(totalSquares / sampleCount);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private sealed record QueuedAudioChunk(byte[] Data, DateTime CapturedAtUtc);
}