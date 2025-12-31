using NAudio.Wave;

namespace Vaani.Services;

/// <summary>
/// Helper methods for audio device operations.
/// </summary>
public static class AudioDeviceHelper
{
    /// <summary>
    /// Gets the WaveIn device number for a specified device name.
    /// </summary>
    public static int GetWaveInDeviceNumber(string deviceName)
    {
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            if (caps.ProductName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }
}