using System.Runtime.InteropServices;
using Lipi.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Lipi.Services;

public sealed class AudioInputDeviceService : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Timer _refreshDebounceTimer;
    private bool _disposed;

    public AudioInputDeviceService()
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

    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var devices = new List<AudioInputDevice>();
        string? defaultDeviceId = null;

        try
        {
            using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            defaultDeviceId = defaultDevice?.ID;
        }
        catch
        {
        }

        var waveInDevices = GetWaveInDevices();

        try
        {
            var captureDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in captureDevices)
            {
                var name = device.FriendlyName ?? string.Empty;
                devices.Add(new AudioInputDevice
                {
                    Id = device.ID,
                    Name = name,
                    DeviceNumber = ResolveWaveInDeviceNumber(name, waveInDevices),
                    IsDefault = string.Equals(device.ID, defaultDeviceId, StringComparison.OrdinalIgnoreCase),
                    IsBluetoothOrHeadset = IsPreferredHeadsetDevice(name)
                });

                device.Dispose();
            }
        }
        catch
        {
        }

        return devices;
    }

    public int? ResolveInputDeviceNumber(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        return GetInputDevices()
            .FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase))
            ?.DeviceNumber;
    }

    public bool TrySetDefaultInputDevice(string captureDeviceId)
    {
        if (string.IsNullOrWhiteSpace(captureDeviceId))
            return false;

        object? comObject = null;
        try
        {
            comObject = new PolicyConfigClient();
            var policyConfig = (IPolicyConfig)comObject;

            var hrConsole = policyConfig.SetDefaultEndpoint(captureDeviceId, ERole.Console);
            var hrMultimedia = policyConfig.SetDefaultEndpoint(captureDeviceId, ERole.Multimedia);
            var hrCommunications = policyConfig.SetDefaultEndpoint(captureDeviceId, ERole.Communications);

            return hrConsole == 0 && hrMultimedia == 0 && hrCommunications == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (comObject != null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => ScheduleRefresh();

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => ScheduleRefresh();

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => ScheduleRefresh();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Capture)
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

    private static List<WaveInDeviceDescriptor> GetWaveInDevices()
    {
        var result = new List<WaveInDeviceDescriptor>();

        try
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                result.Add(new WaveInDeviceDescriptor(i, caps.ProductName ?? string.Empty));
            }
        }
        catch
        {
        }

        return result;
    }

    private static int? ResolveWaveInDeviceNumber(string endpointName, IReadOnlyList<WaveInDeviceDescriptor> waveInDevices)
    {
        if (waveInDevices.Count == 0)
            return null;

        var normalizedEndpoint = NormalizeDeviceName(endpointName);
        if (string.IsNullOrWhiteSpace(normalizedEndpoint))
            return waveInDevices[0].DeviceNumber;

        var exact = waveInDevices.FirstOrDefault(d => NormalizeDeviceName(d.Name) == normalizedEndpoint);
        if (exact != null)
            return exact.DeviceNumber;

        var contains = waveInDevices.FirstOrDefault(d =>
        {
            var normalizedWaveName = NormalizeDeviceName(d.Name);
            return normalizedWaveName.Contains(normalizedEndpoint, StringComparison.Ordinal) ||
                   normalizedEndpoint.Contains(normalizedWaveName, StringComparison.Ordinal);
        });

        if (contains != null)
            return contains.DeviceNumber;

        var endpointTokens = Tokenize(endpointName);
        var bestMatch = waveInDevices
            .Select(d => new
            {
                Device = d,
                Score = Tokenize(d.Name).Intersect(endpointTokens, StringComparer.Ordinal).Count()
            })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        return bestMatch?.Score > 0
            ? bestMatch.Device.DeviceNumber
            : waveInDevices[0].DeviceNumber;
    }

    private static bool IsPreferredHeadsetDevice(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Headphone", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Earbud", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Pods", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Jabra", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Bose", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Sony", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("USB", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDeviceName(string name)
    {
        return new string(name
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string[] Tokenize(string name)
    {
        return name
            .Split([' ', '-', '_', '(', ')', '[', ']', ',', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeDeviceName)
            .Where(token => token.Length > 1)
            .ToArray();
    }

    private sealed record WaveInDeviceDescriptor(int DeviceNumber, string Name);

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient
    {
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, IntPtr ppFormat);
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int bDefault, IntPtr ppFormat);
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId);
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, IntPtr endpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, IntPtr pmftPeriod);
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, IntPtr pMode);
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, IntPtr mode);
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ref PropertyKey key, IntPtr pv);
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ref PropertyKey key, IntPtr pv);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole eRole);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int bVisible);
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }
}
