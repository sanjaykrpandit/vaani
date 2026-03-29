using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Vaani.Services;

/// <summary>
/// Service to ensure only one instance of the application runs at a time
/// </summary>
public class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private bool _hasHandle;
    private const string MutexName = "Global\\VaaniAudioTranscriptionApp_SingleInstance_F5E2A8B1";
    private const string ActivationPipeName = "VaaniAudioTranscriptionApp_ActivationPipe_F5E2A8B1";

    public SingleInstanceService()
    {
        _mutex = new Mutex(true, MutexName, out _hasHandle);
    }

    /// <summary>
    /// Checks if this is the first (only) instance of the application
    /// </summary>
    /// <returns>True if this is the first instance, false if another instance is already running</returns>
    public bool IsFirstInstance()
    {
        return _hasHandle;
    }

    /// <summary>
    /// Attempts to acquire the single instance lock
    /// </summary>
    /// <param name="timeoutMilliseconds">Timeout in milliseconds to wait for the lock</param>
    /// <returns>True if lock was acquired, false otherwise</returns>
    public bool TryAcquire(int timeoutMilliseconds = 0)
    {
        if (_hasHandle)
            return true;

        try
        {
            _hasHandle = _mutex.WaitOne(timeoutMilliseconds, false);
            return _hasHandle;
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed without releasing the mutex
            _hasHandle = true;
            return true;
        }
    }

    public void Dispose()
    {
        if (_hasHandle)
        {
            _mutex.ReleaseMutex();
            _hasHandle = false;
        }
        _mutex?.Dispose();
    }

    public static bool TryNotifyFirstInstance(string message, int timeoutMilliseconds = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out);
            client.Connect(timeoutMilliseconds);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message ?? string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static CancellationTokenSource StartActivationListener(Action<string> onMessage)
    {
        var cts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        ActivationPipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cts.Token);

                    using var reader = new StreamReader(server);
                    var payload = await reader.ReadLineAsync();

                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        onMessage(payload.Trim());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Keep listener alive for subsequent activations
                }
            }
        }, cts.Token);

        return cts;
    }
}
