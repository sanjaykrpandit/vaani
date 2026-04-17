using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Runtime.InteropServices;
using Vaani.Models;
using Avalonia.Threading;

namespace Vaani.Services;

public class DeviceService
{
    // Singleton instance for shared caching across the entire app
    private static readonly Lazy<DeviceService> _instance = new(() => new DeviceService());
    public static DeviceService Instance => _instance.Value;

    private CancellationTokenSource? _cts;
    private bool _isPlayingToMeeting;
    private readonly object _playbackLock = new();
    private Task? _runningTask;
    private readonly SemaphoreSlim _outgoingSynthesisLock = new(1, 1);
    private readonly SemaphoreSlim _incomingSynthesisLock = new(1, 1);

    public event EventHandler<string>? LogMessage;
    public event EventHandler<TranslationEventArgs>? TranslationReceived;
    public event EventHandler<MessageEventArgs>? MessageReceived;
    // Optional topology-change hook (kept for ViewModel compatibility).
    // Real-time notifications are intentionally not wired here to avoid unstable driver callbacks.
    public event EventHandler? DeviceTopologyChanged;

    public bool IsRunning => _cts != null && !_cts.Token.IsCancellationRequested;

    // Keep public constructor for backward compatibility, but singleton is preferred
    public DeviceService()
    {
    }


    //-------------- Enhanced Device Caching (OPTIMIZED FOR PERFORMANCE) --------------
    private static readonly MMDeviceEnumerator _enumerator = new();
    private static List<AudioDeviceInfo>? _cachedInputs;
    private static List<AudioDeviceInfo>? _cachedOutputs;
    private static MMDevice? _cachedOutgoingCable;
    private static MMDevice? _cachedIncomingCable;
    private static MMDevice? _cachedPhysicalMic;
    private static MMDevice? _cachedPhysicalSpeaker;
    private static DateTime _lastCacheTime = DateTime.MinValue;
    
    // Cache expires after 15 seconds for optimal performance
    // Balances responsiveness with minimal overhead (device changes are rare)
    // MainViewModel's 3-second timer will still benefit from cache hits
    private static readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(60);
    
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Force refresh the device cache (call after device changes or when needed)
    /// </summary>
    public void RefreshDeviceCache()
    {
        lock (_cacheLock)
        {
            _cachedInputs = null;
            _cachedOutputs = null;
            _cachedOutgoingCable = null;
            _cachedIncomingCable = null;
            _cachedPhysicalMic = null;
            _cachedPhysicalSpeaker = null;
            _lastCacheTime = DateTime.MinValue;
            Log("[CACHE] Device cache cleared");
        }
    }

    /// <summary>
    /// Check if cache is still valid (expires after 15 seconds)
    /// </summary>
    private bool IsCacheValid()
    {
        return (DateTime.UtcNow - _lastCacheTime) < _cacheExpiration;
    }

    /// <summary>
    /// Get all devices with smart caching that respects auto-refresh requirements
    /// </summary>
    public (List<AudioDeviceInfo> Inputs, List<AudioDeviceInfo> Outputs) GetAllDevices()
    {
        lock (_cacheLock)
        {
            // Return cached data if still valid (within 15 seconds)
            if (_cachedInputs != null && _cachedOutputs != null && IsCacheValid())
            {
                return (_cachedInputs, _cachedOutputs);
            }
        }

        var inputs = new List<AudioDeviceInfo>();
        var outputs = new List<AudioDeviceInfo>();
        MMDevice? defaultInput = null;
        MMDevice? defaultOutput = null;

        try
        {
            defaultInput = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            defaultOutput = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

            var captureDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in captureDevices)
            {
                inputs.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    FriendlyName = device.FriendlyName,
                    IsDefault = device.ID == defaultInput.ID,
                    IsCableDevice = device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
                });
                device.Dispose();
            }

            var renderDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in renderDevices)
            {
                outputs.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    FriendlyName = device.FriendlyName,
                    IsDefault = device.ID == defaultOutput.ID,
                    IsCableDevice = device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
                });
                device.Dispose();
            }

            defaultInput?.Dispose();
            defaultOutput?.Dispose();
        }
        catch (Exception ex)
        {
            Log($"[CACHE] Error listing devices: {ex.Message}");
            defaultInput?.Dispose();
            defaultOutput?.Dispose();
        }

        lock (_cacheLock)
        {
            _cachedInputs = inputs;
            _cachedOutputs = outputs;
            _lastCacheTime = DateTime.UtcNow;
        }

        return (inputs, outputs);
    }

    /// <summary>
    /// Get input devices (now uses smart cache)
    /// </summary>
    public List<AudioDeviceInfo> GetInputDevices()
    {
        var (inputs, _) = GetAllDevices();
        return inputs;
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, message);
    }

    /// <summary>
    /// Get output devices (now uses smart cache)
    /// </summary>
    public List<AudioDeviceInfo> GetOutputDevices()
    {
        var (_, outputs) = GetAllDevices();
        return outputs;
    }

    /// <summary>
    /// Finds cable device for OUTGOING audio with short-term caching
    /// Priority: CABLE-A Input > CABLE-B Input > Standard CABLE Input
    /// </summary>
    public MMDevice? FindOutgoingCableDevice()
    {
        lock (_cacheLock)
        {
            if (_cachedOutgoingCable != null && IsCacheValid())
            {
                return _cachedOutgoingCable;
            }
        }

        MMDevice? device = null;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            // Priority 1: CABLE-A Input (send your translated voice here)
            foreach (var dev in devices)
            {
                var name = dev.FriendlyName;
                if (name.Contains("CABLE-A Input", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE A Input", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[OUTGOING] Using CABLE-A Input: {name}");
                    device = dev;
                    break;
                }
            }

            if (device == null)
            {
                Log("[OUTGOING] ⚠️ No CABLE output device found!");
            }
        }
        catch (Exception ex)
        {
            Log($"[OUTGOING] Error finding cable device: {ex.Message}");
        }

        lock (_cacheLock)
        {
            _cachedOutgoingCable = device;
            _lastCacheTime = DateTime.UtcNow;
        }

        return device;
    }


    public MMDevice? FindOutgoingCaptureCableDevice()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            // Priority 1: CABLE-A Output (send your translated voice here)
            foreach (var device in devices)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE-A Output", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE A Output", StringComparison.OrdinalIgnoreCase))
                {                   
                    return device;
                }
            }   
        }
        catch (Exception ex)
        {
            Log($"[OUTGOING] Error finding cable device: {ex.Message}");
        }
        return null;
    }


    /// <summary>
    /// Finds cable device for INCOMING audio with short-term caching
    /// Priority: CABLE-B Output > CABLE-A Output > Standard CABLE Output
    /// Uses opposite cable from outgoing to avoid conflicts
    /// </summary>
    public MMDevice? FindIncomingCableDevice()
    {
        lock (_cacheLock)
        {
            if (_cachedIncomingCable != null && IsCacheValid())
            {
                return _cachedIncomingCable;
            }
        }

        MMDevice? device = null;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            // Priority 1: CABLE-B Output (capture meeting audio from here)
            // Using CABLE-B for incoming if CABLE-A is used for outgoing
            foreach (var dev in devices)
            {
                var name = dev.FriendlyName;
                if (name.Contains("CABLE-B Output", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE B Output", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[INCOMING] Using CABLE-B Output: {name}");
                    device = dev;
                    break;
                }
            }

            if (device == null)
            {
                Log("[INCOMING] ⚠️ No CABLE input device found!");
            }
        }
        catch (Exception ex)
        {
            Log($"[INCOMING] Error finding cable device: {ex.Message}");
        }

        lock (_cacheLock)
        {
            _cachedIncomingCable = device;
            _lastCacheTime = DateTime.UtcNow;
        }

        return device;
    }

    public MMDevice? FindIncomingReaderCableDevice()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE-B Input", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE B Input", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[INCOMING] Using CABLE-B Input: {name}");
                    return device;
                }
            }          

            Log("[INCOMING] ⚠️ No CABLE input device found!");
        }
        catch (Exception ex)
        {
            Log($"[INCOMING] Error finding cable device: {ex.Message}");
        }

        return null;
    }

    public (string microphone, string speaker) GetRecommendedMeetingDevices()
    {
        string microphoneName = "Not Available";
        string speakerName = "Not Available";

        try
        {
            var (inputs, outputs) = GetAllDevices();  // Now uses cache

            // OUTGOING: Find from outputs (Render devices)
            AudioDeviceInfo? outgoingDevice = null;
            foreach (var device in outputs)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE-A Input", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE A Input", StringComparison.OrdinalIgnoreCase))
                {
                    outgoingDevice = device; break;
                }
            }
            if (outgoingDevice == null)
            {
                foreach (var device in outputs)
                {
                    var name = device.FriendlyName;
                    if (name.Contains("CABLE-B Input", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("CABLE B Input", StringComparison.OrdinalIgnoreCase))
                    {
                        outgoingDevice = device; break;
                    }
                }
            }

            // Map to microphone name
            if (outgoingDevice != null)
            {
                var name = outgoingDevice.FriendlyName;
                if (name.Contains("CABLE-A Input", StringComparison.OrdinalIgnoreCase))
                    microphoneName = "CABLE-A Output (VB-Audio Cable A)";
                else if (name.Contains("CABLE-B Input", StringComparison.OrdinalIgnoreCase))
                    microphoneName = "CABLE-B Output (VB-Audio Cable B)";
                else
                    microphoneName = "CABLE Output (VB-Audio Cable)";
            }

            // INCOMING: Same pattern for inputs list
            AudioDeviceInfo? incomingDevice = null;
            foreach (var device in inputs)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE-B Output", StringComparison.OrdinalIgnoreCase))
                {
                    incomingDevice = device; break;
                }
            }

            if (incomingDevice != null)
            {
                var name = incomingDevice.FriendlyName;
                if (name.Contains("CABLE-B Output", StringComparison.OrdinalIgnoreCase))
                    speakerName = "CABLE-B Input (VB-Audio Cable B)";
                else if (name.Contains("CABLE-A Output", StringComparison.OrdinalIgnoreCase))
                    speakerName = "CABLE-A Input (VB-Audio Cable A)";
                else
                    speakerName = "CABLE Input (VB-Audio Cable)";
            }
        }
        catch (Exception ex)
        {
            Log($"[MEETING SETUP] Error: {ex.Message}");
        }

        return (microphoneName, speakerName);
    }

    public bool HasCableABDevices()
    {
        try
        {
            var (inputs, outputs) = GetAllDevices(); // Now uses cache

            bool hasCableA = outputs.Any(d =>
                d.FriendlyName.Contains("CABLE-A", StringComparison.OrdinalIgnoreCase) ||
                d.FriendlyName.Contains("CABLE A", StringComparison.OrdinalIgnoreCase));

            bool hasCableB = inputs.Any(d =>
                d.FriendlyName.Contains("CABLE-B", StringComparison.OrdinalIgnoreCase) ||
                d.FriendlyName.Contains("CABLE B", StringComparison.OrdinalIgnoreCase));

            bool hasAB = hasCableA && hasCableB;

            if (hasAB)
            {
                Log("[DEVICE DETECTION] ✅ CABLE A+B detected - Parallel flow available");
            }
            else if (hasCableA || hasCableB)
            {
                Log("[DEVICE DETECTION] ⚠️ Only one CABLE device found - Sequential mode required");
            }
            else
            {
                Log("[DEVICE DETECTION] ⚠️ No CABLE devices found - Sequential mode required");
            }

            return hasAB;
        }
        catch (Exception ex)
        {
            Log($"[DEVICE DETECTION] Error detecting CABLE A+B: {ex.Message}");
            return false; // Default to safe sequential mode
        }
    }

    public MMDevice? FindPhysicalMicrophone()
    {
        lock (_cacheLock)
        {
            if (_cachedPhysicalMic != null && IsCacheValid())
            {
                return _cachedPhysicalMic;
            }
        }

        MMDevice? device = null;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            // PRIORITY 1: Look for Headset/USB microphones first (highest priority)
            foreach (var dev in devices)
            {
                var name = dev.FriendlyName;

                // Skip CABLE devices entirely
                if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check for headset microphone (highest priority)
                if (name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
                    (name.Contains("USB", StringComparison.OrdinalIgnoreCase) &&
                     (name.Contains("Microphone", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("Mic", StringComparison.OrdinalIgnoreCase))))
                {
                    Log($"[DEVICE] ✅ Found HEADSET MIC (Priority 1): {name}");
                    device = dev;
                    break;
                }
            }

            // PRIORITY 2: Look for internal/built-in microphones (second priority)
            if (device == null)
            {
                foreach (var dev in devices)
                {
                    var name = dev.FriendlyName;

                    // Skip CABLE devices entirely
                    if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Check for internal/built-in microphones
                    if (name.Contains("Microphone", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Mic", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Array", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Webcam", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[DEVICE] ✅ Found INTERNAL MIC (Priority 2): {name}");
                        device = dev;
                        break;
                    }
                }
            }

            // PRIORITY 3: Fallback - Return first non-CABLE capture device
            if (device == null)
            {
                foreach (var dev in devices)
                {
                    if (!dev.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[DEVICE] ⚠️ Using fallback device (Priority 3): {dev.FriendlyName}");
                        device = dev;
                        break;
                    }
                }
            }

            if (device == null)
                Log("[DEVICE] ❌ No physical microphone found");
        }
        catch (Exception ex)
        {
            Log($"[DEVICE] ❌ Error finding physical microphone: {ex.Message}");
        }

        lock (_cacheLock)
        {
            _cachedPhysicalMic = device;
            _lastCacheTime = DateTime.UtcNow;
        }

        return device;
    }

    public MMDevice? FindPhysicalSpeaker()
    {
        lock (_cacheLock)
        {
            if (_cachedPhysicalSpeaker != null && IsCacheValid())
            {
                return _cachedPhysicalSpeaker;
            }
        }

        MMDevice? device = null;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            // PRIORITY 1: Look for Headset/Headphone devices first (highest priority)
            foreach (var dev in devices)
            {
                var name = dev.FriendlyName;

                // Skip CABLE devices entirely
                if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check for headset/headphone (highest priority)
                if (name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Headphone", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[DEVICE] ✅ Found HEADSET (Priority 1): {name}");
                    device = dev;
                    break;
                }
            }

            // PRIORITY 2: Look for internal/laptop speakers (second priority)
            if (device == null)
            {
                foreach (var dev in devices)
                {
                    var name = dev.FriendlyName;

                    // Skip CABLE devices entirely
                    if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Check for internal speakers
                    if (name.Contains("Speaker", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Audio", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[DEVICE] ✅ Found INTERNAL SPEAKER (Priority 2): {name}");
                        device = dev;
                        break;
                    }
                }
            }

            // PRIORITY 3: Fallback - Return first non-CABLE render device
            if (device == null)
            {
                foreach (var dev in devices)
                {
                    if (!dev.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[DEVICE] ⚠️ Using fallback device (Priority 3): {dev.FriendlyName}");
                        device = dev;
                        break;
                    }
                }
            }

            if (device == null)
                Log("[DEVICE] ❌ No physical speaker found");
        }
        catch (Exception ex)
        {
            Log($"[DEVICE] ❌ Error finding physical speaker: {ex.Message}");
        }

        lock (_cacheLock)
        {
            _cachedPhysicalSpeaker = device;
            _lastCacheTime = DateTime.UtcNow;
        }

        return device;
    }

    /// <summary>
    /// Restores Windows default audio devices to physical endpoints (laptop/headset).
    /// </summary>
    public bool TryRestorePhysicalDefaults()
    {
        try
        {
            var physicalMic = FindPhysicalMicrophone();
            var physicalSpeaker = FindPhysicalSpeaker();

            if (physicalMic == null || physicalSpeaker == null)
            {
                Log("[DEFAULT DEVICE] Skipped restoring physical defaults (physical mic/speaker not fully available).");
                return false;
            }

            var result = TrySetDefaultEndpoints(physicalMic.ID, physicalSpeaker.ID);
            Log(result
                ? $"[DEFAULT DEVICE] Restored physical defaults (Mic={physicalMic.FriendlyName}, Speaker={physicalSpeaker.FriendlyName})."
                : "[DEFAULT DEVICE] Failed to restore physical defaults.");

            return result;
        }
        catch (Exception ex)
        {
            Log($"[DEFAULT DEVICE] Error restoring physical defaults: {ex.Message}");
            return false;
        }
    }

    private bool TrySetDefaultEndpoints(string captureDeviceId, string renderDeviceId)
    {
        object? comObject = null;
        try
        {
            comObject = new PolicyConfigClient();
            var policyConfig = (IPolicyConfig)comObject;

            var hrMicConsole = policyConfig.SetDefaultEndpoint(captureDeviceId, ERole.Console);
            var hrMicMultimedia = policyConfig.SetDefaultEndpoint(captureDeviceId, ERole.Multimedia);
            var hrMicCommunications = policyConfig.SetDefaultEndpoint(captureDeviceId, ERole.Communications);

            var hrSpkConsole = policyConfig.SetDefaultEndpoint(renderDeviceId, ERole.Console);
            var hrSpkMultimedia = policyConfig.SetDefaultEndpoint(renderDeviceId, ERole.Multimedia);
            var hrSpkCommunications = policyConfig.SetDefaultEndpoint(renderDeviceId, ERole.Communications);

            var ok = hrMicConsole == 0 && hrMicMultimedia == 0 && hrMicCommunications == 0 &&
                     hrSpkConsole == 0 && hrSpkMultimedia == 0 && hrSpkCommunications == 0;

            if (!ok)
            {
                Log($"[DEFAULT DEVICE] SetDefaultEndpoint HRESULTs - Mic(C/M/C): {hrMicConsole}/{hrMicMultimedia}/{hrMicCommunications}, " +
                    $"Speaker(C/M/C): {hrSpkConsole}/{hrSpkMultimedia}/{hrSpkCommunications}");
            }

            return ok;
        }
        catch (Exception ex)
        {
            Log($"[DEFAULT DEVICE] COM error while setting defaults: {ex.Message}");
            return false;
        }
        finally
        {
            if (comObject != null && Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }
    }

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