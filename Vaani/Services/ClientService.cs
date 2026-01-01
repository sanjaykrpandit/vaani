using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    }

    // In future, replace with API call
    public async Task<List<LanguageInfo>> GetLanguagesAsync()
    {
        await Task.Delay(10); // Simulate async
        return new List<LanguageInfo>
        {
            new LanguageInfo { Code = "en-US", DisplayName = "English (US)" },
            new LanguageInfo { Code = "en-GB", DisplayName = "English (UK)" },
            new LanguageInfo { Code = "hi-IN", DisplayName = "Hindi (India)" },
            new LanguageInfo { Code = "es-ES", DisplayName = "Spanish (Spain)" },
            new LanguageInfo { Code = "fr-FR", DisplayName = "French (France)" },
            new LanguageInfo { Code = "de-DE", DisplayName = "German (Germany)" },
            new LanguageInfo { Code = "ja-JP", DisplayName = "Japanese (Japan)" },
            new LanguageInfo { Code = "zh-CN", DisplayName = "Chinese (Simplified)" }
        };
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
        return new List<Voice>
        {
            // English (US)
            new Voice { Name = "en-US-GuyNeural", DisplayName = "Guy (Natural)", LanguageCode = "en-US", Gender = "Male" },
            new Voice { Name = "en-US-DavisNeural", DisplayName = "Davis (Natural)", LanguageCode = "en-US", Gender = "Male" },
            new Voice { Name = "en-US-JasonNeural", DisplayName = "Jason (Natural)", LanguageCode = "en-US", Gender = "Male" },
            new Voice { Name = "en-US-AriaNeural", DisplayName = "Aria (Natural)", LanguageCode = "en-US", Gender = "Female" },
            new Voice { Name = "en-US-JennyNeural", DisplayName = "Jenny (Natural)", LanguageCode = "en-US", Gender = "Female" },
            new Voice { Name = "en-US-NancyNeural", DisplayName = "Nancy (Natural)", LanguageCode = "en-US", Gender = "Female" },

            // English (UK)
            new Voice { Name = "en-GB-RyanNeural", DisplayName = "Ryan (Natural)", LanguageCode = "en-GB", Gender = "Male" },
            new Voice { Name = "en-GB-ThomasNeural", DisplayName = "Thomas (Natural)", LanguageCode = "en-GB", Gender = "Male" },
            new Voice { Name = "en-GB-LibbyNeural", DisplayName = "Libby (Natural)", LanguageCode = "en-GB", Gender = "Female" },
            new Voice { Name = "en-GB-SoniaNeural", DisplayName = "Sonia (Natural)", LanguageCode = "en-GB", Gender = "Female" },

            // Hindi (India)
            new Voice { Name = "hi-IN-MadhurNeural", DisplayName = "Madhur (Natural)", LanguageCode = "hi-IN", Gender = "Male" },
            new Voice { Name = "hi-IN-SwaraNeural", DisplayName = "Swara (Natural)", LanguageCode = "hi-IN", Gender = "Female" },

            // Spanish (Spain)
            new Voice { Name = "es-ES-AlvaroNeural", DisplayName = "Alvaro (Natural)", LanguageCode = "es-ES", Gender = "Male" },
            new Voice { Name = "es-ES-ElviraNeural", DisplayName = "Elvira (Natural)", LanguageCode = "es-ES", Gender = "Female" },

            // French (France)
            new Voice { Name = "fr-FR-HenriNeural", DisplayName = "Henri (Natural)", LanguageCode = "fr-FR", Gender = "Male" },
            new Voice { Name = "fr-FR-DeniseNeural", DisplayName = "Denise (Natural)", LanguageCode = "fr-FR", Gender = "Female" },

            // German (Germany)
            new Voice { Name = "de-DE-ConradNeural", DisplayName = "Conrad (Natural)", LanguageCode = "de-DE", Gender = "Male" },
            new Voice { Name = "de-DE-KatjaNeural", DisplayName = "Katja (Natural)", LanguageCode = "de-DE", Gender = "Female" },

            // Japanese (Japan)
            new Voice { Name = "ja-JP-KeitaNeural", DisplayName = "Keita (Natural)", LanguageCode = "ja-JP", Gender = "Male" },
            new Voice { Name = "ja-JP-NanamiNeural", DisplayName = "Nanami (Natural)", LanguageCode = "ja-JP", Gender = "Female" },

            // Chinese (Simplified)
            new Voice { Name = "zh-CN-YunxiNeural", DisplayName = "Yunxi (Natural)", LanguageCode = "zh-CN", Gender = "Male" },
            new Voice { Name = "zh-CN-XiaoxiaoNeural", DisplayName = "Xiaoxiao (Natural)", LanguageCode = "zh-CN", Gender = "Female" }
        };
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
    /// Get translation settings from active session or fallback to default
    /// </summary>
    public TranslationSettings GetTranslationSettings()
    {
        // Try to load from active session first
        if (_sessionManager.HasActiveSession() && _sessionManager.CurrentSession != null)
        {
            return TranslationSettings.FromMeetingConfiguration(
                _sessionManager.CurrentSession.Configuration,
                isVendor: true
            );
        }

        // Fallback to hardcoded settings (for development/testing only)
        return GetHardcodedTranslationSettings();
    }

    /// <summary>
    /// Get hardcoded translation settings (DEPRECATED - for testing only)
    /// In production, this should not be used. Use meeting-based authentication instead.
    /// </summary>
    [System.Obsolete("Use meeting-based authentication instead. This is for testing only.")]
    public TranslationSettings GetHardcodedTranslationSettings()
    {
        //return new TranslationSettings()
        //{
        //    AzureRegion = "eastus2",
        //    AzureSubscriptionKey = "BnsKkEvkgEN4Muh48WOKOWQtT96WpJCNVcjPqBFClKpIyEAu1JBtJQQJ99BKACHYHv6XJ3w3AAAAACOGiTqU",
        //    SourceLanguage = "hi-IN",
        //    TargetLanguage = "en-US",
        //    SourceVoice = "hi-IN-MadhurNeural",
        //    TargetVoice = "en-US-GuyNeural",
        //    IsFromMeetingSession = false
        //};

        return new TranslationSettings()
        {
            AzureRegion = "southeastasia", // Verify this matches your Azure resource
            AzureSubscriptionKey = "6kziD36R2nkcgEeidao1mr5xgYYSo4GTCQQCq4oyILajnc5JmPPuJQQJ99BLACqBBLyXJ3w3AAAAACOGdIV8", // Get fresh key from Azure Portal
            SourceLanguage = "hi-IN",
            TargetLanguage = "en-US",
            SourceVoice = "hi-IN-MadhurNeural",
            TargetVoice = "en-US-GuyNeural"
        };
    }

    /// <summary>
    /// Check if there's an active meeting session
    /// </summary>
    public bool HasActiveMeetingSession()
    {
        return _sessionManager.HasActiveSession();
    }

    /// <summary>
    /// Get session manager instance
    /// </summary>
    public SessionManager GetSessionManager()
    {
        return _sessionManager;
    }
}
