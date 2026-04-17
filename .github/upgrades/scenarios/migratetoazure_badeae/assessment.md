# Azure Translation Service Backend Migration Assessment

## Executive Summary

This assessment evaluates the migration of Azure Cognitive Services Translation functionality from the Vaani desktop client to the Vaani.API backend server. Currently, the desktop application directly calls Azure APIs for speech recognition, translation, and text-to-speech synthesis. The goal is to centralize these operations in the backend to improve security, scalability, and maintainability.

### Current Architecture

**Desktop Client (Vaani.csproj)**
- Direct Azure SDK integration (`Microsoft.CognitiveServices.Speech` v1.47.0)
- Azure subscription keys and region stored in meeting configuration
- Real-time bidirectional translation (English ↔ Hindi)
- Dual pipeline architecture (Outgoing + Incoming flows)
- Direct audio capture/playback using NAudio
- Session management with encrypted configurations

**Backend API (Vaani.API.csproj)**
- .NET 8 Web API with PostgreSQL database
- JWT-based authentication
- Meeting and session management
- Azure subscription management (keys, regions stored in DB)
- Language and voice configuration endpoints

### Migration Scope

**In Scope:**
1. Move Azure Cognitive Services API calls from desktop client to backend
2. Implement real-time translation WebSocket/SignalR endpoints
3. Secure Azure credentials on backend (remove from client)
4. Create backend translation service layer
5. Implement audio streaming between client and server
6. Maintain existing session/meeting management
7. Update client to communicate with backend translation endpoints

**Out of Scope:**
- Audio device management (remains client-side)
- VB-CABLE configuration (client-side responsibility)
- Desktop UI changes beyond API integration
- Database schema changes (current schema sufficient)

---

## Project Structure Analysis

### Solution Overview

```
Vaani.slnx
├── Vaani (Desktop Client)
│   ├── Services/
│   │   ├── TranslationService.cs (MAIN MIGRATION TARGET - 1500+ lines)
│   │   ├── ITranslationService.cs
│   │   ├── BypassTranslationService.cs
│   │   ├── ClientService.cs
│   │   └── DeviceService.cs
│   ├── Authentication/
│   │   ├── Services/SessionManager.cs
│   │   └── Models/MeetingConfiguration.cs
│   └── Models/
│       └── TranslationSettings.cs
│
├── Vaani.API (Backend Server)
│   ├── Controllers/
│   │   ├── MeetingsController.cs
│   │   ├── SessionsController.cs
│   │   └── LanguageController.cs
│   ├── Services/
│   │   ├── MeetingService.cs
│   │   ├── SessionService.cs
│   │   └── AzureSubscriptionService.cs
│   └── Models/
│       ├── Entities/
│       │   ├── Meeting.cs
│       │   ├── Session.cs
│       │   └── AzureSubscription.cs
│       └── DTOs/
│
└── Vaani.Admin (Admin Portal - React)
    └── [Not impacted by this migration]
```

---

## Detailed Analysis

### 1. Current Translation Service Implementation

**File:** `Vaani\Services\TranslationService.cs` (1500+ lines)

#### Key Functionality

**Azure SDK Direct Usage:**
```csharp
// Line 600: Direct Azure configuration
var config = SpeechTranslationConfig.FromEndpoint(
    new Uri($"wss://{settings.AzureRegion}.stt.speech.microsoft.com/speech/universal/v2"),
    settings.AzureSubscriptionKey
);

// Line 635: Speech synthesizer creation
var speechConfig = SpeechConfig.FromSubscription(
    settings.AzureSubscriptionKey, 
    settings.AzureRegion
);
```

**Dual Pipeline Architecture:**
1. **Outgoing Flow** (OutgoingFlow method, ~400 lines)
   - Captures user's microphone input
   - Recognizes speech in source language
   - Translates to target language
   - Synthesizes translated audio
   - Outputs to CABLE device

2. **Incoming Flow** (IncomingFlow method, ~400 lines)
   - Captures meeting audio via loopback
   - Recognizes speech in target language
   - Translates to source language
   - Synthesizes translated audio
   - Outputs to user's speaker

**Key Components:**
- `TranslationRecognizer` for speech-to-text + translation
- `SpeechSynthesizer` for text-to-speech
- Channel-based async audio streaming
- Event-driven architecture (Recognizing, Recognized, Synthesizing events)
- Microphone smart pause/resume for echo prevention
- Duplicate transcript filtering
- Comprehensive logging system

**Dependencies:**
- `Microsoft.CognitiveServices.Speech` (Azure SDK)
- `NAudio.Wave` and `NAudio.CoreAudioApi` (audio capture)
- `Avalonia.Threading` (UI thread marshaling)

### 2. Current Authentication & Configuration Flow

**File:** `Vaani\Authentication\Services\SessionManager.cs`

```csharp
// Current flow:
1. User enters meeting ID + password/token
2. Desktop calls: POST /api/meetings/validate
3. Backend returns encrypted MeetingConfiguration containing:
   - Azure subscription key (SECURITY ISSUE)
   - Azure region
   - Language codes
   - Voice names
   - Session token
4. Desktop decrypts and uses Azure credentials directly
```

**Security Concern:** Azure credentials are sent to client and stored in local secure storage. This violates security best practices and prevents centralized usage monitoring.

### 3. Backend Current Capabilities

**File:** `Vaani.API\Controllers\MeetingsController.cs`

**Existing Endpoints:**
- `POST /api/meetings/validate` - Validates meeting and returns encrypted config
- `GET /api/meetings/{meetingId}/valid` - Health check
- `POST /api/meetings/validate-token` - Token validation

**File:** `Vaani.API\Controllers\SessionsController.cs`

- `POST /api/sessions/start` - Creates session
- `POST /api/sessions/heartbeat` - Keeps session alive
- `POST /api/sessions/end` - Ends session

**File:** `Vaani.API\Services\AzureSubscriptionService.cs`

- Manages Azure subscription keys in database
- CRUD operations for subscriptions
- Already has `AzureSubscription` entity with `SubscriptionKey` and `Region`

**Database Schema (Existing):**
```sql
-- AzureSubscriptions table
- Id (PK)
- SubscriptionKey (encrypted)
- Region
- Description
- IsActive
- CreatedAt
- UpdatedAt

-- Meetings table references AzureSubscriptionId (FK)
```

### 4. Required Backend Changes

#### 4.1 New Backend Service: `TranslationService`

**Purpose:** Handle Azure Cognitive Services communication on server-side

**Responsibilities:**
1. Initialize Azure SDK with credentials from database
2. Create translation recognizers for each session
3. Process audio streams from client
4. Stream translated audio back to client
5. Manage translation lifecycle (start/stop/pause)
6. Log translation metrics to database

**Location:** `Vaani.API\Services\TranslationService.cs` (NEW)

**Interface:** `Vaani.API\Interfaces\ITranslationService.cs` (NEW)

#### 4.2 New Backend Endpoints

**Controller:** `Vaani.API\Controllers\TranslationController.cs` (NEW)

**Endpoints:**

| Endpoint | Method | Description | Request/Response |
|----------|--------|-------------|------------------|
| `/api/translation/start` | POST | Start translation session | Request: SessionId, Direction, AudioFormat<br>Response: WebSocket URL |
| `/api/translation/stop` | POST | Stop translation session | Request: SessionId |
| `/api/translation/status` | GET | Get translation status | Response: Status, Metrics |
| `/api/translation/ws` | WebSocket | Bidirectional audio stream | Audio chunks ↔ Translated audio |

#### 4.3 Real-Time Communication Layer

**Technology Options:**

**Option A: SignalR** (RECOMMENDED)
- Built into ASP.NET Core
- Automatic transport selection (WebSocket, Server-Sent Events, Long Polling)
- Strongly typed hubs
- Connection management
- Reconnection logic

**Option B: Raw WebSockets**
- Lower overhead
- More control
- Requires manual connection management
- Better for pure binary streaming

**Recommendation:** Use **SignalR** for control messages and status updates, combined with **dedicated WebSocket** for audio streaming.

#### 4.4 Audio Streaming Architecture

**Flow:**

```
Desktop Client                          Backend API
─────────────                           ───────────
1. Capture audio from microphone
2. Encode to 16kHz PCM mono
3. Send audio chunks →                  4. Receive audio stream
   (WebSocket/SignalR)                  5. Feed to Azure SDK
                                        6. Azure recognizes speech
                                        7. Azure translates text
                                        8. Azure synthesizes speech
9. Receive translated audio ←           10. Stream audio back
10. Decode audio
11. Play to CABLE device
```

**Key Considerations:**
- Audio latency: Target <500ms end-to-end
- Buffer size: 100ms chunks (1600 bytes @ 16kHz)
- Network resilience: Handle dropped connections
- Synchronization: Maintain audio stream continuity

### 5. Desktop Client Changes Required

#### 5.1 Remove Direct Azure SDK Dependencies

**File:** `Vaani\Vaani.csproj`

**Remove:**
```xml
<PackageReference Include="Microsoft.CognitiveServices.Speech" Version="1.47.0" />
```

**Impact:** Significant refactoring of `TranslationService.cs`

#### 5.2 Create Backend Translation Client

**New File:** `Vaani\Services\BackendTranslationService.cs`

**Responsibilities:**
1. Implement `ITranslationService` interface
2. Establish WebSocket/SignalR connection to backend
3. Stream audio to backend
4. Receive translated audio from backend
5. Maintain same event interface (Recognizing, Recognized, etc.)
6. Handle reconnection logic

**Key APIs:**
- `SignalR.Client` NuGet package
- `System.Net.WebSockets` (WebSocket client)

#### 5.3 Update Configuration Model

**File:** `Vaani\Authentication\Models\MeetingConfiguration.cs`

**Changes:**
```csharp
// REMOVE (security risk):
public class AzureConfiguration
{
    public string SubscriptionKey { get; set; } // DELETE THIS
    public string Region { get; set; }           // DELETE THIS
}

// REPLACE WITH:
public class AzureConfiguration
{
    public string TranslationServiceUrl { get; set; } // Backend URL
    // Credentials stay on server
}
```

#### 5.4 Update Session Manager

**File:** `Vaani\Authentication\Services\SessionManager.cs`

**Changes:**
- Remove secure storage of Azure credentials
- Store only backend service URL and session token
- Update `StartSession` to not expose Azure keys

### 6. Security Improvements

#### 6.1 Current Security Issues

| Issue | Severity | Impact |
|-------|----------|--------|
| Azure keys sent to client | HIGH | Credential exposure, unauthorized usage |
| Keys stored in local storage | HIGH | Potential key extraction by malicious actors |
| No centralized usage tracking | MEDIUM | Can't monitor/limit Azure API usage |
| No key rotation without client update | MEDIUM | Compromised keys hard to revoke |

#### 6.2 Post-Migration Security Benefits

| Improvement | Benefit |
|-------------|---------|
| Keys stay on server | No credential exposure to client |
| Encrypted at rest in DB | AES-256 encryption via `EncryptionService` |
| Centralized access control | Backend validates session before each translation request |
| Usage monitoring | Track Azure API calls per meeting/session |
| Easy key rotation | Update DB, no client changes needed |
| Rate limiting | Implement per-session rate limits |

### 7. Performance Considerations

#### 7.1 Latency Analysis

**Current (Direct Azure):**
```
Microphone → Azure SDK → Azure API → Azure SDK → Speaker
Total: ~200-300ms (direct network path)
```

**Proposed (Via Backend):**
```
Microphone → WebSocket → Backend → Azure API → Backend → WebSocket → Speaker
Total: ~350-500ms (additional hop)
```

**Additional Latency:**
- WebSocket overhead: ~20-50ms each direction
- Backend processing: ~10-20ms
- Total added: ~100-200ms

**Mitigation Strategies:**
1. Use WebSocket binary frames (not text)
2. Optimize audio buffer sizes (100ms chunks)
3. Deploy backend in same Azure region as Cognitive Services
4. Use HTTP/2 for SignalR connections
5. Implement client-side audio buffering

#### 7.2 Scalability

**Current Issues:**
- Each desktop client creates separate Azure SDK connections
- No connection pooling
- No load balancing

**Post-Migration Benefits:**
- Backend can pool Azure SDK connections
- Horizontal scaling with load balancer
- Centralized connection management
- Better resource utilization

#### 7.3 Network Requirements

**Bandwidth:**
- Audio upload: ~256 kbps (16kHz × 16-bit mono)
- Audio download: ~256 kbps
- **Total per session:** ~512 kbps (~64 KB/s)

**Connection Stability:**
- WebSocket reconnection logic required
- Audio buffer to smooth network jitter
- Graceful degradation on poor connections

### 8. Backward Compatibility

#### 8.1 Phased Migration Strategy

**Phase 1:** Backend infrastructure
- Create backend translation service
- Implement WebSocket endpoints
- Test with sample audio

**Phase 2:** Feature flag
- Add `UseBackendTranslation` feature flag
- Both modes coexist
- Gradual rollout

**Phase 3:** Full migration
- Remove Azure SDK from client
- Backend-only mode
- Deprecate old API

#### 8.2 Feature Parity

**Must Maintain:**
- [x] Bidirectional translation (Outgoing + Incoming)
- [x] Real-time speech recognition events (Recognizing, Recognized)
- [x] Synthesis status events (Synthesizing, SynthesizingStatusChanged)
- [x] Microphone mute/unmute
- [x] Speaker mute/unmute
- [x] Duplicate transcript filtering
- [x] Audio device selection (client-side)
- [x] Session expiry handling

### 9. Testing Strategy

#### 9.1 Unit Tests

**Backend:**
- `TranslationService` Azure SDK mocking
- WebSocket message handling
- Audio streaming logic
- Error scenarios (network loss, Azure API errors)

**Desktop Client:**
- `BackendTranslationService` WebSocket mocking
- Event propagation
- Reconnection logic

#### 9.2 Integration Tests

- End-to-end audio streaming
- Latency measurements
- Concurrent sessions
- Load testing (multiple simultaneous translations)

#### 9.3 Manual Testing

- Real meeting scenarios (Teams, Zoom)
- Poor network conditions
- Mid-session reconnection
- Audio quality validation

### 10. Dependencies Analysis

#### 10.1 Backend New Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.CognitiveServices.Speech` | 1.47.0+ | Azure SDK |
| `Microsoft.AspNetCore.SignalR` | (Built-in .NET 8) | Real-time communication |

**Installation:**
```bash
cd Vaani.API
dotnet add package Microsoft.CognitiveServices.Speech
```

#### 10.2 Desktop New Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.AspNetCore.SignalR.Client` | 8.0.0+ | SignalR client |

**Remove:**
- `Microsoft.CognitiveServices.Speech` (1.47.0)

**Keep:**
- `NAudio` (2.2.1) - Still needed for audio capture/playback
- `Avalonia.*` - UI framework

#### 10.3 Dependency Tree Impact

**Before:**
```
Vaani (Desktop)
└── Microsoft.CognitiveServices.Speech
    ├── System.Memory
    └── System.Net.Http
```

**After:**
```
Vaani (Desktop)
└── Microsoft.AspNetCore.SignalR.Client
    ├── System.Net.WebSockets.Client
    └── System.Text.Json

Vaani.API (Backend)
└── Microsoft.CognitiveServices.Speech
    ├── System.Memory
    └── System.Net.Http
```

---

## Risk Assessment

### High Priority Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Increased latency affects user experience | HIGH | HIGH | Deploy in same region, optimize buffers, extensive testing |
| WebSocket connection stability issues | MEDIUM | HIGH | Implement robust reconnection, fallback mechanisms |
| Azure SDK thread safety on server | MEDIUM | HIGH | Use proper connection pooling, review Azure SDK docs |
| Breaking existing desktop clients | LOW | HIGH | Feature flag, phased rollout, backward compatibility mode |

### Medium Priority Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Backend becomes single point of failure | MEDIUM | MEDIUM | Load balancing, health checks, monitoring |
| Increased infrastructure costs | HIGH | MEDIUM | Monitor usage, optimize connections, consider Azure pricing |
| Audio quality degradation | MEDIUM | MEDIUM | Use lossless compression, validate formats |

### Low Priority Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| SignalR compatibility issues | LOW | LOW | Use stable .NET 8 APIs |
| Database performance with session logs | MEDIUM | LOW | Index optimization, log rotation |

---

## Effort Estimation

### Development Tasks

| Task | Effort (Days) | Priority |
|------|---------------|----------|
| **Backend Development** | | |
| Create TranslationService backend | 5 | HIGH |
| Implement WebSocket/SignalR endpoints | 3 | HIGH |
| Audio streaming infrastructure | 4 | HIGH |
| Update meeting validation (remove Azure keys) | 2 | HIGH |
| Error handling & logging | 2 | MEDIUM |
| **Desktop Client Changes** | | |
| Create BackendTranslationService | 5 | HIGH |
| WebSocket client implementation | 3 | HIGH |
| Update configuration models | 1 | HIGH |
| Remove Azure SDK dependencies | 2 | MEDIUM |
| Reconnection logic | 2 | MEDIUM |
| **Testing & QA** | | |
| Unit tests (backend + client) | 4 | MEDIUM |
| Integration tests | 3 | MEDIUM |
| Manual testing & validation | 5 | HIGH |
| Performance testing & tuning | 3 | MEDIUM |
| **Documentation** | | |
| API documentation | 1 | LOW |
| Architecture diagrams | 1 | LOW |
| Migration guide | 1 | LOW |
| **TOTAL** | **47 days** (~9-10 weeks) | |

### Team Composition (Recommended)

- 1 Backend Developer (Vaani.API)
- 1 Desktop Developer (Vaani client)
- 1 QA Engineer
- 1 DevOps Engineer (deployment)

**Timeline:** 10-12 weeks with parallel development

---

## Breaking Changes

### API Changes

#### 1. Meeting Validation Response

**Before:**
```json
{
  "isValid": true,
  "encryptedConfig": "VAANI_ENC_v1_...",
  "meetingName": "Test Meeting",
  "validUntil": "2025-12-31T14:00:00Z"
}

// Decrypted config contains:
{
  "AzureConfig": {
    "SubscriptionKey": "abc123...",  // ❌ REMOVED
    "Region": "eastus"                // ❌ REMOVED
  }
}
```

**After:**
```json
{
  "isValid": true,
  "encryptedConfig": "VAANI_ENC_v1_...",
  "meetingName": "Test Meeting",
  "validUntil": "2025-12-31T14:00:00Z",
  "translationServiceUrl": "wss://api.vaani.com/translation/ws"  // ✅ NEW
}

// Decrypted config contains:
{
  "TranslationConfig": {
    "ServiceUrl": "wss://api.vaani.com/translation/ws",  // ✅ NEW
    "UseBackendService": true  // ✅ Feature flag
  }
}
```

#### 2. Desktop Client Code Changes

**Before:**
```csharp
// Direct Azure SDK usage
var config = SpeechTranslationConfig.FromSubscription(
    settings.AzureSubscriptionKey, 
    settings.AzureRegion
);
var recognizer = new TranslationRecognizer(config, audioConfig);
```

**After:**
```csharp
// Backend service client
var connection = new HubConnectionBuilder()
    .WithUrl(settings.TranslationServiceUrl)
    .Build();
await connection.StartAsync();
// Stream audio via SignalR
```

### Desktop App Update Required

**Impact:** ALL existing desktop installations must be updated to work with backend translation service.

**Migration Path:**
1. Deploy backend with both modes (feature flag)
2. Update desktop app
3. Force update via existing update mechanism
4. Deprecate direct Azure mode after 30 days

---

## Recommendations

### Phase 1: Preparation (Weeks 1-2)
1. ✅ Set up development environment for backend translation service
2. ✅ Create database migration scripts (if schema changes needed)
3. ✅ Design WebSocket/SignalR protocol specification
4. ✅ Create proof-of-concept for audio streaming
5. ✅ Document API contracts

### Phase 2: Backend Implementation (Weeks 3-5)
1. ✅ Implement `TranslationService` backend service
2. ✅ Create `TranslationController` with endpoints
3. ✅ Implement SignalR hub for real-time communication
4. ✅ Add audio streaming WebSocket handler
5. ✅ Update meeting validation to NOT send Azure keys
6. ✅ Add comprehensive logging and metrics

### Phase 3: Desktop Client Migration (Weeks 6-8)
1. ✅ Create `BackendTranslationService` implementing `ITranslationService`
2. ✅ Implement WebSocket/SignalR client
3. ✅ Update configuration models (remove Azure keys)
4. ✅ Add reconnection logic
5. ✅ Maintain backward compatibility (feature flag)
6. ✅ Remove Azure SDK dependency

### Phase 4: Testing & Validation (Weeks 9-10)
1. ✅ Unit tests for backend and client
2. ✅ Integration tests (end-to-end)
3. ✅ Performance testing (latency, throughput)
4. ✅ Load testing (multiple concurrent sessions)
5. ✅ Security audit (penetration testing)
6. ✅ User acceptance testing

### Phase 5: Deployment (Weeks 11-12)
1. ✅ Deploy backend to staging environment
2. ✅ Beta testing with select users
3. ✅ Gradual rollout with feature flag
4. ✅ Monitor metrics and user feedback
5. ✅ Full production deployment
6. ✅ Deprecate old API

---

## Alternative Approaches

### Alternative 1: Hybrid Approach (Partial Migration)

**Description:** Keep Azure SDK in desktop but proxy audio through backend for monitoring.

**Pros:**
- Lower latency (direct Azure connection)
- Simpler implementation
- Gradual migration path

**Cons:**
- Still exposes Azure credentials to client
- Limited security improvement
- More complex architecture

**Verdict:** ❌ Not recommended - doesn't solve primary security concern

### Alternative 2: Desktop Plugin/Extension Model

**Description:** Load Azure SDK dynamically from backend as encrypted plugin.

**Pros:**
- No latency overhead
- Easier credential rotation

**Cons:**
- Complex implementation
- Security through obscurity
- Credentials still in client memory

**Verdict:** ❌ Not recommended - overly complex, limited security benefit

### Alternative 3: Full Migration (Recommended)

**Description:** Move all Azure Cognitive Services calls to backend.

**Pros:**
- ✅ Maximum security (credentials never leave server)
- ✅ Centralized monitoring and control
- ✅ Easy credential rotation
- ✅ Better scalability
- ✅ Usage analytics

**Cons:**
- Increased latency (~100-200ms)
- More complex infrastructure
- Higher backend load

**Verdict:** ✅ **RECOMMENDED** - Best balance of security, maintainability, and functionality

---

## Success Criteria

### Functional Requirements
- [x] Desktop client can start/stop translation sessions via backend
- [x] Audio streaming maintains <500ms end-to-end latency
- [x] All existing translation features work (bidirectional, events, muting)
- [x] Session management unchanged (heartbeat, expiry)
- [x] Error handling and reconnection work seamlessly

### Non-Functional Requirements
- [x] Zero Azure credentials in desktop client code or storage
- [x] Backend can handle 50+ concurrent translation sessions
- [x] Audio quality matches current implementation (16kHz, 16-bit PCM)
- [x] 99.9% uptime for translation service
- [x] Comprehensive logging and monitoring

### Performance Benchmarks
- [x] P95 latency: <500ms (speech to translated audio)
- [x] WebSocket connection success rate: >99%
- [x] Reconnection time: <3 seconds
- [x] Backend CPU usage: <70% under load

---

## Conclusion

The migration of Azure Translation Service to the backend is **feasible and highly recommended** from both security and architectural perspectives. While it introduces additional latency (~100-200ms), the benefits of centralized credential management, usage monitoring, and improved security far outweigh the costs.

### Key Takeaways

1. **Security:** Critical improvement by removing Azure credentials from client
2. **Feasibility:** Well-defined architecture with proven technologies (SignalR, WebSocket)
3. **Complexity:** Moderate - requires significant refactoring but no insurmountable challenges
4. **Timeline:** 10-12 weeks with proper planning and parallel development
5. **Risk:** Medium-high, primarily around latency and connection stability - manageable with proper testing

### Next Steps

1. **Approval:** Get stakeholder sign-off on approach and timeline
2. **Detailed Design:** Create technical specification document
3. **Prototype:** Build proof-of-concept for audio streaming
4. **Resource Allocation:** Assign dedicated team
5. **Kickoff:** Begin Phase 1 (Preparation)

---

## Appendix

### A. File Inventory

#### Files to Modify (Desktop)
- `Vaani\Services\TranslationService.cs` (Major refactor)
- `Vaani\Services\ITranslationService.cs` (Interface unchanged)
- `Vaani\Authentication\Models\MeetingConfiguration.cs` (Remove Azure keys)
- `Vaani\Authentication\Services\SessionManager.cs` (Update session handling)
- `Vaani\Models\TranslationSettings.cs` (Remove Azure properties)
- `Vaani\Vaani.csproj` (Update dependencies)

#### Files to Create (Desktop)
- `Vaani\Services\BackendTranslationService.cs` (NEW - ~800 lines)
- `Vaani\Services\ITranslationWebSocketClient.cs` (NEW - ~100 lines)
- `Vaani\Services\AudioStreamHandler.cs` (NEW - ~300 lines)

#### Files to Create (Backend)
- `Vaani.API\Services\TranslationService.cs` (NEW - ~1000 lines)
- `Vaani.API\Interfaces\ITranslationService.cs` (NEW - ~100 lines)
- `Vaani.API\Controllers\TranslationController.cs` (NEW - ~300 lines)
- `Vaani.API\Hubs\TranslationHub.cs` (NEW - SignalR hub - ~200 lines)
- `Vaani.API\Middleware\TranslationWebSocketMiddleware.cs` (NEW - ~400 lines)
- `Vaani.API\Models\DTOs\TranslationRequest.cs` (NEW)
- `Vaani.API\Models\DTOs\TranslationResponse.cs` (NEW)
- `Vaani.API\Models\DTOs\AudioChunkDto.cs` (NEW)

#### Files to Modify (Backend)
- `Vaani.API\Services\MeetingService.cs` (Update validation response)
- `Vaani.API\Program.cs` (Register new services, configure SignalR)
- `Vaani.API\Vaani.API.csproj` (Add Azure SDK dependency)

### B. Configuration Examples

#### Backend appsettings.json
```json
{
  "Translation": {
    "AudioBufferSizeMs": 100,
    "MaxConcurrentSessions": 100,
    "WebSocketKeepAliveIntervalSeconds": 30,
    "ReconnectionTimeoutSeconds": 60
  },
  "Azure": {
    "ConnectionPoolSize": 20,
    "RequestTimeoutSeconds": 10
  }
}
```

#### Desktop appsettings.json
```json
{
  "Translation": {
    "BackendServiceUrl": "wss://api.vaani.com/translation",
    "ReconnectionAttempts": 5,
    "ReconnectionDelayMs": 2000,
    "AudioBufferSizeMs": 100
  }
}
```

### C. Performance Baseline Metrics

| Metric | Current (Direct Azure) | Target (Backend) |
|--------|------------------------|------------------|
| Recognition latency | 150-250ms | 250-400ms |
| Translation latency | 50-100ms | 50-100ms (same) |
| Synthesis latency | 100-200ms | 100-200ms (same) |
| **Total end-to-end** | **300-550ms** | **400-700ms** |
| Network overhead | - | +100-150ms |

### D. Database Schema (No Changes Required)

Existing schema already supports backend translation:

```sql
-- AzureSubscriptions table (existing)
CREATE TABLE azure_subscriptions (
    id SERIAL PRIMARY KEY,
    subscription_key VARCHAR(255) NOT NULL,  -- Encrypted
    region VARCHAR(50) NOT NULL,
    description TEXT,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP
);

-- Meetings table (existing) - links to azure_subscriptions
CREATE TABLE meetings (
    id SERIAL PRIMARY KEY,
    meeting_id VARCHAR(50) UNIQUE NOT NULL,
    azure_subscription_id INTEGER REFERENCES azure_subscriptions(id),
    -- ... other columns
);

-- Optional: Add translation_sessions table for monitoring
CREATE TABLE translation_sessions (
    id SERIAL PRIMARY KEY,
    session_id INTEGER REFERENCES sessions(id),
    started_at TIMESTAMP DEFAULT NOW(),
    ended_at TIMESTAMP,
    total_recognitions INTEGER DEFAULT 0,
    total_translations INTEGER DEFAULT 0,
    average_latency_ms INTEGER,
    errors_count INTEGER DEFAULT 0
);
```

---

**Assessment Date:** 2025-01-15  
**Assessment Version:** 1.0  
**Next Review:** After Phase 1 completion
