using Vaani.API.Models.DTOs;

namespace Vaani.API.Interfaces;

/// <summary>
/// Interface for encryption/decryption operations
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypt meeting configuration
    /// </summary>
    string EncryptConfiguration(MeetingConfigurationDto configuration);
    
    /// <summary>
    /// Decrypt meeting configuration
    /// </summary>
    MeetingConfigurationDto DecryptConfiguration(string encryptedData);
    
    /// <summary>
    /// Hash password using BCrypt
    /// </summary>
    string HashPassword(string password);
    
    /// <summary>
    /// Verify hashed password
    /// </summary>
    bool VerifyPassword(string password, string hash);
}
