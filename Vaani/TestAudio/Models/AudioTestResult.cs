namespace Vaani.TestAudio.Models;

/// <summary>
/// Result of an individual audio test phase
/// </summary>
public class AudioTestResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }
    public double AudioLevel { get; set; } // in dB
    public int? DetectedFrequency { get; set; }
    public TimeSpan TestDuration { get; set; }
}

/// <summary>
/// Device detection result
/// </summary>
public class DeviceDetectionResult
{
    public bool CableAInputFound { get; set; }
    public bool CableAOutputFound { get; set; }
    public bool CableBInputFound { get; set; }
    public bool CableBOutputFound { get; set; }
    public bool PhysicalMicFound { get; set; }
    public bool PhysicalSpeakerFound { get; set; }

    public string? CableAInputName { get; set; }
    public string? CableAOutputName { get; set; }
    public string? CableBInputName { get; set; }
    public string? CableBOutputName { get; set; }
    public string? PhysicalMicName { get; set; }
    public string? PhysicalSpeakerName { get; set; }

    public bool AllDevicesFound => 
        CableAInputFound && CableAOutputFound && 
        CableBInputFound && CableBOutputFound &&
        PhysicalMicFound && PhysicalSpeakerFound;

    public string GetSummary()
    {
        var missing = new List<string>();
        
        if (!CableAInputFound) missing.Add("CABLE-A Input");
        if (!CableAOutputFound) missing.Add("CABLE-A Output");
        if (!CableBInputFound) missing.Add("CABLE-B Input");
        if (!CableBOutputFound) missing.Add("CABLE-B Output");
        if (!PhysicalMicFound) missing.Add("Physical Microphone");
        if (!PhysicalSpeakerFound) missing.Add("Physical Speaker");

        if (missing.Count == 0)
            return "? All devices found";

        return $"? Missing devices: {string.Join(", ", missing)}";
    }
}
