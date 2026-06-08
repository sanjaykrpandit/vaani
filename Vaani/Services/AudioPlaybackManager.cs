using NAudio.CoreAudioApi;
using NAudio.Wave;
using vconsole.Services.Logger;

namespace Vaani.Services;

/// <summary>
/// Manages audio playback to different output devices (CABLE and physical speakers).
/// Awaitable playback with proper completion tracking via PlaybackStopped events.
/// </summary>
public static class AudioPlaybackManager
{
    private static TranslationLogger? _logger;

    public static void SetLogger(TranslationLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Plays audio data to a CABLE virtual audio device (awaitable).
    /// </summary>
    public static async Task PlayAudioToCableDeviceAsync(byte[] audioData, MMDevice? device, CancellationToken ct = default)
    {
        if (device == null)
        {
            _logger?.Warning(LogCategory.Playback, "[OUT] No CABLE device provided");
            return;
        }

        try
        {
            await PlayAudioInternalAsync(audioData, device, "[OUT] CABLE", ct, allowDefaultFallback: false);
        }
        catch (Exception ex)
        {
            _logger?.Error(LogCategory.Playback, $"[OUT] CABLE playback failed: {ex.GetType().Name} - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Plays audio data to a physical speaker device (awaitable).
    /// </summary>
    public static async Task PlayAudioToPhysicalSpeakerAsync(byte[] audioData, MMDevice? physicalSpeaker, CancellationToken ct = default)
    {
        try
        {
            if (physicalSpeaker != null)
            {
                await PlayAudioInternalAsync(audioData, physicalSpeaker, "[IN] Speaker", ct);
            }
            else
            {
                // Use default device
                await PlayAudioInternalAsync(audioData, null, "[IN] Speaker (default)", ct);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(LogCategory.Playback, $"[IN] Speaker playback failed: {ex.GetType().Name} - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Internal method that handles actual playback with fallback chain.
    /// Uses PlaybackStopped event for accurate completion detection.
    /// </summary>
    /// 

    private static async Task PlayAudioInternalAsync(byte[] audioData, MMDevice? device, string logPrefix, CancellationToken ct, bool allowDefaultFallback = true)
    {
        using var ms = new MemoryStream(audioData);
        using var rs = new RawSourceWaveStream(ms, new WaveFormat(
            AudioConfiguration.SampleRate,
            AudioConfiguration.BitsPerSample,
            AudioConfiguration.Channels));

        // Try specific device first.
        if (device != null)
        {
            try
            {
                await PlayWithWasapiAsync(rs, device, ct);
                _logger?.Debug(LogCategory.Playback, $"{logPrefix} playback completed");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warning(LogCategory.Playback, $"{logPrefix} WASAPI device failed: {ex.GetType().Name} - {ex.Message}");
                ms.Position = 0;
            }
        }

        if (!allowDefaultFallback)
        {
            _logger?.Warning(LogCategory.Playback, $"{logPrefix} skipping default speaker fallback (strict routing enabled)");
            return;
        }

        // Fallback 1: default WASAPI.
        try
        {
            await PlayWithDefaultWasapiAsync(rs, ct);
            _logger?.Debug(LogCategory.Playback, $"{logPrefix} playback completed (default WASAPI)");
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Warning(LogCategory.Playback, $"{logPrefix} default WASAPI failed: {ex.GetType().Name} - {ex.Message}");
            ms.Position = 0;
        }

        // Fallback 2: DirectSound.
        await PlayWithDirectSoundAsync(rs, ct);
        _logger?.Debug(LogCategory.Playback, $"{logPrefix} playback completed (DirectSound fallback)");
    }

    //private static async Task PlayAudioInternalAsync(byte[] audioData, MMDevice? device, string logPrefix, CancellationToken ct)
    //{
    //    using var ms = new MemoryStream(audioData);
    //    using var rs = new RawSourceWaveStream(ms, new WaveFormat(
    //        AudioConfiguration.SampleRate,
    //        AudioConfiguration.BitsPerSample,
    //        AudioConfiguration.Channels));

    //    // ✅ Try WASAPI with specific device
    //    if (device != null)
    //    {
    //        try
    //        {
    //            await PlayWithWasapiAsync(rs, device, ct);
    //            _logger?.Debug(LogCategory.Playback, $"{logPrefix} playback completed");
    //            return;
    //        }
    //        catch (OperationCanceledException)
    //        {
    //            throw;
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger?.Warning(LogCategory.Playback, $"{logPrefix} WASAPI failed: {ex.GetType().Name} - {ex.Message}");
    //            ms.Position = 0;
    //        }
    //    }

    //    // ✅ Fallback: Default WASAPI
    //    try
    //    {
    //        await PlayWithDefaultWasapiAsync(rs, ct);
    //        _logger?.Debug(LogCategory.Playback, $"{logPrefix} playback completed (default WASAPI)");
    //        return;
    //    }
    //    catch (OperationCanceledException)
    //    {
    //        throw;
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger?.Warning(LogCategory.Playback, $"{logPrefix} Default WASAPI failed: {ex.GetType().Name} - {ex.Message}");
    //        ms.Position = 0;
    //    }

    //    // ✅ Final fallback: DirectSound
    //    await PlayWithDirectSoundAsync(rs, ct);
    //    _logger?.Debug(LogCategory.Playback, $"{logPrefix} playback completed (DirectSound fallback)");
    //}

    private static async Task PlayWithWasapiAsync(RawSourceWaveStream stream, MMDevice device, CancellationToken ct)
    {
        using var waveOut = new WasapiOut(device, AudioClientShareMode.Shared, false, 5);
        var tcs = new TaskCompletionSource<bool>();

        waveOut.PlaybackStopped += (s, e) =>
        {
            if (e.Exception != null)
                tcs.TrySetException(e.Exception);
            else
                tcs.TrySetResult(true);
        };

        waveOut.Init(stream);
        waveOut.Play();

        using var reg = ct.Register(() =>
        {
            waveOut.Stop();
            tcs.TrySetCanceled(ct);
        });

        await tcs.Task;
    }

    private static async Task PlayWithDefaultWasapiAsync(RawSourceWaveStream stream, CancellationToken ct)
    {
        using var waveOut = new WasapiOut(AudioClientShareMode.Shared, 5);
        var tcs = new TaskCompletionSource<bool>();

        waveOut.PlaybackStopped += (s, e) =>
        {
            if (e.Exception != null)
                tcs.TrySetException(e.Exception);
            else
                tcs.TrySetResult(true);
        };

        waveOut.Init(stream);
        waveOut.Play();

        using var reg = ct.Register(() =>
        {
            waveOut.Stop();
            tcs.TrySetCanceled(ct);
        });

        await tcs.Task;
    }

    private static async Task PlayWithDirectSoundAsync(RawSourceWaveStream stream, CancellationToken ct)
    {
        using var dsOut = new DirectSoundOut();
        var tcs = new TaskCompletionSource<bool>();

        dsOut.PlaybackStopped += (s, e) =>
        {
            if (e.Exception != null)
                tcs.TrySetException(e.Exception);
            else
                tcs.TrySetResult(true);
        };

        dsOut.Init(stream);
        dsOut.Play();

        using var reg = ct.Register(() =>
        {
            dsOut.Stop();
            tcs.TrySetCanceled(ct);
        });

        await tcs.Task;
    }

    /// <summary>
    /// Calculates the duration of audio data in milliseconds.
    /// </summary>
    public static double CalculateAudioDuration(int audioDataLength, int sampleRate, int channels, int bitsPerSample)
    {
        var bytesPerSample = bitsPerSample / 8;
        var totalSamples = audioDataLength / (bytesPerSample * channels);
        return (totalSamples / (double)sampleRate) * 1000;
    }
}