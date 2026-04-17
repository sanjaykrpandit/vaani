using Vaani.Authentication.Services;
using Vaani.Models;

namespace Vaani.Services;

public class LanguageInfo
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class Voice
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LanguageCode { get; set; } = "";
    public string Gender { get; set; } = "";
}

public class GenderOption
{
    public string Value { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class ClientService
{
    private readonly SessionManager _sessionManager;

    public ClientService()
    {
        _sessionManager = new SessionManager();
        _sessionManager.LoadSession();
    }

    // In future, replace with API call
    public List<LanguageInfo> GetLanguagesAsync()
    {
        var session = _sessionManager.CurrentSession;
        if (session != null && session.Configuration.AvailableLanguages != null && session.Configuration.AvailableLanguages.Count > 0)
        {
            return session.Configuration.AvailableLanguages;
        }
        else
        {
            return new List<LanguageInfo>();
        }
    }

    public List<GenderOption> GetGenderOptions()
    {
        return new List<GenderOption>
        {
            new GenderOption { Value = "Male", DisplayName = "Male" },
            new GenderOption { Value = "Female", DisplayName = "Female" }
        };
    }

    // Neural voices for Azure Speech Service
    private List<Voice> GetAllVoices()
    {
        var session = _sessionManager.CurrentSession;
        if(session != null && session.Configuration.AvailableVoices != null && session.Configuration.AvailableVoices.Count > 0)
        {
            return session.Configuration.AvailableVoices;
        }
        return new List<Voice> { };        
    }

    public Voice? GetVoiceForLanguageAndGender(string languageCode, string gender)
    {
        var allVoices = GetAllVoices();
        var voice = allVoices.FirstOrDefault(v => 
            v.LanguageCode == languageCode && 
            v.Gender.Equals(gender, System.StringComparison.OrdinalIgnoreCase));
        
        return voice;
    }
  
    /// <summary>
    /// Get translation settings from active session or fallback to default.
    /// In backend-translation mode this will include the hub URL from the session;
    /// no Azure credentials are included.
    /// </summary>
    public TranslationSettings GetTranslationSettings()
    {
        // Try to load from active session first
        if (_sessionManager.HasActiveSession() && _sessionManager.CurrentSession != null)
        {
            var settings = TranslationSettings.FromMeetingConfiguration(
                _sessionManager.CurrentSession.Configuration,
                isVendor: true
            );

            settings.SessionId = _sessionManager.CurrentSession.SessionId?.ToString();
            settings.SessionToken = _sessionManager.CurrentSession.SessionToken;

            return settings;
        }
        else
        {
            throw new System.Exception("No active meeting session found for translation settings.");
        }
    }
  
    /// <summary>
    /// Get session manager instance
    /// </summary>
    public SessionManager GetSessionManager()
    {
        return _sessionManager;
    }
}
