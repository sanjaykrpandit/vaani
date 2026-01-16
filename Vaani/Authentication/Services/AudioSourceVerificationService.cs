using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Vaani.Services;

namespace Vaani.Authentication.Services;

/// <summary>
/// Service to verify audio source is from Microsoft Teams before allowing authentication
/// </summary>
public class AudioSourceVerificationService
{
    // Allowed process names for Teams
    private static readonly string[] ALLOWED_TEAMS_PROCESSES = new[]
    {
        "Teams",               // Classic Teams
        "ms-teams"             // New Teams
    };

    // Teams web URLs to check in browser tabs
    private static readonly string[] TEAMS_WEB_URLS = new[]
    {
        "teams.microsoft.com",
        "teams.live.com"
    };

    /// <summary>
    /// Verify that Microsoft Teams is running (native or web browser)
    /// </summary>
    public async Task<AudioSourceVerificationResult> VerifyAudioSourceAsync()
    {
        var result = new AudioSourceVerificationResult();

        try
        {
            // Step 1: Check if VB-CABLE A+B is installed (run in background to avoid UI freeze)
            var hasVBCable = await Task.Run(() => IsVBCableABInstalled());
            
            if (!hasVBCable)
            {
                result.IsValid = false;
                result.ErrorCode = "VBCABLE_NOT_FOUND";
                result.ErrorMessage = "VB-CABLE A+B is not installed. Please install VB-CABLE driver first.";
                return result;
            }

            result.VBCableInstalled = true;

            // Step 2: Check if Teams native app has an active window
            var activeTeamsInfo = GetActiveTeamsWindow();

            if (activeTeamsInfo.IsActive)
            {
                result.IsValid = true;
                result.TeamsDetected = true;
                result.TeamsProcessName = activeTeamsInfo.ProcessName;
                result.SuccessMessage = $"✅ Microsoft Teams detected: {activeTeamsInfo.ProcessName} (Active Window: {activeTeamsInfo.WindowTitle})";
                return result;
            }

            // Step 3: Check if Teams web is open in browser
            var browserTeamsInfo = await CheckTeamsInBrowserAsync();

            if (browserTeamsInfo.IsTeamsOpen)
            {
                result.IsValid = true;
                result.TeamsDetected = true;
                result.TeamsProcessName = $"{browserTeamsInfo.BrowserName} (Teams Web)";
                result.SuccessMessage = $"✅ Microsoft Teams detected in {browserTeamsInfo.BrowserName}";
                return result;
            }

            // Step 4: Neither native nor web Teams detected with active windows
            result.IsValid = false;
            result.ErrorCode = "TEAMS_NOT_DETECTED";
            result.ErrorMessage = "Vanni is accessable only with Teams Meeting, please join a meeting on teams to start translation.";

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorCode = "VERIFICATION_ERROR";
            result.ErrorMessage = $"Audio verification failed: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// Check if VB-CABLE A+B driver is installed
    /// </summary>
    private bool IsVBCableABInstalled()
    {
        try
        {
            // Use singleton instance to benefit from caching
            return DeviceService.Instance.HasCableABDevices();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get active Teams window (checks for visible, responsive windows)
    /// </summary>
    private ActiveTeamsInfo GetActiveTeamsWindow()
    {
        var result = new ActiveTeamsInfo();

        try
        {
            var allProcesses = Process.GetProcesses();

            foreach (var process in allProcesses)
            {
                try
                {
                    // Check if it's a Teams process
                    if (!ALLOWED_TEAMS_PROCESSES.Any(allowed =>
                        process.ProcessName.Equals(allowed, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var mainWindowHandle = process.MainWindowHandle;

                    // Skip if no window or window is not visible
                    if (mainWindowHandle == IntPtr.Zero || !IsWindowVisible(mainWindowHandle))
                    {
                        Debug.WriteLine($"Teams process found but window not visible: {process.ProcessName}");
                        continue;
                    }

                    var windowTitle = GetWindowTitle(mainWindowHandle);

                    // Valid Teams window should have a title
                    if (string.IsNullOrEmpty(windowTitle))
                    {
                        Debug.WriteLine($"Teams window has no title: {process.ProcessName}");
                        continue;
                    }

                    Debug.WriteLine($"✅ Active Teams window found: {process.ProcessName} - {windowTitle}");

                    result.IsActive = true;
                    result.ProcessName = process.ProcessName;
                    result.WindowTitle = windowTitle;
                    return result;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking process {process.ProcessName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting active Teams windows: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Check if Teams web is open in any browser
    /// </summary>
    private async Task<BrowserTeamsInfo> CheckTeamsInBrowserAsync()
    {
        return await Task.Run(() =>
        {
            var result = new BrowserTeamsInfo();

            try
            {
                // Check Edge
                if (IsBrowserRunning("msedge"))
                {
                    Debug.WriteLine("Edge browser detected");
                    if (IsTeamsOpenInBrowser("msedge"))
                    {
                        result.IsTeamsOpen = true;
                        result.BrowserName = "Microsoft Edge";
                        return result;
                    }
                }

                // Check Chrome
                if (IsBrowserRunning("chrome"))
                {
                    Debug.WriteLine("Chrome browser detected");
                    if (IsTeamsOpenInBrowser("chrome"))
                    {
                        result.IsTeamsOpen = true;
                        result.BrowserName = "Google Chrome";
                        return result;
                    }
                }

                // Check Firefox
                if (IsBrowserRunning("firefox"))
                {
                    Debug.WriteLine("Firefox browser detected");
                    if (IsTeamsOpenInBrowser("firefox"))
                    {
                        result.IsTeamsOpen = true;
                        result.BrowserName = "Mozilla Firefox";
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking browser: {ex.Message}");
            }

            return result;
        });
    }

    /// <summary>
    /// Check if browser process is running
    /// </summary>
    private bool IsBrowserRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if Teams URL is open in browser using Windows API
    /// </summary>
    private bool IsTeamsOpenInBrowser(string browserProcess)
    {
        try
        {
            var processes = Process.GetProcessesByName(browserProcess);

            foreach (var process in processes)
            {
                try
                {
                    var mainWindowHandle = process.MainWindowHandle;

                    // Skip if no window or not visible
                    if (mainWindowHandle == IntPtr.Zero || !IsWindowVisible(mainWindowHandle))
                        continue;

                    var windowTitle = GetWindowTitle(mainWindowHandle);

                    Debug.WriteLine($"  Window title: {windowTitle}");

                    if (!string.IsNullOrEmpty(windowTitle))
                    {
                        // Check if window title contains Teams-related keywords
                        if (windowTitle.Contains("Microsoft Teams", StringComparison.OrdinalIgnoreCase) ||
                            windowTitle.Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
                            windowTitle.Contains("teams.live.com", StringComparison.OrdinalIgnoreCase) ||
                            (windowTitle.Contains("Teams", StringComparison.OrdinalIgnoreCase) &&
                             (windowTitle.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                              windowTitle.Contains("Call", StringComparison.OrdinalIgnoreCase) ||
                              windowTitle.Contains("Chat", StringComparison.OrdinalIgnoreCase))))
                        {
                            Debug.WriteLine($"    ✅ Teams detected in visible browser window!");
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"  Error checking process window: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking browser windows: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Get window title using Windows API
    /// </summary>
    private string GetWindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return string.Empty;

        try
        {
            const int maxLength = 256;
            var sb = new StringBuilder(maxLength);

            if (GetWindowText(hWnd, sb, maxLength) > 0)
            {
                return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting window title: {ex.Message}");
        }

        return string.Empty;
    }

    // Windows API declarations
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}

/// <summary>
/// Result of audio source verification
/// </summary>
public class AudioSourceVerificationResult
{
    public bool IsValid { get; set; }
    public bool VBCableInstalled { get; set; }
    public bool TeamsDetected { get; set; }
    public string? TeamsProcessName { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

/// <summary>
/// Information about active Teams window
/// </summary>
public class ActiveTeamsInfo
{
    public bool IsActive { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
}

/// <summary>
/// Information about Teams in browser
/// </summary>
public class BrowserTeamsInfo
{
    public bool IsTeamsOpen { get; set; }
    public string BrowserName { get; set; } = string.Empty;
}