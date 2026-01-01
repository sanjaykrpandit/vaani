# VB-CABLE Driver Auto-Installation

## Overview

Vaani now includes automatic VB-CABLE driver installation functionality. The application will detect if VB-CABLE is installed on the system and automatically download and install it if missing.

## How It Works

### 1. **Detection**
The application checks for VB-CABLE installation in two ways:
- **Windows Registry**: Scans uninstall registry keys and driver registry entries
- **Audio Devices**: Uses NAudio to detect CABLE audio devices

### 2. **Installation Flow**
When you launch Vaani after successful login, the app will:

1. ? Check if VB-CABLE is already installed
   - If installed ? Continue to main window
   - If not installed ? Proceed to installation

2. ?? Obtain Driver Package
   - First checks if driver is bundled in `Drivers/VBCABLE_Driver_Pack43.zip`
   - If not bundled ? Downloads from official VB-Audio website
   - Download URL: `https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip`

3. ?? Extract Driver
   - Extracts to temporary folder: `%TEMP%\VBCableDriver`

4. ?? Install Driver
   - Automatically runs the appropriate installer (x64 or x86)
   - Requests administrator privileges (UAC prompt will appear)
   - Installs with silent parameters: `-i -h`

5. ?? Cleanup
   - Removes temporary extracted files
   - Proceeds to main application window

## Bundling Drivers (Optional)

To avoid downloading drivers every time for new installations:

1. Download VB-CABLE driver: https://vb-audio.com/Cable/
2. Create a `Drivers` folder in your application directory
3. Place the downloaded `VBCABLE_Driver_Pack43.zip` in the `Drivers` folder
4. The application will use the bundled driver instead of downloading

### Directory Structure for Bundled Drivers
```
vconsole/
??? vconsole.exe
??? Drivers/
?   ??? VBCABLE_Driver_Pack43.zip
??? ...other files
```

## User Experience

### Successful Installation
```
? VB-CABLE driver not found. Starting installation process...
? Found bundled VB-CABLE driver at: C:\...\Drivers\VBCABLE_Driver_Pack43.zip
Extracting driver to: C:\Users\...\AppData\Local\Temp\VBCableDriver
? Driver extracted successfully.
Running installer: C:\Users\...\AppData\Local\Temp\VBCableDriver\VBCABLE_Setup_x64.exe
? Administrator privileges required. Please approve the UAC prompt...
? VB-CABLE driver installed successfully.
  Note: You may need to restart your computer for changes to take effect.
? Cleanup completed.
```

### Already Installed
```
? VB-CABLE driver is already installed.
```

### Manual Installation Fallback
If automated installation fails, the application displays manual instructions:
```
???????????????????????????????????????????????????????
Manual Installation Required:
1. Navigate to: C:\Users\...\AppData\Local\Temp\VBCableDriver
2. Right-click the setup executable and run as administrator
3. Follow the installation wizard
4. Restart the application after installation
???????????????????????????????????????????????????????
```

## Technical Details

### Class: `VBCableDriverService`
Location: `Services/VBCableDriverService.cs`

#### Key Methods:
- **`EnsureDriverInstalledAsync()`**: Main entry point for driver installation
- **`IsVBCableInstalled()`**: Checks if VB-CABLE is installed
- **`GetDriverZipAsync()`**: Gets driver from bundle or downloads it
- **`ExtractDriver()`**: Extracts the driver package
- **`InstallDriverAsync()`**: Installs the driver with admin privileges

### Integration Points:
1. **App.axaml.cs**: Checks driver on session restore (existing valid session)
2. **MeetingLoginViewModel.cs**: Checks driver after successful login validation

## Important Notes

### Administrator Privileges
- Driver installation requires administrator privileges
- A UAC (User Account Control) prompt will appear
- User must approve the installation

### System Architecture
- Automatically detects 64-bit vs 32-bit Windows
- Installs the appropriate driver version

### Network Requirements
- If driver is not bundled, requires internet connection to download
- Download size: ~2-3 MB
- Downloads from official VB-Audio website

### Restart Recommendation
- While not always required, restarting the computer after driver installation is recommended
- Ensures the driver is fully registered with the system

## Troubleshooting

### Driver Installation Fails
1. Try running the application as administrator
2. Manually install VB-CABLE from: https://vb-audio.com/Cable/
3. Restart the application after manual installation

### Download Fails
1. Check your internet connection
2. Verify firewall/antivirus isn't blocking the download
3. Consider bundling the driver with your application (see above)

### Audio Routing Not Working
1. Verify VB-CABLE is installed: Check Windows Sound Settings
2. Restart your computer if driver was just installed
3. Ensure your meeting app (Teams/Zoom) is configured to use CABLE devices

## License & Attribution

VB-CABLE is developed by VB-Audio Software: https://vb-audio.com/

Vaani uses VB-CABLE for virtual audio routing to enable real-time translation functionality.

---

**Created by**: Vaani Development Team  
**Last Updated**: December 2024
