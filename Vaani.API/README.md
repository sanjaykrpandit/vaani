# Vaani API

ASP.NET Core 8 Web API for Vaani real-time translation application.

## ??? Architecture

```
Vaani.API/
??? Controllers/           # API endpoints
?   ??? MeetingsController.cs
?   ??? SessionsController.cs
??? Services/             # Business logic
?   ??? MeetingService.cs
?   ??? SessionService.cs
?   ??? JwtTokenService.cs
?   ??? EncryptionService.cs
??? Interfaces/           # Service contracts
?   ??? IMeetingService.cs
?   ??? ISessionService.cs
?   ??? IJwtTokenService.cs
?   ??? IEncryptionService.cs
?   ??? IVaaniRepository.cs
??? Data/                 # Database layer
?   ??? VaaniDbContext.cs
?   ??? VaaniRepository.cs
??? Models/               # Data models
?   ??? Entities/        # Database entities
?   ?   ??? Meeting.cs
?   ?   ??? Session.cs
?   ?   ??? SessionLog.cs
?   ??? DTOs/            # Data transfer objects
?       ??? MeetingValidationRequest.cs
?       ??? MeetingValidationResponse.cs
?       ??? HeartbeatDto.cs
?       ??? EndSessionDto.cs
?       ??? MeetingConfigurationDto.cs
??? Database/            # Database scripts
    ??? schema.sql
    ??? README.md
```

## ?? Quick Start

### Prerequisites

- .NET 8 SDK
- PostgreSQL 12+
- Visual Studio 2022 or VS Code

### 1. Clone Repository

```bash
git clone https://github.com/sanjaykrpandit/vaani
cd vaani/Vaani.API
```

### 2. Setup Database

See [Database/README.md](Database/README.md) for detailed instructions.

Quick setup:
```bash
# Create database
psql -U postgres -c "CREATE DATABASE vaani_db;"

# Run schema
psql -U postgres -d vaani_db -f Database/schema.sql
```

### 3. Configure Settings

Update `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=vaani_db;Username=postgres;Password=your_password"
  },
  "Jwt": {
    "Secret": "YourSuperSecretKeyForJWTTokenGenerationAtLeast32Characters",
    "Issuer": "VaaniAPI",
    "Audience": "VaaniDesktopApp",
    "ExpirationHours": 4
  },
  "Encryption": {
    "AesKey": "YourAES256EncryptionKey32Chars!"
  }
}
```

### 4. Restore NuGet Packages

```bash
dotnet restore
```

### 5. Run the API

```bash
dotnet run
```

The API will start at:
- HTTPS: `https://localhost:7001`
- HTTP: `http://localhost:5001`
- Swagger UI: `https://localhost:7001` (root URL)

## ?? API Endpoints

### 1. Meeting Validation

**Endpoint:** `POST /api/meetings/validate`

Validate a meeting ID and create a session.

**Request:**
```json
{
  "meetingId": "VAANI-TEST-001",
  "deviceId": "ABC123XYZ",
  "deviceName": "DESKTOP-USER",
  "appVersion": "1.0.0",
  "timestamp": "2025-12-31T10:00:00Z"
}
```

**Response (Success):**
```json
{
  "isValid": true,
  "meetingName": "Test Meeting - English to Hindi",
  "encryptedConfig": "VAANI_ENC_v1_<base64_data>",
  "validUntil": "2025-12-31T14:00:00Z",
  "remainingMinutes": 240,
  "sessionToken": "eyJhbGci...",
  "features": {
    "allowReconnect": true,
    "heartbeatIntervalSeconds": 60,
    "enableLocalCache": false
  }
}
```

### 2. Session Heartbeat

**Endpoint:** `POST /api/sessions/heartbeat`

**Headers:**
```
Authorization: Bearer <session_token>
```

**Request:**
```json
{
  "timestamp": "2025-12-31T10:05:00Z"
}
```

**Response:**
```json
{
  "success": true,
  "remainingMinutes": 235,
  "message": "Heartbeat successful"
}
```

### 3. End Session

**Endpoint:** `POST /api/sessions/end`

**Headers:**
```
Authorization: Bearer <session_token>
```

**Request:**
```json
{
  "timestamp": "2025-12-31T12:00:00Z",
  "statistics": {
    "meetingId": "VAANI-TEST-001",
    "meetingName": "Test Meeting",
    "durationMinutes": 120,
    "startedAt": "2025-12-31T10:00:00Z",
    "deviceId": "ABC123XYZ"
  }
}
```

**Response:**
```json
{
  "success": true,
  "message": "Session ended successfully"
}
```

### 4. Health Check

**Endpoint:** `GET /health`

**Response:**
```json
{
  "status": "healthy",
  "timestamp": "2025-12-31T10:00:00Z"
}
```

## ?? Security

### JWT Authentication

- Algorithm: HS256
- Expiration: 4 hours (configurable)
- Claims: `meetingId`, `deviceId`, `jti`, `iat`

### Configuration Encryption

- Algorithm: AES-256-GCM
- Format: `VAANI_ENC_v1_<base64_encrypted_data>`
- IV prepended to encrypted data

### Password Hashing

- Algorithm: BCrypt
- Work factor: 11 (default)

## ?? Testing with Swagger

1. Start the API: `dotnet run`
2. Open browser: `https://localhost:7001`
3. Try the `/api/meetings/validate` endpoint with sample meeting ID: `VAANI-TEST-001`
4. Copy the `sessionToken` from the response
5. Click "Authorize" button and enter: `Bearer <your_token>`
6. Test `/api/sessions/heartbeat` and `/api/sessions/end` endpoints

## ?? Deployment

### AWS Elastic Beanstalk

1. Install EB CLI:
```bash
pip install awsebcli
```

2. Initialize EB:
```bash
eb init -p "64bit Amazon Linux 2023 v3.0.0 running .NET 8" vaani-api --region us-east-1
```

3. Create environment:
```bash
eb create vaani-api-prod
```

4. Deploy:
```bash
dotnet publish -c Release
eb deploy
```

### Docker (for AWS ECS)

```bash
docker build -t vaani-api .
docker tag vaani-api:latest <account>.dkr.ecr.us-east-1.amazonaws.com/vaani-api:latest
docker push <account>.dkr.ecr.us-east-1.amazonaws.com/vaani-api:latest
```

## ?? Configuration

### Environment Variables

For production, use environment variables instead of `appsettings.json`:

```bash
export ConnectionStrings__PostgreSQL="Host=...;Database=vaani_db;..."
export Jwt__Secret="your_production_secret"
export Encryption__AesKey="your_production_key"
```

### AWS Secrets Manager

Recommended for production:

```csharp
// In Program.cs, add before builder.Build()
if (builder.Environment.IsProduction())
{
    builder.Configuration.AddSecretsManager();
}
```

## ?? Monitoring

### Logging

- Default: Console and Debug output
- Production: Add Serilog or Application Insights

### Metrics

Key endpoints for monitoring:
- `/health` - Health check
- Database connection pool metrics
- JWT token validation failures

## ?? Troubleshooting

### Database Connection Errors

```bash
# Test connection
psql -h localhost -U postgres -d vaani_db -c "SELECT 1;"

# Check if tables exist
psql -d vaani_db -c "\dt"
```

### JWT Authentication Errors

- Verify `Jwt:Secret` is at least 32 characters
- Check token expiration time
- Ensure clock synchronization between client and server

### Encryption Errors

- Verify `Encryption:AesKey` is exactly 32 characters
- Check that encrypted data starts with `VAANI_ENC_v1_`

## ?? Sample Data

The database includes 4 test meetings:

| Meeting ID | Name | Valid Until |
|-----------|------|-------------|
| VAANI-TEST-001 | Test Meeting - English to Hindi | +10 hours |
| VAANI-TEST-002 | Test Meeting - Sales Call | +10 hours |
| VAANI-DEMO-123 | Demo Meeting - Product Presentation | +10 hours |
| VM-2025-1220-A7B3 | Vendor Discussion Meeting | +10 hours |

**Important:** Update Azure subscription keys in the database:

```sql
UPDATE meetings 
SET azure_subscription_key = 'your_actual_azure_key'
WHERE is_active = TRUE;
```

## ?? Integration with Desktop App

Update the desktop app's `appsettings.json`:

```json
{
  "Authentication": {
    "ApiBaseUrl": "https://your-api-url.com",
    "ApiTimeout": 30
  }
}
```

Set `USE_MOCK_MODE = false` in `MeetingAuthenticationService.cs`.

## ?? License

MIT License

## ?? Support

For issues and questions:
- GitHub Issues: https://github.com/sanjaykrpandit/vaani/issues
- Email: support@vaani.com
