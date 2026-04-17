using System.Security.Cryptography;
using System.Text;

namespace Vaani.API.Services;

/// <summary>
/// Service for securely hashing and verifying passwords
/// </summary>
public class PasswordHashingService
{
    private const int SaltSize = 32; // 256 bits
    private const int HashSize = 32; // 256 bits
    private const int Iterations = 100000; // PBKDF2 iterations

    /// <summary>
    /// Hash a password using PBKDF2 with a random salt
    /// </summary>
    /// <param name="password">Plain text password</param>
    /// <returns>Tuple of (hash, salt) as base64 strings</returns>
    public (string hash, string salt) HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        // Generate a random salt
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

        // Hash the password with PBKDF2
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize
        );

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>
    /// Verify a password against a stored hash and salt
    /// </summary>
    /// <param name="password">Plain text password to verify</param>
    /// <param name="storedHash">Stored hash as base64 string</param>
    /// <param name="storedSalt">Stored salt as base64 string</param>
    /// <returns>True if password matches, false otherwise</returns>
    public bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(storedSalt))
            return false;

        try
        {
            // Convert stored hash and salt from base64
            byte[] hashBytes = Convert.FromBase64String(storedHash);
            byte[] saltBytes = Convert.FromBase64String(storedSalt);

            // Hash the provided password with the stored salt
            byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize
            );

            // Compare the computed hash with the stored hash using constant-time comparison
            return CryptographicOperations.FixedTimeEquals(computedHash, hashBytes);
        }
        catch
        {
            // If any exception occurs (invalid base64, etc.), return false
            return false;
        }
    }
}
