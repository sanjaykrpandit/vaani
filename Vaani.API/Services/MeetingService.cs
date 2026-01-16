using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;
using Vaani.API.Interfaces;
using Vaani.API.Migrations;
using Vaani.API.Models.DTOs;
using Vaani.API.Models.Entities;

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
            //start 15 minutes before validfrom
            var startFrom = meeting.ValidFrom.AddMinutes(-10);
            if (now < startFrom)
            {
                return new MeetingValidationResponse
                {
                    IsValid = false,
                    ErrorCode = "MEETING_NOT_STARTED",
                    Message = "Meeting has not started yet"
                };
            }

            var endTime = meeting.ValidUntil.AddMinutes(+15);
            if (now > endTime)
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
                request.AppVersion,
                request.UserName
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

        var languages =  await _dbContext.Languages.ToListAsync();
        var _availableLanguages = new List<LanguageInfo>();
        var _availableVoices = new List<Voice>();

        //check only Active languages
        _availableLanguages = languages.Where(lang => lang.IsActive).Select(lang => new LanguageInfo
        {
            Code = lang.LanguageCode,
            DisplayName = lang.LanguageName
        }).ToList();

        //filter voices based on available languages

        foreach (var lang in languages)
        {
            if (lang.IsActive)
            {
                var voiceMale = new Voice();
                voiceMale.Name = $"{lang.LanguageMaleNeural}";
                voiceMale.DisplayName = $"{lang.LanguageMaleNeural.Replace(lang.LanguageCode + "-", "")}";
                voiceMale.LanguageCode = lang.LanguageCode;
                voiceMale.Gender = "Male";
                _availableVoices.Add(voiceMale);
                var voiceFemale = new Voice();
                voiceFemale.Name = $"{lang.LanguageFemaleNeural}";
                voiceFemale.DisplayName = $"{lang.LanguageFemaleNeural.Replace(lang.LanguageCode + "-", "")}";
                voiceFemale.LanguageCode = lang.LanguageCode;
                voiceFemale.Gender = "Female";
                _availableVoices.Add(voiceFemale);
            }
        }

        //get default meeting language
        var meetingLanguage = await _dbContext.Languages
            .FirstOrDefaultAsync(lang => lang.LanguageCode == meeting.MeetingLanguage && lang.IsActive);


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
                OrganizerLanguage = new LanguageConfigDto
                {
                    Code = meeting.MeetingLanguage,
                    Voice = _availableVoices.FirstOrDefault(v => v.LanguageCode == meeting.MeetingLanguage && v.Gender == "Male")?.Name ?? "en-US-JennyNeural"
                }
            },

            TimeWindow = new TimeWindowDto
            {
                ValidFrom = meeting.ValidFrom.AddMinutes(-10),
                ValidUntil = meeting.ValidUntil.AddMinutes(+15)
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
            AvailableLanguages = _availableLanguages,
            AvailableVoices = _availableVoices           

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
