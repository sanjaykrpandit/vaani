# ?? Vaani API - Quick Reference Guide

## ?? Project Information
- **Framework:** ASP.NET Core 8
- **Database:** PostgreSQL 12+
- **Authentication:** JWT Bearer
- **Architecture:** Clean 3-Tier Architecture

---

## ? Quick Commands

### Development
```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run (Development)
dotnet run

# Run (Production mode)
dotnet run --environment Production

# Watch mode (auto-reload)
dotnet watch run
```

### Database
```bash
# Create database
psql -U postgres -c "CREATE DATABASE vaani_db;"

# Run schema
psql -U postgres -d vaani_db -f Database/schema.sql

# Connect to database
psql -U postgres -d vaani_db

# Update Azure keys
psql -d vaani_db -c "UPDATE meetings SET azure_subscription_key = 'your_key';"
```

### Docker
```bash
# Build image
docker build -t vaani-api .

# Run with docker-compose
docker-compose up -d

# Stop containers
docker-compose down

# View logs
docker-compose logs -f vaani-api
```

---

## ?? Configuration

### Required Settings (appsettings.json)

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

**?? Important:**
- JWT Secret: Minimum 32 characters
- AES Key: Exactly 32 characters
- Use different secrets for Production

---

## ?? API Endpoints

### Base URL
- Development: `https://localhost:7001`
- Production: `https://your-domain.com`

### Endpoints

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/health` | GET | ? | Health check |
| `/api/meetings/validate` | POST | ? | Validate meeting & create session |
| `/api/meetings/{id}/valid` | GET | ? | Check if meeting is valid |
| `/api/sessions/heartbeat` | POST | ? | Send heartbeat |
| `/api/sessions/end` | POST | ? | End session |
| `/api/sessions/remaining` | GET | ? | Get remaining time |

---

## ?? Testing

### Swagger UI
```
https://localhost:7001
```

### Sample Request (Validate Meeting)
```bash
curl -X POST https://localhost:7001/api/meetings/validate \
  -H "Content-Type: application/json" \
  -d '{
    "meetingId": "VAANI-TEST-001",
    "deviceId": "test-123",
    "deviceName": "Test Device",
    "appVersion": "1.0.0"
  }'
```

### Sample Request (Heartbeat)
```bash
curl -X POST https://localhost:7001/api/sessions/heartbeat \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"timestamp": "2025-12-31T10:00:00Z"}'
```

### Postman Collection
Import `Vaani-API.postman_collection.json` into Postman

---

## ??? Database Quick Reference

### Sample Meetings
```sql
-- View all meetings
SELECT meeting_id, meeting_name, valid_until, is_active 
FROM meetings;

-- Get active sessions
SELECT * FROM sessions WHERE status = 'Active';

-- View session logs
SELECT * FROM session_logs ORDER BY timestamp DESC LIMIT 10;
```

### Common Updates
```sql
-- Update Azure key
UPDATE meetings 
SET azure_subscription_key = 'your_azure_key' 
WHERE meeting_id = 'VAANI-TEST-001';

-- Extend meeting validity
UPDATE meetings 
SET valid_until = CURRENT_TIMESTAMP + INTERVAL '10 hours' 
WHERE meeting_id = 'VAANI-TEST-001';

-- Deactivate meeting
UPDATE meetings 
SET is_active = FALSE 
WHERE meeting_id = 'OLD-MEETING-ID';
```

---

## ?? Troubleshooting

### Database Connection Error
```bash
# Check PostgreSQL status
sudo systemctl status postgresql

# Test connection
psql -U postgres -d vaani_db -c "SELECT 1;"
```

### JWT Authentication Error
- Verify JWT Secret is 32+ characters
- Check Authorization header format: `Bearer <token>`
- Verify token hasn't expired

### Build Errors
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

---

## ?? Deployment Checklist

### Pre-Deployment
- [ ] Update `appsettings.Production.json`
- [ ] Configure AWS RDS PostgreSQL
- [ ] Run database schema
- [ ] Update Azure subscription keys
- [ ] Set up AWS Secrets Manager

### Deployment
- [ ] Choose deployment method (EB/ECS/EC2)
- [ ] Deploy application
- [ ] Configure SSL certificate
- [ ] Set up load balancer
- [ ] Configure CloudWatch logging

### Post-Deployment
- [ ] Test health endpoint
- [ ] Test meeting validation
- [ ] Test JWT authentication
- [ ] Configure monitoring
- [ ] Update desktop app API URL

---

## ?? Project Structure Quick View

```
Controllers/        ? API endpoints
Services/          ? Business logic
Interfaces/        ? Contracts
Data/             ? Database access
Models/
  ??Entities/     ? Database tables
  ??DTOs/         ? API models
Database/         ? SQL scripts
```

---

## ?? Security Checklist

- [ ] Use HTTPS in production
- [ ] Store secrets in AWS Secrets Manager
- [ ] Enable CORS properly
- [ ] Use strong JWT secret (32+ chars)
- [ ] Rotate encryption keys periodically
- [ ] Enable CloudWatch logging
- [ ] Set up WAF (Web Application Firewall)
- [ ] Configure security groups properly

---

## ?? Monitoring

### Health Check
```bash
curl https://your-api.com/health
```

### CloudWatch Logs
```bash
# View logs
aws logs tail /aws/elasticbeanstalk/vaani-api/app --follow
```

### Database Monitoring
```sql
-- Active sessions count
SELECT COUNT(*) FROM sessions WHERE status = 'Active';

-- Recent session logs
SELECT event_type, COUNT(*) 
FROM session_logs 
WHERE timestamp > CURRENT_TIMESTAMP - INTERVAL '1 hour'
GROUP BY event_type;
```

---

## ?? Useful Links

- **Swagger UI:** https://localhost:7001
- **Health Check:** https://localhost:7001/health
- **GitHub Repo:** https://github.com/sanjaykrpandit/vaani
- **PostgreSQL Docs:** https://www.postgresql.org/docs/
- **EF Core Docs:** https://learn.microsoft.com/en-us/ef/core/
- **JWT Docs:** https://jwt.io/

---

## ?? Support

- **GitHub Issues:** https://github.com/sanjaykrpandit/vaani/issues
- **Documentation:** See README.md, DEPLOYMENT.md
- **Database Guide:** Database/README.md

---

## ?? Key Files Reference

| File | Purpose |
|------|---------|
| `Program.cs` | App startup, DI, middleware |
| `appsettings.json` | Configuration |
| `VaaniDbContext.cs` | EF Core context |
| `MeetingsController.cs` | Meeting endpoints |
| `SessionsController.cs` | Session endpoints |
| `schema.sql` | Database schema |

---

## ?? Tips

1. **Use Swagger** for initial testing
2. **Import Postman collection** for automated testing
3. **Check logs** in `bin/Debug/net8.0/` for errors
4. **Use Docker** for consistent local environment
5. **Test locally** before AWS deployment

---

## ?? Environment Variables

For production, use environment variables:

```bash
export ConnectionStrings__PostgreSQL="Host=...;Database=vaani_db;..."
export Jwt__Secret="production_secret_key"
export Encryption__AesKey="production_encryption_key"
```

Or use AWS Secrets Manager (recommended)

---

## ?? Quick Notes

- Default port: 7001 (HTTPS), 5001 (HTTP)
- Swagger available at root URL in development
- JWT tokens expire after 4 hours
- Sample meetings valid for 10 hours
- All timestamps in UTC

---

**Version:** 1.0  
**Last Updated:** December 2025  
**Status:** ? Production Ready
