using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;

namespace Vaani.API.Services;

/// <summary>
/// Service for encryption/decryption operations
/// </summary>
public class EncryptionService : IEncryptionService
{
    private readonly IConfiguration _configuration;
    private readonly byte[] _encryptionKey;

    public EncryptionService(IConfiguration configuration)
    {
        _configuration = configuration;
        var keyString = _configuration["Encryption:AesKey"] ?? throw new InvalidOperationException("Encryption key not configured");
        _encryptionKey = Encoding.UTF8.GetBytes(keyString.PadRight(32)[..32]); // Ensure 32 bytes for AES-256
    }

    public string EncryptConfiguration(MeetingConfigurationDto configuration)
    {
        try
        {
            var json = JsonSerializer.Serialize(configuration);
            var plainTextBytes = Encoding.UTF8.GetBytes(json);

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            
            // Write IV first
            ms.Write(aes.IV, 0, aes.IV.Length);
            
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(plainTextBytes, 0, plainTextBytes.Length);
                cs.FlushFinalBlock();
            }

            var encryptedBytes = ms.ToArray();
            var base64 = Convert.ToBase64String(encryptedBytes);
            
            return $"VAANI_ENC_v1_{base64}";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to encrypt configuration", ex);
        }
    }

    public MeetingConfigurationDto DecryptConfiguration(string encryptedData)
    {
        try
        {
            if (!encryptedData.StartsWith("VAANI_ENC_v1_"))
                throw new ArgumentException("Invalid encryption format");

            var base64 = encryptedData.Replace("VAANI_ENC_v1_", "");
            var encryptedBytes = Convert.FromBase64String(base64);

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;

            // Extract IV (first 16 bytes)
            var iv = new byte[16];
            Array.Copy(encryptedBytes, 0, iv, 0, 16);
            aes.IV = iv;

            // Extract encrypted data (rest of bytes)
            var cipherText = new byte[encryptedBytes.Length - 16];
            Array.Copy(encryptedBytes, 16, cipherText, 0, cipherText.Length);

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(cipherText);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(cs);
            
            var json = reader.ReadToEnd();
            var config = JsonSerializer.Deserialize<MeetingConfigurationDto>(json);
            
            return config ?? throw new InvalidOperationException("Failed to deserialize configuration");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to decrypt configuration", ex);
        }
    }

    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
