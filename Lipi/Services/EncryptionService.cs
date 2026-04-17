using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lipi.Models;

namespace Lipi.Services;

public class EncryptionService
{
    public MeetingConfiguration DecryptConfig(string encryptedData, string key)
    {
        if (!encryptedData.StartsWith("VAANI_ENC_v1_", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid encryption format.");

        var base64 = encryptedData.Replace("VAANI_ENC_v1_", string.Empty, StringComparison.Ordinal);
        var encryptedBytes = Convert.FromBase64String(base64);

        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key.PadRight(32)[..32]);

        var iv = new byte[16];
        Array.Copy(encryptedBytes, 0, iv, 0, 16);
        aes.IV = iv;

        var cipherText = new byte[encryptedBytes.Length - 16];
        Array.Copy(encryptedBytes, 16, cipherText, 0, cipherText.Length);

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(cipherText);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var reader = new StreamReader(cs);
        var json = reader.ReadToEnd();

        return JsonSerializer.Deserialize<MeetingConfiguration>(json)
            ?? throw new InvalidOperationException("Failed to deserialize configuration.");
    }
}