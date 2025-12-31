using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vaani.Authentication.Services;

/// <summary>
/// Service for securely storing sensitive data using Windows DPAPI (Data Protection API)
/// Data is encrypted and tied to the current Windows user account
/// </summary>
public class SecureStorageService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vaani"
    );

    private const string SessionFileName = "session.dat";
    private static readonly string SessionFilePath = Path.Combine(AppDataFolder, SessionFileName);

    /// <summary>
    /// Initialize storage directory
    /// </summary>
    public SecureStorageService()
    {
        EnsureStorageDirectoryExists();
    }

    /// <summary>
    /// Save data securely using DPAPI
    /// </summary>
    /// <typeparam name="T">Type of data to store</typeparam>
    /// <param name="key">Storage key/identifier</param>
    /// <param name="data">Data to encrypt and store</param>
    public void SaveSecure<T>(string key, T data)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty", nameof(key));

        // Serialize to JSON
        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        var json = JsonSerializer.Serialize(data, jsonOptions);
        var plainTextBytes = Encoding.UTF8.GetBytes(json);

        // Encrypt using DPAPI (user-scoped)
        var encryptedBytes = ProtectedData.Protect(
            plainTextBytes,
            GetEntropy(key),
            DataProtectionScope.CurrentUser
        );

        // Save to file
        var filePath = GetFilePath(key);
        File.WriteAllBytes(filePath, encryptedBytes);
    }

    /// <summary>
    /// Load and decrypt data using DPAPI
    /// </summary>
    /// <typeparam name="T">Type of data to retrieve</typeparam>
    /// <param name="key">Storage key/identifier</param>
    /// <returns>Decrypted data or default if not found</returns>
    public T? LoadSecure<T>(string key) where T : class
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be empty", nameof(key));

        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
            return null;

        try
        {
            // Read encrypted data
            var encryptedBytes = File.ReadAllBytes(filePath);

            // Decrypt using DPAPI
            var plainTextBytes = ProtectedData.Unprotect(
                encryptedBytes,
                GetEntropy(key),
                DataProtectionScope.CurrentUser
            );

            var json = Encoding.UTF8.GetString(plainTextBytes);

            // Deserialize from JSON
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (CryptographicException)
        {
            // Decryption failed (wrong user, corrupted data)
            return null;
        }
        catch (JsonException)
        {
            // Invalid JSON
            return null;
        }
    }

    /// <summary>
    /// Check if secure data exists for a key
    /// </summary>
    public bool Exists(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return File.Exists(GetFilePath(key));
    }

    /// <summary>
    /// Delete secure data for a key
    /// </summary>
    public void Delete(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        var filePath = GetFilePath(key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Clear all stored secure data
    /// </summary>
    public void ClearAll()
    {
        if (Directory.Exists(AppDataFolder))
        {
            Directory.Delete(AppDataFolder, recursive: true);
        }
        EnsureStorageDirectoryExists();
    }

    /// <summary>
    /// Get entropy (additional encryption data) based on key
    /// </summary>
    private byte[] GetEntropy(string key)
    {
        // Use key as additional entropy for DPAPI
        // This makes it harder to decrypt without knowing the exact key
        return Encoding.UTF8.GetBytes($"VAANI_{key}_v1");
    }

    /// <summary>
    /// Get file path for a storage key
    /// </summary>
    private string GetFilePath(string key)
    {
        // Sanitize key for filename
        var sanitizedKey = key.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
        return Path.Combine(AppDataFolder, $"{sanitizedKey}.dat");
    }

    /// <summary>
    /// Ensure storage directory exists
    /// </summary>
    private void EnsureStorageDirectoryExists()
    {
        if (!Directory.Exists(AppDataFolder))
        {
            Directory.CreateDirectory(AppDataFolder);
        }
    }

    /// <summary>
    /// Get the application data folder path
    /// </summary>
    public static string GetStoragePath() => AppDataFolder;
}
