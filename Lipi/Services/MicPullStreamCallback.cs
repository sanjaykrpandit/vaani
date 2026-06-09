using System.Collections.Concurrent;
using Microsoft.CognitiveServices.Speech.Audio;

namespace Lipi.Services;

/// <summary>
/// Thread-safe PullAudioInputStreamCallback that paces the Azure Speech SDK
/// pull thread to the real-time NAudio capture rate.
///
/// KEY DESIGN RULE: Read() BLOCKS when the queue is empty, waking only when
/// NAudio delivers a real audio chunk via Enqueue(). This ensures Azure reads
/// at exactly real-time speed — no silence-padding race-ahead, no latency
/// accumulation.
///
/// Silence is written ONLY on a capture-stall timeout (200ms), not as routine
/// filler between chunks.
///
/// Overflow policy: oldest chunk is dropped when capacity is exceeded so
/// recent speech is never lost.
/// </summary>
public sealed class MicPullStreamCallback : PullAudioInputStreamCallback
{
    // Signals Read() that at least one chunk is waiting in the queue
    private readonly SemaphoreSlim _dataReady = new(0, int.MaxValue);
    private readonly ConcurrentQueue<byte[]> _queue = new();
    private readonly int _capacityBytes;
    private readonly Action? _onDropped;
    private int _queuedBytes;
    private byte[]? _remainder;
    private volatile bool _disposed;

    public int QueuedBytes => _queuedBytes;

    public MicPullStreamCallback(int capacityBytes, Action? onDropped = null)
    {
        _capacityBytes = capacityBytes > 0 ? capacityBytes : 32000 * 6; // default 6s
        _onDropped = onDropped;
    }

    /// <summary>
    /// Called from the NAudio DataAvailable thread. Thread-safe.
    /// Enqueues the chunk and unblocks any Read() waiting for data.
    /// </summary>
    public bool Enqueue(byte[] chunk)
    {
        if (_disposed || chunk.Length == 0)
            return false;

        // Evict oldest chunks when at capacity (keep recent speech)
        while (Interlocked.Add(ref _queuedBytes, 0) + chunk.Length > _capacityBytes)
        {
            if (_queue.TryDequeue(out var dropped))
            {
                Interlocked.Add(ref _queuedBytes, -dropped.Length);
                _onDropped?.Invoke();
            }
            else
                break;
        }

        _queue.Enqueue(chunk);
        Interlocked.Add(ref _queuedBytes, chunk.Length);

        // Wake up Read() if it is waiting
        _dataReady.Release();
        return true;
    }

    /// <summary>
    /// Called by the Azure Speech SDK on its internal pull thread.
    ///
    /// Blocks until real audio arrives from NAudio — this is what paces Azure
    /// to real-time and prevents latency accumulation.
    ///
    /// Returns 0 ONLY when disposed (end-of-stream signal that terminates the
    /// recognizer). Padding silence on a 200ms stall timeout keeps the session
    /// alive without racing ahead.
    /// </summary>
    public override int Read(byte[] dataBuffer, uint size)
    {
        if (_disposed)
            return 0;

        var written = 0;
        var target = (int)size;

        // Consume leftover bytes from a previous oversized chunk first
        if (_remainder != null)
        {
            var take = Math.Min(_remainder.Length, target);
            Buffer.BlockCopy(_remainder, 0, dataBuffer, 0, take);
            written = take;
            _remainder = take < _remainder.Length ? _remainder[take..] : null;
        }

        while (written < target && !_disposed)
        {
            if (_queue.TryDequeue(out var chunk))
            {
                Interlocked.Add(ref _queuedBytes, -chunk.Length);
                var take = Math.Min(chunk.Length, target - written);
                Buffer.BlockCopy(chunk, 0, dataBuffer, written, take);
                written += take;

                if (take < chunk.Length)
                    _remainder = chunk[take..];
            }
            else
            {
                // Queue is empty — BLOCK here until NAudio delivers the next chunk.
                // Timeout = 200ms (4× the 50ms capture interval).
                // On timeout: pad the remaining buffer with silence so Azure
                // keeps the session alive during a mic stall, then return.
                if (!_dataReady.Wait(200) && !_disposed)
                {
                    Array.Clear(dataBuffer, written, target - written);
                    written = target;
                }
            }
        }

        return _disposed ? 0 : written;
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        _remainder = null;
        while (_queue.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _queuedBytes, 0);
        // Release any thread currently blocked in Read() so it can exit
        try { _dataReady.Release(100); } catch { }
        base.Dispose(disposing);
    }
}

