using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;
using Vaani.DriverInstallation.Models;

namespace Vaani.DriverInstallation.Services;

/// <summary>
/// Service for managing VB-CABLE virtual audio driver installation.
/// </summary>
public class DriverInstallationService
{
    private const string VB_CABLE_PRIMARY_DOWNLOAD_URL = "https://vaani-rtt.s3.ap-south-1.amazonaws.com/drivers/VBCable_AB_PackSetup.zip";
    private const string VB_CABLE_FALLBACK_DOWNLOAD_URL = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip";
    private const string DRIVER_BUNDLE_FOLDER = "Drivers";
    private const string DRIVER_ZIP_NAME = "VBCABLE_Driver_Pack43.zip";
    private const string DRIVER_EXTRACT_FOLDER = "VBCableDriver";

    private readonly string _appDirectory;
    private readonly string _driverBundlePath;
    private readonly string _driverExtractPath;
    private string _downloadedDriverPath = string.Empty;

    public DriverInstallationService()
    {
        _appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _driverBundlePath = Path.Combine(_appDirectory, DRIVER_BUNDLE_FOLDER);
        _driverExtractPath = Path.Combine(Path.GetTempPath(), DRIVER_EXTRACT_FOLDER);
    }

    /// <summary>
    /// Checks if VB-CABLE driver is installed on the system.
    /// </summary>
    public bool IsVBCableInstalled()
    {
        try
        {
            // Method 1: Check Windows Registry
            if (CheckRegistryForVBCable())
            {
                return true;
            }

            // Method 2: Check audio devices
            if (CheckAudioDevicesForVBCable())
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads the VB-CABLE driver package.
    /// </summary>
    public async Task<(bool success, string message)> DownloadDriverAsync(IProgress<int>? progress = null)
    {
        try
        {
            // Check if driver is bundled
            var bundledDriverPath = Path.Combine(_driverBundlePath, DRIVER_ZIP_NAME);
            if (File.Exists(bundledDriverPath))
            {
                _downloadedDriverPath = bundledDriverPath;
                progress?.Report(100);
                return (true, "Using bundled driver package");
            }

            // Download the driver
            _downloadedDriverPath = Path.Combine(Path.GetTempPath(), DRIVER_ZIP_NAME);

            // Try primary URL first (localhost)
            var downloadResult = await TryDownloadFromUrlAsync(VB_CABLE_PRIMARY_DOWNLOAD_URL, _downloadedDriverPath, progress);

            if (downloadResult.success)
            {
                return (true, "Driver downloaded successfully from primary source");
            }

            // Fallback to secondary URL if primary fails
            downloadResult = await TryDownloadFromUrlAsync(VB_CABLE_FALLBACK_DOWNLOAD_URL, _downloadedDriverPath, progress);

            if (downloadResult.success)
            {
                return (true, "Driver downloaded successfully from fallback source");
            }

            return (false, $"Download failed from all sources. Last error: {downloadResult.message}");
        }
        catch (Exception ex)
        {
            return (false, $"Download error: {ex.Message}");
        }
    }

    private async Task<(bool success, string message)> TryDownloadFromUrlAsync(string url, string destinationPath, IProgress<int>? progress)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"HTTP {response.StatusCode}");
            }

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var buffer = new byte[8192];
            var totalRead = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var progressPercentage = (int)((totalRead * 100) / totalBytes);
                    progress?.Report(progressPercentage);
                }
            }

            return (true, "Download successful");
        }
        catch (HttpRequestException ex)
        {
            return (false, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return (false, "Download timeout");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Extracts the driver package.
    /// </summary>
    public async Task<(bool success, string message)> ExtractDriverAsync(IProgress<int>? progress = null)
    {
        try
        {
            if (string.IsNullOrEmpty(_downloadedDriverPath) || !File.Exists(_downloadedDriverPath))
            {
                return (false, "Driver package not found");
            }

            // Clean up existing extraction folder
            if (Directory.Exists(_driverExtractPath))
            {
                Directory.Delete(_driverExtractPath, true);
            }

            Directory.CreateDirectory(_driverExtractPath);

            // Extract with progress
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(_downloadedDriverPath);
                var totalEntries = archive.Entries.Count;
                var extractedCount = 0;

                foreach (var entry in archive.Entries)
                {
                    var destinationPath = Path.Combine(_driverExtractPath, entry.FullName);

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // Directory entry
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        // File entry
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                        entry.ExtractToFile(destinationPath, true);
                    }

                    extractedCount++;
                    progress?.Report((extractedCount * 100) / totalEntries);
                }
            });

            return (true, "Driver extracted successfully");
        }
        catch (Exception ex)
        {
            return (false, $"Extraction error: {ex.Message}");
        }
    }

    /// <summary>
    /// Installs the VB-CABLE driver.
    /// </summary>
    public async Task<(bool success, string message, bool requiresRestart)> InstallDriverAsync()
    {
        try
        {
            // Find the installer
            bool is64Bit = Environment.Is64BitOperatingSystem;
            string installerName = is64Bit ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe";
            string installerPath = Path.Combine(_driverExtractPath, installerName);

            // Fallback: search for any setup exe
            if (!File.Exists(installerPath))
            {
                var setupFiles = Directory.GetFiles(_driverExtractPath, "*setup*.exe", SearchOption.AllDirectories);
                if (setupFiles.Length > 0)
                {
                    installerPath = setupFiles.FirstOrDefault(f =>
                        f.Contains("x64", StringComparison.OrdinalIgnoreCase) == is64Bit) ?? setupFiles[0];
                }
            }

            if (!File.Exists(installerPath))
            {
                return (false, "Installer executable not found", false);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "-i -h", // Silent install
                UseShellExecute = true,
                Verb = "runas", // Request admin privileges
                WorkingDirectory = _driverExtractPath
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, "Failed to start installer process", false);
            }

            // Wait for installation with timeout
            var completed = await Task.Run(() => process.WaitForExit(60000));

            if (!completed)
            {
                await Task.Run(() => process.WaitForExit());
            }

            // Verify installation
            await Task.Delay(2000); // Give system time to register
            bool isInstalled = IsVBCableInstalled();

            if (isInstalled)
            {
                return (true, "Driver installed successfully", true);
            }
            else if (process.ExitCode == 0)
            {
                return (true, "Installation completed", true);
            }
            else
            {
                return (false, $"Installation failed with exit code: {process.ExitCode}", false);
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, "Installation cancelled or access denied", false);
        }
        catch (Exception ex)
        {
            return (false, $"Installation error: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Cleans up temporary files.
    /// </summary>
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_driverExtractPath))
            {
                Directory.Delete(_driverExtractPath, true);
            }

            if (!string.IsNullOrEmpty(_downloadedDriverPath) &&
                File.Exists(_downloadedDriverPath) &&
                !_downloadedDriverPath.Contains(DRIVER_BUNDLE_FOLDER))
            {
                File.Delete(_downloadedDriverPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private bool CheckRegistryForVBCable()
    {
        try
        {
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
        catch
        {
            // Ignore registry errors
        }

        return false;
    }

    private bool CheckAudioDevicesForVBCable()
    {
        try
        {
            var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

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
        catch
        {
            // Ignore audio device errors
        }

        return false;
    }
}