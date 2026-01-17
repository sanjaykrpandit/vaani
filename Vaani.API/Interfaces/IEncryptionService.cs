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
    string EncryptConfiguration(MeetingConfigurationDto configuration, string key);
    
    /// <summary>
    /// Decrypt meeting configuration
    /// </summary>
    MeetingConfigurationDto DecryptConfiguration(string encryptedData, string key);
    
    /// <summary>
    /// Encrypt meeting token (combines meetingId and publicToken)
    /// </summary>
    string EncryptMeetingToken(string meetingId, string publicToken);
    
    /// <summary>
    /// Decrypt meeting token and extract meetingId and publicToken
    /// </summary>
    (string meetingId, string publicToken)? DecryptMeetingToken(string encryptedToken);
    
    /// <summary>
    /// Hash password using BCrypt
    /// </summary>
    string HashPassword(string password);
    
    /// <summary>
    /// Verify hashed password
    /// </summary>
    bool VerifyPassword(string password, string hash);
}
