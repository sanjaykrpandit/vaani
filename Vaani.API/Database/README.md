# Vaani API - Database Setup Guide

## Prerequisites
- PostgreSQL 12+ installed
- Database admin credentials
- pgAdmin (optional, for GUI management)

## Setup Instructions

### 1. Create Database

Connect to PostgreSQL and create the database:

```sql
CREATE DATABASE vaani_db;
```

Or using psql command line:
```bash
psql -U postgres
CREATE DATABASE vaani_db;
\q
```

### 2. Run Schema Script

Execute the schema.sql file to create tables and sample data:

```bash
psql -U postgres -d vaani_db -f Database/schema.sql
```

Or using pgAdmin:
1. Connect to your PostgreSQL server
2. Right-click on `vaani_db` database
3. Select "Query Tool"
4. Open `schema.sql` file
5. Click "Execute" (F5)

### 3. Verify Installation

```sql
-- Check if tables are created
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'public';

-- Check sample data
SELECT meeting_id, meeting_name, valid_until 
FROM meetings 
WHERE is_active = TRUE;
```

## Configuration

### Update Connection String

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=vaani_db;Username=postgres;Password=your_password"
  }
}
```

### Update Azure Subscription Keys

After creating the database, update the Azure subscription keys in the meetings table:

```sql
UPDATE meetings 
SET azure_subscription_key = 'your_actual_azure_key_here'
WHERE is_active = TRUE;
```

## AWS RDS PostgreSQL Setup

### 1. Create RDS Instance

1. Go to AWS RDS Console
2. Click "Create database"
3. Choose PostgreSQL
4. Select version 14+
5. Template: Free tier (for testing) or Production
6. Settings:
   - DB instance identifier: `vaani-db`
   - Master username: `admin`
   - Master password: `<strong_password>`
7. Instance configuration: `db.t3.micro` (free tier eligible)
8. Storage: 20 GB (minimum)
9. Connectivity:
   - VPC: Default
   - Public access: Yes (for development)
   - Security group: Create new or select existing
10. Additional configuration:
    - Initial database name: `vaani_db`
11. Click "Create database"

### 2. Configure Security Group

1. Go to EC2 Console > Security Groups
2. Find the security group attached to your RDS instance
3. Add inbound rule:
   - Type: PostgreSQL
   - Protocol: TCP
   - Port: 5432
   - Source: Your IP (for development) or 0.0.0.0/0 (for production with caution)

### 3. Update Connection String for AWS

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=your-rds-endpoint.us-east-1.rds.amazonaws.com;Port=5432;Database=vaani_db;Username=admin;Password=your_password;SSL Mode=Require"
  }
}
```

Find your RDS endpoint in the AWS Console under "Connectivity & security".

### 4. Connect and Run Schema

```bash
psql -h your-rds-endpoint.us-east-1.rds.amazonaws.com -U admin -d vaani_db -f Database/schema.sql
```

## Sample Data

The schema includes 4 sample meetings:

| Meeting ID | Name | Languages |
|-----------|------|-----------|
| VAANI-TEST-001 | Test Meeting - English to Hindi | en-US ? hi-IN |
| VAANI-TEST-002 | Test Meeting - Sales Call | en-US ? es-ES |
| VAANI-DEMO-123 | Demo Meeting - Product Presentation | en-US ? hi-IN |
| VM-2025-1220-A7B3 | Vendor Discussion Meeting | en-US ? hi-IN |

All meetings are valid for 10 hours from creation.

## Maintenance Queries

### Add New Meeting

```sql
INSERT INTO meetings (
    meeting_id, 
    meeting_name, 
    azure_subscription_key, 
    azure_region,
    vendor_language_code, 
    vendor_voice,
    organizer_language_code, 
    organizer_voice,
    valid_from, 
    valid_until,
    is_active
) VALUES (
    'YOUR-MEETING-ID',
    'Your Meeting Name',
    'your_azure_key',
    'eastus2',
    'hi-IN',
    'hi-IN-MadhurNeural',
    'en-US',
    'en-US-GuyNeural',
    CURRENT_TIMESTAMP,
    CURRENT_TIMESTAMP + INTERVAL '4 hours',
    TRUE
);
```

### Deactivate Meeting

```sql
UPDATE meetings 
SET is_active = FALSE 
WHERE meeting_id = 'MEETING-ID';
```

### View Active Sessions

```sql
SELECT 
    s.session_token,
    s.meeting_id,
    m.meeting_name,
    s.device_name,
    s.started_at,
    s.last_heartbeat,
    s.status
FROM sessions s
JOIN meetings m ON s.meeting_id = m.meeting_id
WHERE s.status = 'Active'
ORDER BY s.started_at DESC;
```

### Clean Up Old Sessions

```sql
-- Mark sessions as expired if no heartbeat for 2 hours
UPDATE sessions 
SET status = 'Expired', 
    ended_at = CURRENT_TIMESTAMP 
WHERE status = 'Active' 
AND last_heartbeat < CURRENT_TIMESTAMP - INTERVAL '2 hours';
```

### View Session Statistics

```sql
SELECT 
    m.meeting_id,
    m.meeting_name,
    COUNT(s.id) as total_sessions,
    SUM(CASE WHEN s.status = 'Active' THEN 1 ELSE 0 END) as active_sessions,
    AVG(s.duration_minutes) as avg_duration,
    MAX(s.started_at) as last_session
FROM meetings m
LEFT JOIN sessions s ON m.meeting_id = s.meeting_id
GROUP BY m.meeting_id, m.meeting_name
ORDER BY last_session DESC;
```

## Troubleshooting

### Connection Issues

1. Check if PostgreSQL is running:
```bash
sudo systemctl status postgresql
```

2. Check PostgreSQL logs:
```bash
sudo tail -f /var/log/postgresql/postgresql-*.log
```

3. Test connection:
```bash
psql -U postgres -d vaani_db -c "SELECT version();"
```

### Permission Issues

Grant permissions if needed:
```sql
GRANT ALL PRIVILEGES ON DATABASE vaani_db TO your_user;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO your_user;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO your_user;
```

### Reset Database

To completely reset:
```sql
DROP DATABASE IF EXISTS vaani_db;
CREATE DATABASE vaani_db;
-- Then run schema.sql again
```

## Backup and Restore

### Backup

```bash
pg_dump -U postgres -d vaani_db -F c -f vaani_backup.dump
```

### Restore

```bash
pg_restore -U postgres -d vaani_db -c vaani_backup.dump
```

## Next Steps

1. Update Azure subscription keys in the meetings table
2. Update connection string in `appsettings.json`
3. Test the API endpoints using Swagger
4. Create additional meetings as needed
5. Set up automated backups (AWS RDS has automatic backups)
