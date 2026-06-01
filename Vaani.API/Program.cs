using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Vaani.API.Data;
using Vaani.API.Hubs;
using Vaani.API.Interfaces;
using Vaani.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Add IHttpContextAccessor for services that need current user info
builder.Services.AddHttpContextAccessor();

// Configure PostgreSQL Database with retry logic
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
Action<DbContextOptionsBuilder> configureDb = options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    });

    // Log SQL queries in development
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
};

builder.Services.AddDbContext<VaaniDbContext>(configureDb);

// Register application services
builder.Services.AddScoped<IVaaniRepository, VaaniRepository>();
builder.Services.AddScoped<IMeetingService, MeetingService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IAzureSubscriptionService, AzureSubscriptionService>();
// Register language service implementation
builder.Services.AddScoped<ILanguageService, LanguageService>();
builder.Services.AddScoped<ILipiDirectAccessService, LipiDirectAccessService>();
builder.Services.AddScoped<IConversationalDictionaryService, ConversationalDictionaryService>();
builder.Services.AddHttpClient<IConversationalRewriteFallbackService, ConversationalRewriteFallbackService>();
// Register translation service (singleton - manages long-lived per-session Azure SDK instances)
builder.Services.AddSingleton<ITranslationService, TranslationService>();
builder.Services.AddSingleton<ILipiTranslationService, LipiTranslationService>();
// Background service: tears down sessions whose meetings have expired or that have gone silent
builder.Services.AddHostedService<StaleSessionCleanupService>();
// Add SignalR for real-time translation hub
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 128 * 1024; // 128 KB max message
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Rate limiting — protect meeting validation endpoint from brute-force
builder.Services.AddRateLimiter(options =>
{
    // 10 requests per minute per IP for meeting-join operations
    options.AddSlidingWindowLimiter("meeting-join", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 4;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Configure JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "VaaniAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "VaaniDesktopApp";

// Warn when obvious placeholder secrets are used.
if (jwtSecret.Contains("YourSuperSecretKey", StringComparison.OrdinalIgnoreCase))
{
    builder.Logging.AddConsole();
    var startupLogger = LoggerFactory.Create(lb => lb.AddConsole()).CreateLogger("Startup");
    startupLogger.LogWarning("JWT secret appears to be a placeholder. Configure a real secret via secure configuration before production use.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "name",
            RoleClaimType = "role"
        };

        options.Events = new JwtBearerEvents
        {
            // ✅ SignalR: read JWT from query-string ?access_token= during WebSocket upgrade
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/hubs") || path.StartsWithSegments("/api/hubs")))
                {
                    ctx.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                try
                {
                    JwtSecurityToken? jwt = null;

                    if (ctx.SecurityToken is JwtSecurityToken parsedJwt)
                    {
                        jwt = parsedJwt;
                    }
                    else
                    {
                        string? token = null;

                        if (ctx.Request.Headers.TryGetValue("Authorization", out var auth) && auth.Count > 0)
                        {
                            var authHeader = auth.FirstOrDefault();
                            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                                token = authHeader.Substring("Bearer ".Length).Trim();
                        }

                        if (string.IsNullOrEmpty(token) && ctx.Request.Query.TryGetValue("access_token", out var at) && at.Count > 0)
                        {
                            token = at.FirstOrDefault();
                        }

                        if (!string.IsNullOrEmpty(token))
                        {
                            try
                            {
                                jwt = new JwtSecurityTokenHandler().ReadJwtToken(token!);
                            }
                            catch
                            {
                                // ignore parse errors
                            }
                        }
                    }

                    if (jwt == null)
                        return Task.CompletedTask;

                    var identity = ctx.Principal?.Identity as ClaimsIdentity;
                    if (identity == null) return Task.CompletedTask;

                    // Restored missing role parsing logic to securely handle token identity maps
                    var roleClaims = jwt.Claims.Where(c => c.Type == "role" || c.Type == "roles");
                    foreach (var roleClaim in roleClaims)
                    {
                        if (!identity.HasClaim(identity.RoleClaimType, roleClaim.Value))
                        {
                            identity.AddClaim(new Claim(identity.RoleClaimType, roleClaim.Value));
                        }
                    }
                }
                catch
                {
                    // ignore parse errors
                }
                return Task.CompletedTask;
            }
        };
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Add development exceptions if required
}

// -------------------------------------------------------------
// CLICKONCE LAUNCHER FIX: ROUTING /api/launcher TO PHYSICAL /launcher
// -------------------------------------------------------------
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".dll"] = "application/octet-stream";
provider.Mappings[".manifest"] = "application/x-ms-manifest";
provider.Mappings[".application"] = "application/x-ms-application";
provider.Mappings[".deploy"] = "application/octet-stream";

// 1. Serve default static files from standard wwwroot location
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

// 2. Map virtual route "/api/launcher" to physical directory "/wwwroot/launcher"
string physicalLauncherPath = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "launcher");

if (Directory.Exists(physicalLauncherPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(physicalLauncherPath),
        RequestPath = "/api/launcher", // Intercepts the mismatched application manifest request safely
        ContentTypeProvider = provider
    });
}
// -------------------------------------------------------------

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<TranslationHub>("/hubs/translation");
app.MapHub<TranslationHub>("/api/hubs/translation");
app.MapHub<LipiHub>("/hubs/lipi");
app.MapHub<LipiHub>("/api/hubs/lipi");

app.Run();
