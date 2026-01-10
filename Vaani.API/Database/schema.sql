-- =============================================
-- Vaani API Database Schema for PostgreSQL
-- Version: 1.0
-- =============================================

-- Drop existing tables if they exist (for clean reinstall)
DROP TABLE IF EXISTS session_logs CASCADE;
DROP TABLE IF EXISTS sessions CASCADE;
DROP TABLE IF EXISTS meetings CASCADE;

-- =============================================
-- Table: meetings
-- Description: Stores meeting configurations
-- =============================================
CREATE TABLE meetings (
    id SERIAL PRIMARY KEY,
    meeting_id VARCHAR(50) UNIQUE NOT NULL,
    meeting_name VARCHAR(255) NOT NULL,
    azure_subscription_key VARCHAR(255) NOT NULL,
    azure_region VARCHAR(50) NOT NULL,
    vendor_language_code VARCHAR(10) NOT NULL,
    vendor_voice VARCHAR(50) NOT NULL,
    organizer_language_code VARCHAR(10) NOT NULL,
    organizer_voice VARCHAR(50) NOT NULL,
    valid_from TIMESTAMP NOT NULL,
    valid_until TIMESTAMP NOT NULL,
    allow_reconnect BOOLEAN DEFAULT TRUE,
    heartbeat_interval_seconds INTEGER DEFAULT 60,
    enable_local_cache BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP,
    is_active BOOLEAN DEFAULT TRUE
);

-- Index for faster lookups
CREATE INDEX idx_meetings_meeting_id ON meetings(meeting_id);
CREATE INDEX idx_meetings_valid_until ON meetings(valid_until);
CREATE INDEX idx_meetings_is_active ON meetings(is_active);

-- =============================================
-- Table: sessions
-- Description: Stores active and historical sessions
-- =============================================
CREATE TABLE sessions (
    id SERIAL PRIMARY KEY,
    session_token VARCHAR(500) UNIQUE NOT NULL,
    meeting_id VARCHAR(50) NOT NULL,
    device_id VARCHAR(50) NOT NULL,
    device_name VARCHAR(255) NOT NULL,
    app_version VARCHAR(20),
    started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_heartbeat TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    ended_at TIMESTAMP,
    status VARCHAR(20) DEFAULT 'Active',
    duration_minutes INTEGER DEFAULT 0,
    FOREIGN KEY (meeting_id) REFERENCES meetings(meeting_id) ON DELETE CASCADE
);

-- Indexes for faster lookups
CREATE INDEX idx_sessions_session_token ON sessions(session_token);
CREATE INDEX idx_sessions_meeting_id ON sessions(meeting_id);
CREATE INDEX idx_sessions_status ON sessions(status);
CREATE INDEX idx_sessions_started_at ON sessions(started_at);

-- =============================================
-- Table: session_logs
-- Description: Stores session event logs
-- =============================================
CREATE TABLE session_logs (
    id SERIAL PRIMARY KEY,
    session_id INTEGER NOT NULL,
    event_type VARCHAR(50) NOT NULL,
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    details TEXT,
    FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);

-- Index for faster lookups
CREATE INDEX idx_session_logs_session_id ON session_logs(session_id);
CREATE INDEX idx_session_logs_event_type ON session_logs(event_type);
CREATE INDEX idx_session_logs_timestamp ON session_logs(timestamp);

-- =============================================
-- Insert Sample Data (for testing)
-- =============================================

-- Sample Meeting 1: English to Hindi
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
    allow_reconnect,
    heartbeat_interval_seconds,
    enable_local_cache,
    is_active
) VALUES (
    'VAANI-TEST-001',
    'Test Meeting - English to Hindi',
    'your_azure_subscription_key_here',
    'eastus2',
    'hi-IN',
    'hi-IN-MadhurNeural',
    'en-US',
    'en-US-GuyNeural',
    CURRENT_TIMESTAMP - INTERVAL '1 hour',
    CURRENT_TIMESTAMP + INTERVAL '10 hours',
    TRUE,
    60,
    FALSE,
    TRUE
);

-- Sample Meeting 2: English to Spanish
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
    allow_reconnect,
    heartbeat_interval_seconds,
    enable_local_cache,
    is_active
) VALUES (
    'VAANI-TEST-002',
    'Test Meeting - Sales Call',
    'your_azure_subscription_key_here',
    'eastus2',
    'es-ES',
    'es-ES-AlvaroNeural',
    'en-US',
    'en-US-JennyNeural',
    CURRENT_TIMESTAMP - INTERVAL '1 hour',
    CURRENT_TIMESTAMP + INTERVAL '10 hours',
    TRUE,
    60,
    FALSE,
    TRUE
);

-- Sample Meeting 3: Demo Meeting
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
    allow_reconnect,
    heartbeat_interval_seconds,
    enable_local_cache,
    is_active
) VALUES (
    'VAANI-DEMO-123',
    'Demo Meeting - Product Presentation',
    'your_azure_subscription_key_here',
    'eastus2',
    'hi-IN',
    'hi-IN-SwaraNeural',
    'en-US',
    'en-US-AriaNeural',
    CURRENT_TIMESTAMP - INTERVAL '1 hour',
    CURRENT_TIMESTAMP + INTERVAL '10 hours',
    TRUE,
    60,
    FALSE,
    TRUE
);

-- Sample Meeting 4: Vendor Meeting
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
    allow_reconnect,
    heartbeat_interval_seconds,
    enable_local_cache,
    is_active
) VALUES (
    'VM-2025-1220-A7B3',
    'Vendor Discussion Meeting',
    'your_azure_subscription_key_here',
    'eastus2',
    'hi-IN',
    'hi-IN-MadhurNeural',
    'en-US',
    'en-US-GuyNeural',
    CURRENT_TIMESTAMP - INTERVAL '1 hour',
    CURRENT_TIMESTAMP + INTERVAL '10 hours',
    TRUE,
    60,
    FALSE,
    TRUE
);

-- =============================================
-- Useful Queries for Administration
-- =============================================

-- View all active meetings
-- SELECT * FROM meetings WHERE is_active = TRUE AND valid_until > CURRENT_TIMESTAMP;

-- View active sessions
-- SELECT s.*, m.meeting_name 
-- FROM sessions s 
-- JOIN meetings m ON s.meeting_id = m.meeting_id 
-- WHERE s.status = 'Active';

-- View session logs for a specific session
-- SELECT * FROM session_logs WHERE session_id = ? ORDER BY timestamp DESC;

-- Clean up expired sessions (run periodically)
-- UPDATE sessions 
-- SET status = 'Expired', ended_at = CURRENT_TIMESTAMP 
-- WHERE status = 'Active' 
-- AND started_at < CURRENT_TIMESTAMP - INTERVAL '24 hours';

-- Get session statistics
-- SELECT 
--     m.meeting_name,
--     COUNT(s.id) as total_sessions,
--     AVG(s.duration_minutes) as avg_duration_minutes,
--     MAX(s.duration_minutes) as max_duration_minutes
-- FROM meetings m
-- LEFT JOIN sessions s ON m.meeting_id = s.meeting_id
-- GROUP BY m.meeting_id, m.meeting_name;

-- =============================================
-- Database Setup Complete
-- =============================================

-- Verify installation
SELECT 'Database schema created successfully!' as status;
SELECT 'Sample meetings inserted: ' || COUNT(*) as sample_data FROM meetings;
