//using NAudio.CoreAudioApi;
//using NAudio.Wave;
//using Vaani.Models;
//using Avalonia.Threading;

//namespace Vaani.Services;

//public class DeviceService
//{
//    private CancellationTokenSource? _cts;
//    private bool _isPlayingToMeeting;
//    private readonly object _playbackLock = new();
//    private Task? _runningTask;
//    private readonly SemaphoreSlim _outgoingSynthesisLock = new(1, 1);
//    private readonly SemaphoreSlim _incomingSynthesisLock = new(1, 1);

//    public event EventHandler<string>? LogMessage;
//    public event EventHandler<TranslationEventArgs>? TranslationReceived;
//    public event EventHandler<MessageEventArgs>? MessageReceived;

//    public bool IsRunning => _cts != null && !_cts.Token.IsCancellationRequested;

//    public List<AudioDeviceInfo> GetInputDevices()
//    {
//        var devices = new List<AudioDeviceInfo>();
//        try
//        {
//            var enumerator = new MMDeviceEnumerator();
//            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
//            var defaultCapture = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

//            foreach (var device in captureDevices)
//            {
//                devices.Add(new AudioDeviceInfo
//                {
//                    Id = device.ID,
//                    FriendlyName = device.FriendlyName,
//                    IsDefault = device.ID == defaultCapture.ID,
//                    IsCableDevice = device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
//                });
//            }
//        }
//        catch (Exception ex)
//        {
//            Log($"Error listing input devices: {ex.Message}");
//        }
//        return devices;
//    }
//    private void Log(string message)
//    {
//        LogMessage?.Invoke(this,message);
//    }
//    public List<AudioDeviceInfo> GetOutputDevices()
//    {
//        var devices = new List<AudioDeviceInfo>();
//        try
//        {
//            var enumerator = new MMDeviceEnumerator();
//            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
//            var defaultRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

//            foreach (var device in renderDevices)
//            {
//                devices.Add(new AudioDeviceInfo
//                {
//                    Id = device.ID,
//                    FriendlyName = device.FriendlyName,
//                    IsDefault = device.ID == defaultRender.ID,
//                    IsCableDevice = device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
//                });
//            }
//        }
//        catch (Exception ex)
//        {
//            Log($"Error listing output devices: {ex.Message}");
//        }
//        return devices;
//    }

//    public MMDevice? FindCableMMDevice(DataFlow dataFlow)
//    {
//        try
//        {
//            var enumerator = new MMDeviceEnumerator();
//            var devices = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);

//            foreach (var device in devices)
//            {
//                var name = device.FriendlyName;

//                if (dataFlow == DataFlow.Capture)
//                {                  

//                    if (name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
//                        return device;
//                }
//                else
//                {
//                    if (name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
//                        return device;
//                }
//            }

//            if (dataFlow == DataFlow.Render)
//            {
//                foreach (var device in devices)
//                {
//                    if (device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) &&
//                        device.FriendlyName.Contains("Speakers", StringComparison.OrdinalIgnoreCase))
//                        return device;
//                }
//            }
//        }
//        catch
//        {
//            // Ignore
//        }

//        return null;
//    }
//    // public List<AudioDeviceInfo> GetOutputDevices()
//    public string getMeeingConnectedDevice(string type)
//    {
//        if (type == "INCOMING")
//        {
//            var cableDevice = FindCableMMDevice(DataFlow.Capture);
//            if (cableDevice != null)
//            {
//                return cableDevice.FriendlyName;
//            }
//            else
//            {
//                return ("WARNING: CABLE device not found!");
//            }

//        }
//        else if (type == "OUTGOING")
//        {
//            var cableDevice = FindCableMMDevice(DataFlow.Render);
//            if (cableDevice != null)
//            {
//                return cableDevice.FriendlyName;
//            }
//            else
//            {
//                return ("WARNING: CABLE Output device not found!");
//            }
//        }
//        else
//        {
//            return ("Invalid type specified");
//        }
//    }
//    public MMDevice? FindPhysicalMicrophone()
//    {
//        try
//        {
//            var enumerator = new MMDeviceEnumerator();
//            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

//            Log("[DEVICE] Searching for physical microphone with priority (1-Headset Mic, 2-Internal Mic)...");

//            // PRIORITY 1: Look for Headset/USB microphones first (highest priority)
//            foreach (var device in devices)
//            {
//                var name = device.FriendlyName;

//                // Skip CABLE devices entirely
//                if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
//                {
//                    Log($"[DEVICE] Skipping CABLE device: {name}");
//                    continue;
//                }

//                // Check for headset microphone (highest priority)
//                if (name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
//                    (name.Contains("USB", StringComparison.OrdinalIgnoreCase) &&
//                     (name.Contains("Microphone", StringComparison.OrdinalIgnoreCase) ||
//                      name.Contains("Mic", StringComparison.OrdinalIgnoreCase))))
//                {
//                    Log($"[DEVICE] ✅ Found HEADSET MIC (Priority 1): {name}");
//                    return device;
//                }
//            }

//            // PRIORITY 2: Look for internal/built-in microphones (second priority)
//            foreach (var device in devices)
//            {
//                var name = device.FriendlyName;

//                // Skip CABLE devices entirely
//                if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
//                    continue;

//                // Check for internal/built-in microphones
//                if (name.Contains("Microphone", StringComparison.OrdinalIgnoreCase) ||
//                    name.Contains("Mic", StringComparison.OrdinalIgnoreCase) ||
//                    name.Contains("Array", StringComparison.OrdinalIgnoreCase) ||
//                    name.Contains("Webcam", StringComparison.OrdinalIgnoreCase))
//                {
//                    Log($"[DEVICE] ✅ Found INTERNAL MIC (Priority 2): {name}");
//                    return device;
//                }
//            }

//            // PRIORITY 3: Fallback - Return first non-CABLE capture device
//            foreach (var device in devices)
//            {
//                if (!device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
//                {
//                    Log($"[DEVICE] ⚠️ Using fallback device (Priority 3): {device.FriendlyName}");
//                    return device;
//                }
//            }

//            Log("[DEVICE] ❌ No physical microphone found");
//        }
//        catch (Exception ex)
//        {
//            Log($"[DEVICE] ❌ Error finding physical microphone: {ex.Message}");
//        }

//        return null;
//    }
//    public MMDevice? FindPhysicalSpeaker()
//    {
//        try
//        {
//            var enumerator = new MMDeviceEnumerator();
//            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

//            Log("[DEVICE] Searching for physical speakers with priority (1-Headset, 2-Internal Speaker)...");

//            // PRIORITY 1: Look for Headset/Headphone devices first (highest priority)
//            foreach (var device in devices)
//            {
//                var name = device.FriendlyName;

//                // Skip CABLE devices entirely
//                if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
//                    continue;

//                // Check for headset/headphone (highest priority)
//                if (name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
//                    name.Contains("Headphone", StringComparison.OrdinalIgnoreCase))
//                {
//                    Log($"[DEVICE] ✅ Found HEADSET (Priority 1): {name}");
//                    return device;
//                }
//            }

//            // PRIORITY 2: Look for internal/laptop speakers (second priority)
//            foreach (var device in devices)
//            {
//                var name = device.FriendlyName;

//                // Skip CABLE devices entirely
//                if (name.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
//                    continue;

//                // Check for internal speakers
//                if (name.Contains("Speaker", StringComparison.OrdinalIgnoreCase) ||
//                    name.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
//                    name.Contains("Audio", StringComparison.OrdinalIgnoreCase))
//                {
//                    Log($"[DEVICE] ✅ Found INTERNAL SPEAKER (Priority 2): {name}");
//                    return device;
//                }
//            }

//            // PRIORITY 3: Fallback - Return first non-CABLE render device
//            foreach (var device in devices)
//            {
//                if (!device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
//                {
//                    Log($"[DEVICE] ⚠️ Using fallback device (Priority 3): {device.FriendlyName}");
//                    return device;
//                }
//            }

//            Log("[DEVICE] ❌ No physical speaker found");
//        }
//        catch (Exception ex)
//        {
//            Log($"[DEVICE] ❌ Error finding physical speaker: {ex.Message}");
//        }

//        return null;
//    }



//}

