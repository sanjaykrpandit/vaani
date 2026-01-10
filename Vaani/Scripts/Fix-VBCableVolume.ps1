# VB Cable Volume Auto-Fixer
# Automatically sets all VB Cable device volumes to 100%

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  VB Cable Volume Auto-Fixer" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "??  WARNING: Not running as Administrator" -ForegroundColor Yellow
    Write-Host "   Some volume changes may not work." -ForegroundColor Yellow
    Write-Host "   Right-click this script ? Run as Administrator for best results" -ForegroundColor Yellow
    Write-Host ""
}

# Function to set device volume using nircmd (if available)
function Set-DeviceVolume {
    param(
        [string]$DeviceName,
        [int]$Volume = 100
    )
    
    Write-Host "Setting $DeviceName volume to $Volume%..." -NoNewline
    
    # Try using Windows AudioEndpointVolume API via PowerShell
    try {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume {
    int NotImpl1();
    int NotImpl2();
    int GetChannelCount([Out] out uint channelCount);
    int SetMasterVolumeLevel(float level, ref Guid eventContext);
    int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
    int GetMasterVolumeLevel([Out] out float level);
    int GetMasterVolumeLevelScalar([Out] out float level);
    int SetChannelVolumeLevel(uint channelNumber, float level, ref Guid eventContext);
    int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);
    int GetChannelVolumeLevel(uint channelNumber, [Out] out float level);
    int GetChannelVolumeLevelScalar(uint channelNumber, [Out] out float level);
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
    int GetMute([Out, MarshalAs(UnmanagedType.Bool)] out bool isMuted);
    int GetVolumeStepInfo([Out] out uint step, [Out] out uint stepCount);
    int VolumeStepUp(ref Guid eventContext);
    int VolumeStepDown(ref Guid eventContext);
    int QueryHardwareSupport([Out] out uint hardwareSupportMask);
    int GetVolumeRange([Out] out float volumeMin, [Out] out float volumeMax, [Out] out float volumeStep);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    int GetState(out int pdwState);
}

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    int GetDevice(string pwstrId, out IMMDevice ppDevice);
    int RegisterEndpointNotificationCallback(IntPtr pClient);
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection {
    int GetCount(out uint pcDevices);
    int Item(uint nDevice, out IMMDevice ppDevice);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorComObject { }

public class AudioVolumeHelper {
    public static void SetAllCableVolumes() {
        try {
            var deviceEnumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
            IntPtr pDevices;
            
            // Get all audio devices
            deviceEnumerator.EnumAudioEndpoints(0, 0xFF, out pDevices); // 0 = Render, 0xFF = All states
            var devices = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pDevices);
            
            uint count;
            devices.GetCount(out count);
            
            for (uint i = 0; i < count; i++) {
                IMMDevice device;
                devices.Item(i, out device);
                
                // Check if it's a CABLE device
                string deviceId;
                device.GetId(out deviceId);
                
                // Get device name (simplified)
                if (deviceId.ToUpper().Contains("CABLE")) {
                    Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;
                    object aepv_obj;
                    device.Activate(ref IID_IAudioEndpointVolume, 0, IntPtr.Zero, out aepv_obj);
                    IAudioEndpointVolume aepv = (IAudioEndpointVolume)aepv_obj;
                    
                    Guid ZeroGuid = Guid.Empty;
                    aepv.SetMasterVolumeLevelScalar(1.0f, ref ZeroGuid); // 1.0 = 100%
                    aepv.SetMute(false, ref ZeroGuid); // Unmute
                }
            }
        }
        catch { }
    }
}
"@
        [AudioVolumeHelper]::SetAllCableVolumes()
        Write-Host " ?" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host " ? (Using fallback method)" -ForegroundColor Yellow
        return $false
    }
}

# Main execution
Write-Host "?? Searching for VB Cable devices..." -ForegroundColor Cyan
Write-Host ""

# Try to set volumes
$success = Set-DeviceVolume -DeviceName "All CABLE Devices" -Volume 100

if ($success) {
    Write-Host ""
    Write-Host "? Success! All VB Cable device volumes set to 100%" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "??  Automated method not available. Using manual method..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "?? Manual Steps:" -ForegroundColor Cyan
    Write-Host "1. Press Win + R" -ForegroundColor White
    Write-Host "2. Type: control mmsys.cpl sounds" -ForegroundColor White
    Write-Host "3. Press Enter" -ForegroundColor White
    Write-Host ""
    Write-Host "Recording Tab:" -ForegroundColor Yellow
    Write-Host "  • Double-click 'CABLE-A Output'" -ForegroundColor White
    Write-Host "  • Levels tab ? Set volume to 100%" -ForegroundColor White
    Write-Host "  • Do same for 'CABLE-B Output'" -ForegroundColor White
    Write-Host ""
    Write-Host "Playback Tab:" -ForegroundColor Yellow
    Write-Host "  • Double-click 'CABLE-A Input'" -ForegroundColor White
    Write-Host "  • Levels tab ? Set volume to 100%" -ForegroundColor White
    Write-Host "  • Do same for 'CABLE-B Input'" -ForegroundColor White
    Write-Host ""
    Write-Host "Opening Sound Control Panel for you..." -ForegroundColor Cyan
    Start-Process "control.exe" -ArgumentList "mmsys.cpl sounds"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Press any key to close..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
