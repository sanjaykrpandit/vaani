using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Vaani.API.Data;
using Vaani.API.Interfaces;
using Vaani.API.Services;
using Vaani.API.Hubs;
using Microsoft.AspNetCore.StaticFiles;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Add IHttpContextAccessor for services that need current user info
builder.Services.AddHttpContextAccessor();

// Configure PostgreSQL Database with retry logic
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
builder.Services.AddDbContext<VaaniDbContext>(options =>
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
});

// Register application services
builder.Services.AddScoped<IVaaniRepository, VaaniRepository>();
builder.Services.AddScoped<IMeetingService, MeetingService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IAzureSubscriptionService, AzureSubscriptionService>();
// Register language service implementation
builder.Services.AddScoped<ILanguageService, LanguageService>();
// Register translation service (singleton - manages long-lived per-session Azure SDK instances)
builder.Services.AddSingleton<ITranslationService, TranslationService>();
// Add SignalR for real-time translation hub
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 128 * 1024; // 128 KB max message
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Configure JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "VaaniAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "VaaniDesktopApp";

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
            // Use 'role' as the default RoleClaimType (common), but we also map other claim names on token validated
            NameClaimType = "name",
            RoleClaimType = "role"
        };

        // Map other role claim names (e.g. 'roles', 'realm_access', 'resource_access') into the configured RoleClaimType so Authorize(Roles=...) works
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                try
                {
                    JwtSecurityToken? jwt = null;

                    // Prefer the already-parsed SecurityToken if it's a JwtSecurityToken
                    if (ctx.SecurityToken is JwtSecurityToken parsedJwt)
                    {
                        jwt = parsedJwt;
                    }
                    else
                    {
                        // Fallback: try to read raw token string from Authorization header or query string
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

                    var roleClaimType = identity.RoleClaimType ?? ClaimTypes.Role;

                    // 1) Direct role-like claims
                    var directRoleTypes = new[] { "role", "roles", ClaimTypes.Role, "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" };
                    foreach (var c in jwt.Claims.Where(c => directRoleTypes.Contains(c.Type, StringComparer.OrdinalIgnoreCase)))
                    {
                        if (!identity.HasClaim(roleClaimType, c.Value))
                            identity.AddClaim(new Claim(roleClaimType, c.Value));
                    }

                    // 2) realm_access.roles (Keycloak style)
                    if (jwt.Payload.TryGetValue("realm_access", out var realmObj) && realmObj != null)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(realmObj.ToString() ?? "{}");
                            if (doc.RootElement.TryGetProperty("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var r in rolesElement.EnumerateArray())
                                {
                                    var role = r.GetString();
                                    if (!string.IsNullOrWhiteSpace(role) && !identity.HasClaim(roleClaimType, role))
                                        identity.AddClaim(new Claim(roleClaimType, role!));
                                }
                            }
                        }
                        catch { /* ignore parse errors */ }
                    }

                    // 3) resource_access -> { client: { roles: [...] } }
                    if (jwt.Payload.TryGetValue("resource_access", out var resObj) && resObj != null)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(resObj.ToString() ?? "{}");
                            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var clientProp in doc.RootElement.EnumerateObject())
                                {
                                    if (clientProp.Value.TryGetProperty("roles", out var clientRoles) && clientRoles.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var r in clientRoles.EnumerateArray())
                                        {
                                            var role = r.GetString();
                                            if (!string.IsNullOrWhiteSpace(role) && !identity.HasClaim(roleClaimType, role))
                                                identity.AddClaim(new Claim(roleClaimType, role!));
                                        }
                                    }
                                }
                            }
                        }
                        catch { /* ignore parse errors */ }
                    }
                }
                catch
                {
                    // swallow - don't fail authentication because of mapping
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Vaani API",
        Version = "v1",
        Description = "API for Vaani real-time translation application",
        Contact = new OpenApiContact
        {
            Name = "Vaani Team",
            Email = "support@vaani.com"
        }
    });

    // Use HTTP Bearer scheme so Swagger can send JWT tokens
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("VaaniPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Log startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting Vaani API...");
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);


// Serve static files
app.UseStaticFiles();
var provider = new FileExtensionContentTypeProvider();
provider.Mappings[".application"] = "application/x-ms-application";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

// Test database connection (optional - won't crash if DB is down)
try
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<VaaniDbContext>();
        var canConnect = await dbContext.Database.CanConnectAsync();
        
        if (canConnect)
        {
            logger.LogInformation("? Database connection successful");
        }
        else
        {
            logger.LogWarning("?? Cannot connect to database. API will start but database operations will fail.");
        }
    }
}
catch (Exception ex)
{
    logger.LogWarning(ex, "?? Database connection test failed. API will start but database operations will fail.");
}

// Configure the HTTP request pipeline

// HTTPS Redirection should come first
app.UseHttpsRedirection();

// Enable CORS
app.UseCors("VaaniPolicy");

// Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Vaani API V1");
    c.RoutePrefix = "swagger"; // Change from empty to "swagger"
});

logger.LogInformation("?? Swagger UI available at: https://localhost:7020/swagger");
logger.LogInformation("?? API Health check at: https://localhost:7020/health");

if (app.Environment.IsDevelopment())
{
    logger.LogInformation("?? Swagger UI available at: https://localhost:7020");
}
else
{
    logger.LogInformation("?? Swagger UI enabled");
}

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Map Controllers
app.MapControllers();

// Map SignalR Translation Hub
app.MapHub<Vaani.API.Hubs.TranslationHub>("/hubs/translation");

// Health check endpoint
app.MapGet("/health", async (VaaniDbContext dbContext) =>
{
    var dbHealthy = false;
    try
    {
        dbHealthy = await dbContext.Database.CanConnectAsync();
    }
    catch { }
    
    return Results.Ok(new
    {
        status = dbHealthy ? "healthy" : "degraded",
        timestamp = DateTime.UtcNow,
        database = dbHealthy ? "connected" : "disconnected",
        message = dbHealthy ? "All systems operational" : "API running but database unavailable"
    });
})
.WithName("HealthCheck")
.WithTags("Health");

logger.LogInformation("? Vaani API started successfully");

app.Run();
