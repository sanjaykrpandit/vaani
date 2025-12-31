using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Vaani.Services;

/// <summary>
/// Service for managing VB-CABLE virtual audio driver installation.
/// Checks for existing installation, downloads if needed, and installs the driver.
/// </summary>
public class VBCableDriverService
{
    private const string VB_CABLE_DOWNLOAD_URL = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip";
    private const string DRIVER_BUNDLE_FOLDER = "Drivers";
    private const string DRIVER_ZIP_NAME = "VBCABLE_Driver_Pack43.zip";
    private const string DRIVER_EXTRACT_FOLDER = "VBCableDriver";
    
    private readonly string _appDirectory;
    private readonly string _driverBundlePath;
    private readonly string _driverExtractPath;
    
    public VBCableDriverService()
    {
        _appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _driverBundlePath = Path.Combine(_appDirectory, DRIVER_BUNDLE_FOLDER);
        _driverExtractPath = Path.Combine(Path.GetTempPath(), DRIVER_EXTRACT_FOLDER);
    }

    /// <summary>
    /// Ensures VB-CABLE driver is installed. Downloads and installs if missing.
    /// </summary>
    /// <returns>True if driver is installed or successfully installed; false otherwise.</returns>
    public async Task<bool> EnsureDriverInstalledAsync()
    {
        try
        {
            // Step 1: Check if VB-CABLE is already installed
            if (IsVBCableInstalled())
            {
                Console.WriteLine("? VB-CABLE driver is already installed.");
                return true;
            }

            Console.WriteLine("? VB-CABLE driver not found. Starting installation process...");

            // Step 2: Get the driver zip file (from bundle or download)
            string driverZipPath = await GetDriverZipAsync();
            if (string.IsNullOrEmpty(driverZipPath) || !File.Exists(driverZipPath))
            {
                Console.WriteLine("? Failed to obtain VB-CABLE driver package.");
                return false;
            }

            // Step 3: Extract the driver package
            if (!ExtractDriver(driverZipPath))
            {
                Console.WriteLine("? Failed to extract VB-CABLE driver package.");
                return false;
            }

            // Step 4: Install the driver
            bool installed = await InstallDriverAsync();
            if (installed)
            {
                Console.WriteLine("? VB-CABLE driver installation completed successfully.");
                Console.WriteLine("  Note: You may need to restart your computer for changes to take effect.");
            }
            else
            {
                Console.WriteLine("? VB-CABLE driver installation failed.");
            }

            // Cleanup extracted files
            CleanupExtractedFiles();

            return installed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error during VB-CABLE driver installation: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if VB-CABLE driver is installed on the system.
    /// </summary>
    public bool IsVBCableInstalled()
    {
        try
        {
            // Method 1: Check Windows Registry for VB-CABLE driver
            if (CheckRegistryForVBCable())
            {
                return true;
            }

            // Method 2: Check using NAudio for CABLE devices
            if (CheckAudioDevicesForVBCable())
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error checking VB-CABLE installation: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check Windows Registry for VB-CABLE installation.
    /// </summary>
    private bool CheckRegistryForVBCable()
    {
        try
        {
            // Check common registry locations for VB-CABLE
            string[] registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var registryPath in registryPaths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(registryPath);
                if (key != null)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        var displayName = subKey?.GetValue("DisplayName")?.ToString() ?? "";
                        
                        if (displayName.Contains("VB-CABLE", StringComparison.OrdinalIgnoreCase) ||
                            displayName.Contains("VBCABLE", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            // Also check for driver installation in the drivers registry
            using var driverKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e96c-e325-11ce-bfc1-08002be10318}");
            if (driverKey != null)
            {
                foreach (var subKeyName in driverKey.GetSubKeyNames())
                {
                    using var subKey = driverKey.OpenSubKey(subKeyName);
                    var driverDesc = subKey?.GetValue("DriverDesc")?.ToString() ?? "";
                    
                    if (driverDesc.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
                        driverDesc.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Registry check warning: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Check audio devices using NAudio for CABLE devices.
    /// </summary>
    private bool CheckAudioDevicesForVBCable()
    {
        try
        {
            var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            
            // Check output devices
            var outputDevices = enumerator.EnumerateAudioEndPoints(
                NAudio.CoreAudioApi.DataFlow.Render, 
                NAudio.CoreAudioApi.DeviceState.All);
            
            foreach (var device in outputDevices)
            {
                if (device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Check input devices
            var inputDevices = enumerator.EnumerateAudioEndPoints(
                NAudio.CoreAudioApi.DataFlow.Capture, 
                NAudio.CoreAudioApi.DeviceState.All);
            
            foreach (var device in inputDevices)
            {
                if (device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio device check warning: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Gets the driver zip file from bundle or downloads it.
    /// </summary>
    private async Task<string> GetDriverZipAsync()
    {
        // Check if driver is bundled with the application
        var bundledDriverPath = Path.Combine(_driverBundlePath, DRIVER_ZIP_NAME);
        if (File.Exists(bundledDriverPath))
        {
            Console.WriteLine($"? Found bundled VB-CABLE driver at: {bundledDriverPath}");
            return bundledDriverPath;
        }

        Console.WriteLine("Driver not found in bundle. Downloading from official source...");

        // Download the driver
        var downloadPath = Path.Combine(Path.GetTempPath(), DRIVER_ZIP_NAME);
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            Console.WriteLine($"Downloading VB-CABLE driver from: {VB_CABLE_DOWNLOAD_URL}");
            var response = await httpClient.GetAsync(VB_CABLE_DOWNLOAD_URL);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"? Download failed with status: {response.StatusCode}");
                return string.Empty;
            }

            var fileBytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(downloadPath, fileBytes);
            
            Console.WriteLine($"? Driver downloaded successfully to: {downloadPath}");
            return downloadPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error downloading driver: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts the driver zip file.
    /// </summary>
    private bool ExtractDriver(string zipPath)
    {
        try
        {
            // Clean up any existing extraction folder
            if (Directory.Exists(_driverExtractPath))
            {
                Directory.Delete(_driverExtractPath, true);
            }

            Directory.CreateDirectory(_driverExtractPath);

            Console.WriteLine($"Extracting driver to: {_driverExtractPath}");
            ZipFile.ExtractToDirectory(zipPath, _driverExtractPath);
            
            Console.WriteLine("? Driver extracted successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error extracting driver: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Installs the VB-CABLE driver using the setup executable.
    /// </summary>
    private async Task<bool> InstallDriverAsync()
    {
        try
        {
            // Determine the correct installer based on system architecture
            bool is64Bit = Environment.Is64BitOperatingSystem;
            string installerName = is64Bit ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe";
            string installerPath = Path.Combine(_driverExtractPath, installerName);

            // Fallback: search for any setup exe
            if (!File.Exists(installerPath))
            {
                var setupFiles = Directory.GetFiles(_driverExtractPath, "*setup*.exe", SearchOption.AllDirectories);
                if (setupFiles.Length > 0)
                {
                    installerPath = setupFiles.First(f => 
                        f.Contains("x64", StringComparison.OrdinalIgnoreCase) == is64Bit);
                    
                    if (string.IsNullOrEmpty(installerPath))
                    {
                        installerPath = setupFiles[0];
                    }
                }
            }

            if (!File.Exists(installerPath))
            {
                Console.WriteLine($"? Installer not found at: {installerPath}");
                return false;
            }

            Console.WriteLine($"Running installer: {installerPath}");
            Console.WriteLine("? Administrator privileges required. Please approve the UAC prompt...");

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "-i -h", // Silent install with hide parameter
                UseShellExecute = true,
                Verb = "runas", // Request admin privileges
                WorkingDirectory = _driverExtractPath
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine("? Failed to start installer process.");
                return false;
            }

            // Wait for installation to complete (with timeout)
            var completed = await Task.Run(() => process.WaitForExit(60000)); // 60 second timeout
            
            if (!completed)
            {
                Console.WriteLine("? Installation is taking longer than expected...");
                await Task.Run(() => process.WaitForExit()); // Wait indefinitely
            }

            if (process.ExitCode == 0)
            {
                Console.WriteLine("? VB-CABLE driver installed successfully.");
                return true;
            }
            else
            {
                Console.WriteLine($"? Installer exited with code: {process.ExitCode}");
                // Some installers return non-zero even on success, so verify installation
                await Task.Delay(2000); // Give system time to register the driver
                return IsVBCableInstalled();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error installing driver: {ex.Message}");
            
            // If automated install fails, provide manual instructions
            Console.WriteLine("\n???????????????????????????????????????????????????????");
            Console.WriteLine("Manual Installation Required:");
            Console.WriteLine($"1. Navigate to: {_driverExtractPath}");
            Console.WriteLine("2. Right-click the setup executable and run as administrator");
            Console.WriteLine("3. Follow the installation wizard");
            Console.WriteLine("4. Restart the application after installation");
            Console.WriteLine("???????????????????????????????????????????????????????\n");
            
            return false;
        }
    }

    /// <summary>
    /// Cleans up extracted driver files.
    /// </summary>
    private void CleanupExtractedFiles()
    {
        try
        {
            if (Directory.Exists(_driverExtractPath))
            {
                Directory.Delete(_driverExtractPath, true);
                Console.WriteLine("? Cleanup completed.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not cleanup extracted files: {ex.Message}");
        }
    }
}
