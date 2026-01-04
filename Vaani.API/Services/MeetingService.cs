using Vaani.API.Interfaces;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;

namespace Vaani.API.Services;

/// <summary>
/// Service for meeting-related operations
/// </summary>
public class MeetingService : IMeetingService
{
    private readonly VaaniDbContext _dbContext;
    private readonly IEncryptionService _encryptionService;
    private readonly ISessionService _sessionService;
    private readonly ILogger<MeetingService> _logger;

    public MeetingService(
        VaaniDbContext dbContext,
        IEncryptionService encryptionService,
        ISessionService sessionService,
        ILogger<MeetingService> logger)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task<MeetingValidationResponse> ValidateMeetingAsync(MeetingValidationRequest request)
    {
        try
        {
            _logger.LogInformation("Validating meeting: {MeetingId} for device: {DeviceId}",
                request.MeetingId, request.DeviceId);

            // Get meeting from database with Azure subscription
            var meeting = await _dbContext.Meetings
                .Include(m => m.AzureSubscription)
                .FirstOrDefaultAsync(m => m.MeetingId == request.MeetingId.ToUpperInvariant());

            if (meeting == null)
            {
                _logger.LogWarning("Meeting not found: {MeetingId}", request.MeetingId);
                return new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "MEETING_NOT_FOUND",
                    Message = "Meeting ID not found or expired"
                };
            }

            // Check if meeting is active
            if (!meeting.IsActive)
            {
                return new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "MEETING_INACTIVE",
                    Message = "Meeting is no longer active"
                };
            }

            // Check time window
            var now = DateTime.UtcNow;
            if (now < meeting.ValidFrom)
            {
                return new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "MEETING_NOT_STARTED",
                    Message = "Meeting has not started yet"
                };
            }

            if (now > meeting.ValidUntil)
            {
                return new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "MEETING_EXPIRED",
                    Message = "Meeting has expired"
                };
            }

            // Create configuration DTO
            var config = await GetMeetingConfigurationAsync(meeting.MeetingId);
            if (config == null)
            {
                return new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "CONFIG_ERROR",
                    Message = "Failed to retrieve meeting configuration"
                };
            }

            var (success, sessionId, accessToken, message) = await _sessionService.CreateSessionAsync(
                request.MeetingId,
                request.DeviceId,
                request.DeviceName,
                request.AppVersion
            );

            if (!success)
            {
                return new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "SESSION_CREATION_FAILED",
                    Message = message
                };
            }

            // Encrypt configuration
            var encryptedConfig = _encryptionService.EncryptConfiguration(config, request.DeviceId);

            // Calculate remaining time
            var remainingTime = meeting.ValidUntil - now;
            var remainingMinutes = (int)remainingTime.TotalMinutes;

            _logger.LogInformation("Meeting validation successful: {MeetingId}", meeting.MeetingId);

            return new MeetingValidationResponse
            {
                SessionToken = accessToken ?? "",
                IsValid = true,
                MeetingName = meeting.MeetingName,
                EncryptedConfig = encryptedConfig,
                ValidUntil = meeting.ValidUntil,
                RemainingMinutes = remainingMinutes,
                Features = new MeetingFeaturesDto
                {
                    AllowReconnect = true,
                    HeartbeatIntervalSeconds = 60,
                    EnableLocalCache = false
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating meeting: {MeetingId}", request.MeetingId);
            return new MeetingValidationResponse
            {
                IsValid = false,
                ErrorCode = "INTERNAL_ERROR",
                Message = "An error occurred while validating the meeting"
            };
        }
    }

    public async Task<MeetingConfigurationDto?> GetMeetingConfigurationAsync(string meetingId)
    {
        var meeting = await _dbContext.Meetings
            .Include(m => m.AzureSubscription)
            .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());

        if (meeting == null) return null;

        return new MeetingConfigurationDto
        {
            MeetingId = meeting.MeetingId,
            MeetingName = meeting.MeetingName,
            AzureConfig = new AzureConfigDto
            {
                SubscriptionKey = meeting.AzureSubscription.SubscriptionKey,
                Region = meeting.AzureSubscription.Region
            },
            TranslationConfig = new TranslationConfigDto
            {
                VendorLanguage = new LanguageConfigDto { Code = "hi-IN", Voice = "hi-IN-SwaraNeural" },
                OrganizerLanguage = new LanguageConfigDto { Code = "en-US", Voice = "en-US-JennyNeural" }
            },
            TimeWindow = new TimeWindowDto
            {
                ValidFrom = meeting.ValidFrom,
                ValidUntil = meeting.ValidUntil
            },
            Features = new MeetingFeaturesDto
            {
                AllowReconnect = true,
                HeartbeatIntervalSeconds = 60,
                EnableLocalCache = false
            },
            Metadata = new ConfigMetadataDto
            {
                ApiVersion = "v1",
                EncryptedAt = DateTime.UtcNow,
                ConfigVersion = 1
            },
            AvailableLanguages = new List<LanguageInfo>
            {
                new LanguageInfo { Code = "en-US", DisplayName = "English (US)" },
                new LanguageInfo { Code = "en-GB", DisplayName = "English (UK)" },
                new LanguageInfo { Code = "hi-IN", DisplayName = "Hindi (India)" },
                new LanguageInfo { Code = "es-ES", DisplayName = "Spanish (Spain)" },
                new LanguageInfo { Code = "fr-FR", DisplayName = "French (France)" },
                new LanguageInfo { Code = "de-DE", DisplayName = "German (Germany)" },
                new LanguageInfo { Code = "ja-JP", DisplayName = "Japanese (Japan)" },
                new LanguageInfo { Code = "zh-CN", DisplayName = "Chinese (Simplified)" }
            },
            AvailableVoices = new List<Voice>
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

            }

        };
    }

    public async Task<bool> IsMeetingValidAsync(string meetingId)
    {
        var meeting = await _dbContext.Meetings
            .FirstOrDefaultAsync(m => m.MeetingId == meetingId.ToUpperInvariant());
            
        if (meeting == null || !meeting.IsActive) return false;

        var now = DateTime.UtcNow;
        return now >= meeting.ValidFrom && now <= meeting.ValidUntil;
    }
}
