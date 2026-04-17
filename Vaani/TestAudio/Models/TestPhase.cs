namespace Vaani.TestAudio.Models;

/// <summary>
/// Represents the different phases of the audio loopback test
/// </summary>
public enum TestPhase
{
    NotStarted,
    DeviceDetection,
    CableATest,
    CableBTest,
    Completed,
    Failed
}
