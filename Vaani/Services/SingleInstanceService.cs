using System;
using System.Threading;

namespace Vaani.Services;

/// <summary>
/// Service to ensure only one instance of the application runs at a time
/// </summary>
public class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private bool _hasHandle;
    private const string MutexName = "Global\\VaaniAudioTranscriptionApp_SingleInstance_F5E2A8B1";

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
}
