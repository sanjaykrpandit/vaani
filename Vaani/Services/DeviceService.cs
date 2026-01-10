using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vaani.Models;
using Avalonia.Threading;

namespace Vaani.Services;

public class DeviceService
{
    private CancellationTokenSource? _cts;
    private bool _isPlayingToMeeting;
    private readonly object _playbackLock = new();
    private Task? _runningTask;
    private readonly SemaphoreSlim _outgoingSynthesisLock = new(1, 1);
    private readonly SemaphoreSlim _incomingSynthesisLock = new(1, 1);

    public event EventHandler<string>? LogMessage;
    public event EventHandler<TranslationEventArgs>? TranslationReceived;
    public event EventHandler<MessageEventArgs>? MessageReceived;

    public bool IsRunning => _cts != null && !_cts.Token.IsCancellationRequested;


    //-------------- Cached Device Lists --------------
    private static readonly MMDeviceEnumerator _enumerator = new();
    private static List<AudioDeviceInfo>? _cachedInputs;
    private static List<AudioDeviceInfo>? _cachedOutputs;
    private static readonly object _lock = new();
    public (List<AudioDeviceInfo> Inputs, List<AudioDeviceInfo> Outputs) GetAllDevices()
    {
        lock (_lock)
        {
            if (_cachedInputs != null && _cachedOutputs != null)
                return (_cachedInputs, _cachedOutputs);
        }

        var inputs = new List<AudioDeviceInfo>();
        var outputs = new List<AudioDeviceInfo>();
        var defaultInput = default(MMDevice);
        var defaultOutput = default(MMDevice);

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

            defaultInput.Dispose();
            defaultOutput.Dispose();
        }
        catch (Exception ex)
        {
            Log($"Error listing devices: {ex.Message}");
        }

        lock (_lock)
        {
            _cachedInputs = inputs;
            _cachedOutputs = outputs;
        }

        return (inputs, outputs);
    }


    //--------------
    public List<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var defaultCapture = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

            foreach (var device in captureDevices)
            {
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    FriendlyName = device.FriendlyName,
                    IsDefault = device.ID == defaultCapture.ID,
                    IsCableDevice = device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch (Exception ex)
        {
            Log($"Error listing input devices: {ex.Message}");
        }
        return devices;
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, message);
    }

    public List<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var defaultRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

            foreach (var device in renderDevices)
            {
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    FriendlyName = device.FriendlyName,
                    IsDefault = device.ID == defaultRender.ID,
                    IsCableDevice = device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch (Exception ex)
        {
            Log($"Error listing output devices: {ex.Message}");
        }
        return devices;
    }

    /// <summary>
    /// Finds cable device for OUTGOING audio (your voice to meeting)
    /// Priority: CABLE-A Input > CABLE-B Input > Standard CABLE Input
    /// </summary>
    public MMDevice? FindOutgoingCableDevice()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            // Priority 1: CABLE-A Input (send your translated voice here)
            foreach (var device in devices)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE-A Input", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE A Input", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[OUTGOING] Using CABLE-A Input: {name}");
                    return device;
                }
            }

            // Priority 2: CABLE-B Input
            //foreach (var device in devices)
            //{
            //    var name = device.FriendlyName;
            //    if (name.Contains("CABLE-B Input", StringComparison.OrdinalIgnoreCase) ||
            //        name.Contains("CABLE B Input", StringComparison.OrdinalIgnoreCase))
            //    {
            //        Log($"[OUTGOING] Using CABLE-B Input: {name}");
            //        return device;
            //    }
            //}

            // Priority 3: Standard CABLE Input
            //foreach (var device in devices)
            //{
            //    var name = device.FriendlyName;
            //    if (name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) &&
            //        !name.Contains("CABLE-A", StringComparison.OrdinalIgnoreCase) &&
            //        !name.Contains("CABLE-B", StringComparison.OrdinalIgnoreCase) &&
            //        !name.Contains("CABLE A", StringComparison.OrdinalIgnoreCase) &&
            //        !name.Contains("CABLE B", StringComparison.OrdinalIgnoreCase))
            //    {
            //        Log($"[OUTGOING] Using standard CABLE Input: {name}");
            //        return device;
            //    }
            //}

            // Fallback: CABLE Speakers
            //foreach (var device in devices)
            //{
            //    if (device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) &&
            //        device.FriendlyName.Contains("Speakers", StringComparison.OrdinalIgnoreCase))
            //    {
            //        Log($"[OUTGOING] Using CABLE Speakers (fallback): {device.FriendlyName}");
            //        return device;
            //    }
            //}

            Log("[OUTGOING] ⚠️ No CABLE output device found!");
        }
        catch (Exception ex)
        {
            Log($"[OUTGOING] Error finding cable device: {ex.Message}");
        }

        return null;
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
    /// Finds cable device for INCOMING audio (meeting audio capture)
    /// Priority: CABLE-B Output > CABLE-A Output > Standard CABLE Output
    /// Uses opposite cable from outgoing to avoid conflicts
    /// </summary>
    public MMDevice? FindIncomingCableDevice()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            // Priority 1: CABLE-B Output (capture meeting audio from here)
            // Using CABLE-B for incoming if CABLE-A is used for outgoing
            foreach (var device in devices)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE-B Output", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE B Output", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[INCOMING] Using CABLE-B Output: {name}");
                    return device;
                }
            }

            //// Priority 2: CABLE-A Output
            //foreach (var device in devices)
            //{
            //    var name = device.FriendlyName;
            //    if (name.Contains("CABLE-A Output", StringComparison.OrdinalIgnoreCase) ||
            //        name.Contains("CABLE A Output", StringComparison.OrdinalIgnoreCase))
            //    {
            //        Log($"[INCOMING] Using CABLE-A Output: {name}");
            //        return device;
            //    }
            //}

            //// Priority 3: Standard CABLE Output
            //foreach (var device in devices)
            //{
            //    var name = device.FriendlyName;
            //    if (name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase) &&
            //        !name.Contains("CABLE-A", StringComparison.OrdinalIgnoreCase) &&
            //        !name.Contains("CABLE-B", StringComparison.OrdinalIgnoreCase) &&
            //        !name.Contains("CABLE A", StringComparison.OrdinalIgnoreCase) &&
            //        !name.Contains("CABLE B", StringComparison.OrdinalIgnoreCase))
            //    {
            //        Log($"[INCOMING] Using standard CABLE Output: {name}");
            //        return device;
            //    }
            //}

            Log("[INCOMING] ⚠️ No CABLE input device found!");
        }
        catch (Exception ex)
        {
            Log($"[INCOMING] Error finding cable device: {ex.Message}");
        }

        return null;
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
            var (inputs, outputs) = GetAllDevices();  // Returns List<AudioDeviceInfo>

            // OUTGOING: Find from outputs (Render devices)
            AudioDeviceInfo? outgoingDevice = null;
            foreach (var device in outputs)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE-A Input", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE A Input", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[OUTGOING] Using CABLE-A Input: {name}");
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
                        Log($"[OUTGOING] Using CABLE-B Input: {name}");
                        outgoingDevice = device; break;
                    }
                }
            }
            // ... continue other priorities with AudioDeviceInfo

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
                    Log($"[INCOMING] Using CABLE-B Output: {name}");
                    incomingDevice = device; break;
                }
            }
            // ... continue priorities

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

            Log($"[MEETING SETUP] Recommended Microphone: {microphoneName}");
            Log($"[MEETING SETUP] Recommended Speaker: {speakerName}");
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
            var enumerator = new MMDeviceEnumerator();

            bool hasCableA = false;
            bool hasCableB = false;

            // Check for CABLE-A devices (Input or Output)
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in renderDevices)
            {
                if (device.FriendlyName.Contains("CABLE-A", StringComparison.OrdinalIgnoreCase) ||
                    device.FriendlyName.Contains("CABLE A", StringComparison.OrdinalIgnoreCase))
                {
                    hasCableA = true;
                    Log($"[DEVICE DETECTION] Found CABLE-A (Render): {device.FriendlyName}");
                    break;
                }
            }

            // Check for CABLE-B devices (Input or Output)
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in captureDevices)
            {
                if (device.FriendlyName.Contains("CABLE-B", StringComparison.OrdinalIgnoreCase) ||
                    device.FriendlyName.Contains("CABLE B", StringComparison.OrdinalIgnoreCase))
                {
                    hasCableB = true;
                    Log($"[DEVICE DETECTION] Found CABLE-B (Capture): {device.FriendlyName}");
                    break;
                }
            }

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
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            Log("[DEVICE] Searching for physical microphone with priority (1-Headset Mic, 2-Internal Mic)...");

            // PRIORITY 1: Look for Headset/USB microphones first (highest priority)
            foreach (var device in devices)
            {
                var name = device.FriendlyName;

                // Skip CABLE devices entirely
                if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[DEVICE] Skipping CABLE device: {name}");
                    continue;
                }

                // Check for headset microphone (highest priority)
                if (name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
                    (name.Contains("USB", StringComparison.OrdinalIgnoreCase) &&
                     (name.Contains("Microphone", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("Mic", StringComparison.OrdinalIgnoreCase))))
                {
                    Log($"[DEVICE] ✅ Found HEADSET MIC (Priority 1): {name}");
                    return device;
                }
            }

            // PRIORITY 2: Look for internal/built-in microphones (second priority)
            foreach (var device in devices)
            {
                var name = device.FriendlyName;

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
                    return device;
                }
            }

            // PRIORITY 3: Fallback - Return first non-CABLE capture device
            foreach (var device in devices)
            {
                if (!device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[DEVICE] ⚠️ Using fallback device (Priority 3): {device.FriendlyName}");
                    return device;
                }
            }

            Log("[DEVICE] ❌ No physical microphone found");
        }
        catch (Exception ex)
        {
            Log($"[DEVICE] ❌ Error finding physical microphone: {ex.Message}");
        }

        return null;
    }

    public MMDevice? FindPhysicalSpeaker()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            Log("[DEVICE] Searching for physical speakers with priority (1-Headset, 2-Internal Speaker)...");

            // PRIORITY 1: Look for Headset/Headphone devices first (highest priority)
            foreach (var device in devices)
            {
                var name = device.FriendlyName;

                // Skip CABLE devices entirely
                if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check for headset/headphone (highest priority)
                if (name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Headphone", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[DEVICE] ✅ Found HEADSET (Priority 1): {name}");
                    return device;
                }
            }

            // PRIORITY 2: Look for internal/laptop speakers (second priority)
            foreach (var device in devices)
            {
                var name = device.FriendlyName;

                // Skip CABLE devices entirely
                if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check for internal speakers
                if (name.Contains("Speaker", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Audio", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[DEVICE] ✅ Found INTERNAL SPEAKER (Priority 2): {name}");
                    return device;
                }
            }

            // PRIORITY 3: Fallback - Return first non-CABLE render device
            foreach (var device in devices)
            {
                if (!device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[DEVICE] ⚠️ Using fallback device (Priority 3): {device.FriendlyName}");
                    return device;
                }
            }

            Log("[DEVICE] ❌ No physical speaker found");
        }
        catch (Exception ex)
        {
            Log($"[DEVICE] ❌ Error finding physical speaker: {ex.Message}");
        }

        return null;
    }

}