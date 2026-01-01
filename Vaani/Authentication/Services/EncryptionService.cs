using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vaani.Authentication.Models;

namespace Vaani.Authentication.Services;

/// <summary>
/// Service for encrypting and decrypting meeting configurations using AES-256
/// </summary>
public class EncryptionService
{
    private const string EncryptionPrefix = "VAANI_ENC_v1_";
    private const int KeySize = 256;
    private const int BlockSize = 128;

    /// <summary>
    /// Decrypt an encrypted configuration blob from the API
    /// Format: VAANI_ENC_v1_<base64_encrypted_data>
    /// The encrypted data contains: [IV(16 bytes) + Encrypted JSON + HMAC(32 bytes)]
    /// </summary>
    /// <param name="encryptedConfig">Encrypted configuration string from API</param>
    /// <param name="encryptionKey">AES encryption key (will be provided by API or derived)</param>
    /// <returns>Decrypted MeetingConfiguration object</returns>
    public MeetingConfiguration DecryptConfiguration(string encryptedConfig, byte[] encryptionKey)
    {
        if (string.IsNullOrWhiteSpace(encryptedConfig))
            throw new ArgumentException("Encrypted configuration cannot be empty", nameof(encryptedConfig));

        if (encryptionKey == null || encryptionKey.Length != 32)
            throw new ArgumentException("Encryption key must be 32 bytes (256 bits)", nameof(encryptionKey));

        // Remove prefix if present
        var base64Data = encryptedConfig.StartsWith(EncryptionPrefix)
            ? encryptedConfig.Substring(EncryptionPrefix.Length)
            : encryptedConfig;

        // Decode base64
        byte[] encryptedBytes;
        try
        {
            encryptedBytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Invalid encrypted configuration format", ex);
        }

        // Extract components: IV (16) + Encrypted Data (variable) + HMAC (32)
        if (encryptedBytes.Length < 48) // Minimum: 16 (IV) + 1 (data) + 32 (HMAC)
            throw new InvalidOperationException("Encrypted data is too short");

        var iv = new byte[16];
        var hmac = new byte[32];
        var cipherText = new byte[encryptedBytes.Length - 48];

        Array.Copy(encryptedBytes, 0, iv, 0, 16);
        Array.Copy(encryptedBytes, 16, cipherText, 0, cipherText.Length);
        Array.Copy(encryptedBytes, encryptedBytes.Length - 32, hmac, 0, 32);

        // Verify HMAC for integrity
        if (!VerifyHmac(iv, cipherText, hmac, encryptionKey))
            throw new InvalidOperationException("Configuration integrity check failed - possible tampering detected");

        // Decrypt using AES-256-CBC
        string decryptedJson;
        using (var aes = Aes.Create())
        {
            aes.KeySize = KeySize;
            aes.BlockSize = BlockSize;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encryptionKey;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var msDecrypt = new MemoryStream(cipherText);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt, Encoding.UTF8);
            
            decryptedJson = srDecrypt.ReadToEnd();
        }

        // Deserialize JSON to MeetingConfiguration
        var config = JsonSerializer.Deserialize<MeetingConfiguration>(decryptedJson);
        if (config == null)
            throw new InvalidOperationException("Failed to deserialize meeting configuration");

        return config;
    }

    /// <summary>
    /// Verify HMAC-SHA256 signature for data integrity
    /// </summary>
    private bool VerifyHmac(byte[] iv, byte[] cipherText, byte[] providedHmac, byte[] key)
    {
        using var hmacAlgorithm = new HMACSHA256(key);
        
        // Combine IV and ciphertext for HMAC calculation
        var dataToVerify = new byte[iv.Length + cipherText.Length];
        Array.Copy(iv, 0, dataToVerify, 0, iv.Length);
        Array.Copy(cipherText, 0, dataToVerify, iv.Length, cipherText.Length);

        var computedHmac = hmacAlgorithm.ComputeHash(dataToVerify);

        // Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(computedHmac, providedHmac);
    }

    /// <summary>
    /// Encrypt a configuration for testing purposes (typically done on backend)
    /// </summary>
    public string EncryptConfiguration(MeetingConfiguration config, byte[] encryptionKey)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        if (encryptionKey == null || encryptionKey.Length != 32)
            throw new ArgumentException("Encryption key must be 32 bytes (256 bits)", nameof(encryptionKey));

        // Serialize to JSON
        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
        var json = JsonSerializer.Serialize(config, jsonOptions);
        var plainTextBytes = Encoding.UTF8.GetBytes(json);

        byte[] iv;
        byte[] cipherText;

        // Encrypt using AES-256-CBC
        using (var aes = Aes.Create())
        {
            aes.KeySize = KeySize;
            aes.BlockSize = BlockSize;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encryptionKey;
            aes.GenerateIV();
            iv = aes.IV;

            using var encryptor = aes.CreateEncryptor();
            using var msEncrypt = new MemoryStream();
            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            {
                csEncrypt.Write(plainTextBytes, 0, plainTextBytes.Length);
                csEncrypt.FlushFinalBlock();
            }
            cipherText = msEncrypt.ToArray();
        }

        // Compute HMAC
        byte[] hmac;
        using (var hmacAlgorithm = new HMACSHA256(encryptionKey))
        {
            var dataToSign = new byte[iv.Length + cipherText.Length];
            Array.Copy(iv, 0, dataToSign, 0, iv.Length);
            Array.Copy(cipherText, 0, dataToSign, iv.Length, cipherText.Length);
            hmac = hmacAlgorithm.ComputeHash(dataToSign);
        }

        // Combine: IV + CipherText + HMAC
        var combined = new byte[iv.Length + cipherText.Length + hmac.Length];
        Array.Copy(iv, 0, combined, 0, iv.Length);
        Array.Copy(cipherText, 0, combined, iv.Length, cipherText.Length);
        Array.Copy(hmac, 0, combined, iv.Length + cipherText.Length, hmac.Length);

        // Encode to Base64 and add prefix
        var base64 = Convert.ToBase64String(combined);
        return EncryptionPrefix + base64;
    }

    /// <summary>
    /// Generate a random 256-bit encryption key
    /// </summary>
    public static byte[] GenerateKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var key = new byte[32]; // 256 bits
        rng.GetBytes(key);
        return key;
    }

    /// <summary>
    /// Derive encryption key from a password (for testing/demo purposes)
    /// In production, the key should come from the API response
    /// </summary>
    public static byte[] DeriveKeyFromPassword(string password, byte[] salt)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        if (salt == null || salt.Length < 16)
            throw new ArgumentException("Salt must be at least 16 bytes", nameof(salt));

        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            iterations: 100000,
            HashAlgorithmName.SHA256
        );

        return pbkdf2.GetBytes(32); // 256 bits
    }
}
