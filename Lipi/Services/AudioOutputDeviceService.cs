using Lipi.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Lipi.Services;

public sealed class AudioOutputDeviceService : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Timer _refreshDebounceTimer;
    private bool _disposed;

    public AudioOutputDeviceService()
    {
        _refreshDebounceTimer = new Timer(_ => RaiseDevicesChanged(), null, Timeout.Infinite, Timeout.Infinite);

        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
        }
        catch
        {
        }
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var devices = new List<AudioOutputDevice>();
        string? defaultDeviceId = null;

        try
        {
            using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            defaultDeviceId = defaultDevice?.ID;
        }
        catch
        {
        }

        try
        {
            var renderDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in renderDevices)
            {
                var name = device.FriendlyName ?? string.Empty;
                devices.Add(new AudioOutputDevice
                {
                    Id = device.ID,
                    Name = name,
                    IsDefault = string.Equals(device.ID, defaultDeviceId, StringComparison.OrdinalIgnoreCase),
                    IsBluetoothOrHeadset = IsPreferredOutputDevice(name)
                });

                device.Dispose();
            }
        }
        catch
        {
        }

        return devices;
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => ScheduleRefresh();

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => ScheduleRefresh();

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => ScheduleRefresh();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render)
            ScheduleRefresh();
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => ScheduleRefresh();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch
        {
        }

        _refreshDebounceTimer.Dispose();
        _enumerator.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ScheduleRefresh()
    {
        if (_disposed)
            return;

        _refreshDebounceTimer.Change(TimeSpan.FromMilliseconds(600), Timeout.InfiniteTimeSpan);
    }

    private void RaiseDevicesChanged()
    {
        if (_disposed)
            return;

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsPreferredOutputDevice(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Headset", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Headphone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Earbud", StringComparison.OrdinalIgnoreCase)
            || name.Contains("AirPods", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pods", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Jabra", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Bose", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sony", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Speaker", StringComparison.OrdinalIgnoreCase)
            || name.Contains("USB", StringComparison.OrdinalIgnoreCase);
    }
}
