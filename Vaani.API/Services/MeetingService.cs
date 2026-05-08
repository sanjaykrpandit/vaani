using Microsoft.EntityFrameworkCore;
using Vaani.API.Data;
using Vaani.API.Interfaces;
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
    private readonly PasswordHashingService _passwordHashingService;
    private readonly IConfiguration _configuration;

    public MeetingService(
        VaaniDbContext dbContext,
        IEncryptionService encryptionService,
        ISessionService sessionService,
        ILogger<MeetingService> logger,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _sessionService = sessionService;
        _logger = logger;
        _configuration = configuration;
        _passwordHashingService = new PasswordHashingService();
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

            // Check password if required
            if (meeting.RequiresPassword)
            {
                if (string.IsNullOrWhiteSpace(request.Password))
                {
                    _logger.LogWarning("Password required but not provided for meeting: {MeetingId}", request.MeetingId);
                    return new MeetingValidationResponse
                    {
                        IsValid = false,
                        ErrorCode = "PASSWORD_REQUIRED",
                        Message = "This meeting requires a password"
                    };
                }

                if (!_passwordHashingService.VerifyPassword(request.Password, meeting.PasswordHash!, meeting.PasswordSalt!))
                {
                    _logger.LogWarning("Incorrect password provided for meeting: {MeetingId}", request.MeetingId);
                    return new MeetingValidationResponse
                    {
                        IsValid = false,
                        ErrorCode = "PASSWORD_INCORRECT",
                        Message = "Incorrect meeting password"
                    };
                }
            }

            // Create configuration DTO — pass the already-loaded meeting entity to avoid
            // a second DB round-trip inside GetMeetingConfigurationAsync.
            var config = await BuildConfigurationAsync(meeting);
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
                // ✅ Backend translation hub URL — desktop connects here instead of calling Azure directly
                BackendTranslationHubUrl = _configuration["Translation:HubUrl"] ?? "/hubs/translation",
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

        return await BuildConfigurationAsync(meeting);
    }

    /// <summary>
    /// Builds the configuration DTO from an already-loaded <see cref="Meeting"/> entity.
    /// Called by both <see cref="GetMeetingConfigurationAsync"/> and
    /// <see cref="ValidateMeetingAsync"/> to avoid fetching the meeting twice.
    /// </summary>
    private async Task<MeetingConfigurationDto?> BuildConfigurationAsync(Meeting meeting)
    {
        var languages = await _dbContext.Languages
            .Where(l => l.IsActive)
            .ToListAsync();

        var availableLanguages = languages.Select(lang => new LanguageInfo
        {
            Code = lang.LanguageCode,
            DisplayName = lang.LanguageName
        }).ToList();

        var availableVoices = languages.SelectMany(lang => new[]
        {
            new Voice
            {
                Name = lang.LanguageMaleNeural,
                DisplayName = lang.LanguageMaleNeural.Replace(lang.LanguageCode + "-", ""),
                LanguageCode = lang.LanguageCode,
                Gender = "Male"
            },
            new Voice
            {
                Name = lang.LanguageFemaleNeural,
                DisplayName = lang.LanguageFemaleNeural.Replace(lang.LanguageCode + "-", ""),
                LanguageCode = lang.LanguageCode,
                Gender = "Female"
            }
        }).ToList();

        // VendorLanguage and voice are configurable per deployment (appsettings.json).
        var vendorLangCode = _configuration["Translation:VendorLanguage"] ?? "hi-IN";
        var vendorVoiceName = _configuration["Translation:VendorVoice"] ?? "hi-IN-SwaraNeural";

        //target language and voice are determined by the meeting configuration
        var organizerVoice = availableVoices
            .FirstOrDefault(v => v.LanguageCode == meeting.MeetingLanguage && v.Gender == "Male")?.Name
            ?? "en-US-JennyNeural";

        return new MeetingConfigurationDto
        {
            MeetingId = meeting.MeetingId,
            MeetingName = meeting.MeetingName,
            AzureConfig = new AzureConfigDto
            {
                // Never send Azure credentials to the desktop client.
                // Backend owns Azure access end-to-end.
                SubscriptionKey = string.Empty,
                Region = string.Empty
            },
            TranslationConfig = new TranslationConfigDto
            {
                VendorLanguage = new LanguageConfigDto { Code = vendorLangCode, Voice = vendorVoiceName },
                OrganizerLanguage = new LanguageConfigDto
                {
                    Code = meeting.MeetingLanguage,
                    Voice = organizerVoice
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
            AvailableLanguages = availableLanguages,
            AvailableVoices = availableVoices,
            BackendTranslationHubUrl = _configuration["Translation:HubUrl"] ?? "/hubs/translation"
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
