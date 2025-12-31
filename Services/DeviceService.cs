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
            foreach (var device in devices)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE-B Input", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE B Input", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[OUTGOING] Using CABLE-B Input: {name}");
                    return device;
                }
            }

            // Priority 3: Standard CABLE Input
            foreach (var device in devices)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("CABLE-A", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("CABLE-B", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("CABLE A", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("CABLE B", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[OUTGOING] Using standard CABLE Input: {name}");
                    return device;
                }
            }

            // Fallback: CABLE Speakers
            foreach (var device in devices)
            {
                if (device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) &&
                    device.FriendlyName.Contains("Speakers", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[OUTGOING] Using CABLE Speakers (fallback): {device.FriendlyName}");
                    return device;
                }
            }

            Log("[OUTGOING] ⚠️ No CABLE output device found!");
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

            // Priority 2: CABLE-A Output
            foreach (var device in devices)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE-A Output", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE A Output", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[INCOMING] Using CABLE-A Output: {name}");
                    return device;
                }
            }

            // Priority 3: Standard CABLE Output
            foreach (var device in devices)
            {
                var name = device.FriendlyName;
                if (name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("CABLE-A", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("CABLE-B", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("CABLE A", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("CABLE B", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[INCOMING] Using standard CABLE Output: {name}");
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

    /// <summary>
    /// Returns the recommended meeting audio device settings.
    /// These are the OPPOSITE endpoints of what the translation app uses internally.
    /// </summary>
    /// <returns>Tuple of (MicrophoneName, SpeakerName) that should be set in the meeting app</returns>
    public (string microphone, string speaker) GetRecommendedMeetingDevices()
    {
        string microphoneName = "Not Available";
        string speakerName = "Not Available";

        try
        {
            var enumerator = new MMDeviceEnumerator();

            // MICROPHONE for Meeting App
            // The meeting needs to capture from CABLE-A Output (opposite of CABLE-A Input used for outgoing)
            var outgoingDevice = FindOutgoingCableDevice();
            if (outgoingDevice != null)
            {
                // Map the Input device to its Output counterpart
                var name = outgoingDevice.FriendlyName;

                if (name.Contains("CABLE-A Input", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE A Input", StringComparison.OrdinalIgnoreCase))
                {
                    microphoneName = "CABLE-A Output (VB-Audio Cable A)";
                }
                else if (name.Contains("CABLE-B Input", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("CABLE B Input", StringComparison.OrdinalIgnoreCase))
                {
                    microphoneName = "CABLE-B Output (VB-Audio Cable B)";
                }
                else if (name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
                {
                    microphoneName = "CABLE Output (VB-Audio Cable)";
                }
            }

            // SPEAKER for Meeting App
            // The meeting needs to output to CABLE-B Input (opposite of CABLE-B Output used for incoming)
            var incomingDevice = FindIncomingCableDevice();
            if (incomingDevice != null)
            {
                // Map the Output device to its Input counterpart
                var name = incomingDevice.FriendlyName;

                if (name.Contains("CABLE-B Output", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("CABLE B Output", StringComparison.OrdinalIgnoreCase))
                {
                    speakerName = "CABLE-B Input (VB-Audio Cable B)";
                }
                else if (name.Contains("CABLE-A Output", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("CABLE A Output", StringComparison.OrdinalIgnoreCase))
                {
                    speakerName = "CABLE-A Input (VB-Audio Cable A)";
                }
                else if (name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
                {
                    speakerName = "CABLE Input (VB-Audio Cable)";
                }
            }

            Log($"[MEETING SETUP] Recommended Microphone: {microphoneName}");
            Log($"[MEETING SETUP] Recommended Speaker: {speakerName}");
        }
        catch (Exception ex)
        {
            Log($"[MEETING SETUP] Error determining recommended devices: {ex.Message}");
        }

        return (microphoneName, speakerName);
    }


    // Add this method to the DeviceService class:

    /// <summary>
    /// Checks if both CABLE-A and CABLE-B devices are installed and available.
    /// This enables true parallel bidirectional audio flow without echo concerns.
    /// </summary>
    /// <returns>True if both CABLE-A and CABLE-B are detected, false otherwise</returns>
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

    /// <summary>
    /// Gets user-friendly setup instructions for configuring the meeting app
    /// </summary>
    /// <returns>Multi-line string with setup instructions</returns>
    public string GetMeetingSetupInstructions()
    {
        var (microphone, speaker) = GetRecommendedMeetingDevices();

        var instructions = $@"
╔══════════════════════════════════════════════════════════════╗
║          MEETING APP AUDIO CONFIGURATION GUIDE               ║
╚══════════════════════════════════════════════════════════════╝

📋 Configure your meeting app (Teams/Meet/Zoom) with these settings:

🎤 MICROPHONE (in meeting app):
   ➜ Set to: {microphone}
   ℹ️  This captures your translated voice and sends it to the meeting

🔊 SPEAKER (in meeting app):
   ➜ Set to: {speaker}
   ℹ️  This sends meeting audio to the translation app for processing

⚠️  IMPORTANT:
   • Do NOT use your physical microphone/speakers in the meeting app
   • Use the CABLE devices listed above
   • Your physical devices are used by the translation app

✅ SETUP STEPS:
   1. Open your meeting app settings (Teams/Meet/Zoom)
   2. Go to Audio/Device settings
   3. Set Microphone to: {microphone}
   4. Set Speaker to: {speaker}
   5. Test audio to confirm it works
   6. Start the translation app BEFORE joining the meeting

═══════════════════════════════════════════════════════════════";

        return instructions;
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