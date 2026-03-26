# Azure Translation Service Backend Migration Plan

## Table of Contents
- [1. Executive Summary](#1-executive-summary)
- [2. Migration Strategy](#2-migration-strategy)
- [3. Detailed Dependency Analysis](#3-detailed-dependency-analysis)
- [4. Implementation Timeline](#4-implementation-timeline)
- [5. Detailed Execution Steps](#5-detailed-execution-steps)
- [6. Package Update Reference](#6-package-update-reference)
- [7. Breaking Changes Catalog](#7-breaking-changes-catalog)
- [8. Project-by-Project Migration Plans](#8-project-by-project-migration-plans)
- [9. Risk Management](#9-risk-management)
- [10. Testing & Validation Strategy](#10-testing--validation-strategy)
- [11. Complexity & Effort Assessment](#11-complexity--effort-assessment)
- [12. Source Control Strategy](#12-source-control-strategy)
- [13. Success Criteria](#13-success-criteria)

## 1. Executive Summary
This plan defines a backend-first migration of Azure Speech Translation from `Vaani` desktop client to `Vaani.API`.

### Goal
- Move Azure Cognitive Services Speech calls to server-side (`Vaani.API`)
- Keep Azure credentials out of desktop runtime and storage
- Preserve real-time, bidirectional translation UX
- Execute in two tracks: backend implementation first, then desktop integration

### Current State
- `Vaani\Services\TranslationService.cs` currently uses `Microsoft.CognitiveServices.Speech` directly
- Desktop receives Azure key/region via meeting validation configuration
- `Vaani.API` already stores Azure subscription records and meeting/session state

### Target State
- `Vaani.API` owns Azure SDK lifecycle and streaming translation sessions
- `Vaani` desktop streams audio to backend and receives translated events/audio
- Meeting/session flow remains same from business perspective

## 2. Migration Strategy
### Chosen Strategy: Backend-first phased migration with feature flag

#### Why this strategy
- Minimizes blast radius by stabilizing server-side translation first
- Enables side-by-side operation while desktop migration is in progress
- Supports controlled rollout and rollback

#### Phases
1. **Phase A (Backend Foundation)**
   - Add backend translation services, contracts, and endpoints
   - Keep existing meeting/session APIs stable
2. **Phase B (Backend Streaming + Operational Hardening)**
   - Add SignalR/WebSocket transport and connection/session handling
   - Add telemetry, limits, timeouts, retries
3. **Phase C (Desktop Integration)**
   - Add backend translation client in `Vaani`
   - Switch translation runtime from local Azure SDK to backend streams
4. **Phase D (Cleanup & Cutover)**
   - Remove Azure credentials from client models/storage
   - Remove desktop Azure SDK dependency

## 3. Detailed Dependency Analysis
### Solution projects
- `Vaani.API\Vaani.API.csproj` (`net8.0`) — backend migration target
- `Vaani\Vaani.csproj` (`net8.0`) — desktop integration target
- `Vaani.Admin\Vaani.Admin.esproj` — not in runtime path for translation streaming

### Dependency direction
- Desktop depends on backend APIs/contracts behavior
- Backend does not depend on desktop

### Ordering constraints
1. Implement backend APIs + streaming contracts first
2. Verify backend functionality with integration checks
3. Implement desktop client against finalized backend contracts
4. Remove desktop Azure direct dependency only after parity is proven

## 4. Implementation Timeline
### Milestones (backend then frontend)
- **M1: Backend contract and service layer** — 4 to 6 days
- **M2: Backend streaming endpoints + auth/session enforcement** — 4 to 6 days
- **M3: Backend observability + error handling + scale guards** — 2 to 3 days
- **M4: Desktop backend translation client and wiring** — 5 to 7 days
- **M5: Parity testing, feature-flag rollout, cleanup** — 3 to 5 days

### Total estimate
- **18 to 27 engineering days** depending on test depth and rollout controls

## 5. Detailed Execution Steps
### Step Group 1 — Backend contracts and service registration (`Vaani.API`)
1. Add package `Microsoft.CognitiveServices.Speech` to `Vaani.API`
2. Create `Interfaces\ITranslationService.cs`
3. Create DTOs:
   - `Models\DTOs\TranslationStartRequest.cs`
   - `Models\DTOs\TranslationStartResponse.cs`
   - `Models\DTOs\TranslationControlRequest.cs`
   - `Models\DTOs\TranslationStatusResponse.cs`
   - `Models\DTOs\AudioChunkDto.cs`
4. Register translation service and transport support in `Program.cs`
5. Add `Translation` settings section in `appsettings*.json`

### Step Group 2 — Backend runtime implementation (`Vaani.API`)
1. Create `Services\TranslationService.cs`
2. Resolve Azure subscription from DB by meeting/session context
3. Build per-session translation runtime manager
4. Implement lifecycle methods: start, stop, status, disconnect cleanup
5. Add resilient cancellation/timeout policy

### Step Group 3 — Backend transport layer (`Vaani.API`)
1. Create `Hubs\TranslationHub.cs` (SignalR)
2. Define hub methods for:
   - Opening translation channel
   - Sending binary audio chunks
   - Receiving translation events and optional synthesized audio
3. Enforce JWT/session validation on connection + method calls
4. Add payload validation and chunk-size limits
5. Add concurrent-session and rate-limit guards

### Step Group 4 — API controller surface (`Vaani.API`)
1. Create `Controllers\TranslationController.cs`
2. Expose endpoints:
   - `POST /api/translation/start`
   - `POST /api/translation/stop`
   - `GET /api/translation/status/{sessionId}`
3. Keep response contracts explicit and versionable

### Step Group 5 — Backend validation and hardening
1. Build `Vaani.API`
2. Validate authorization and session enforcement paths
3. Validate translation runtime teardown and leak-free disposal
4. Run concurrency tests (N parallel sessions)
5. Capture latency/error baseline and tune settings

### Step Group 6 — Desktop integration (`Vaani`)
1. Create `Services\BackendTranslationService.cs` implementing `ITranslationService`
2. Add SignalR client package and connection management
3. Replace local Azure SDK runtime invocation with backend hub flow
4. Keep event semantics stable for UI/view-models

### Step Group 7 — Desktop configuration/security updates
1. Update `Authentication\Models\MeetingConfiguration.cs` to avoid Azure key exposure
2. Update `Models\TranslationSettings.cs` to backend-service contract
3. Update `Authentication\Services\SessionManager.cs` serialization/storage model
4. Remove `Microsoft.CognitiveServices.Speech` dependency from desktop project after parity

### Step Group 8 — Cutover
1. Feature-flag pilot rollout
2. Validate production telemetry and failures
3. Progressive rollout
4. Sunset legacy direct-Azure mode

## 6. Package Update Reference
### Backend (`Vaani.API`)
- **Add** `Microsoft.CognitiveServices.Speech` (align with existing desktop version baseline, currently `1.47.0`)
- **Optional add** `Microsoft.AspNetCore.SignalR.Protocols.MessagePack` for transport optimization (if needed)

### Desktop (`Vaani`)
- **Add** `Microsoft.AspNetCore.SignalR.Client` (`8.x` compatible with runtime)
- **Remove** `Microsoft.CognitiveServices.Speech` once backend parity is validated

### Notes
- Keep both projects on compatible protocol + serialization settings
- Freeze package versions during migration window to reduce drift

## 7. Breaking Changes Catalog
1. **Meeting configuration payload contract**
   - Remove Azure credentials from client-facing config
   - Introduce backend translation endpoint/runtime hints instead
2. **Desktop translation runtime behavior**
   - Local SDK operations replaced by remote session operations
3. **Operational model**
   - Backend availability now required for translation start

### Compatibility plan
- Introduce `UseBackendTranslation` flag in meeting/session configuration
- Allow temporary dual-mode operation while migrating clients

## 8. Project-by-Project Migration Plans
### `Vaani.API`
- Add translation domain contracts + DTOs
- Add translation runtime service and state manager
- Add `TranslationHub` + `TranslationController`
- Register services and policies in `Program.cs`
- Add configuration knobs in `appsettings*.json`

### `Vaani`
- Add backend translation client service
- Keep existing UI/event interfaces unchanged
- Refactor translation orchestration to remote transport
- Remove local Azure credential dependence in config/session models

### `Vaani.Admin`
- No runtime dependency for first cut
- Optional future work: operational dashboard for translation sessions

## 9. Risk Management
### Top risks and mitigations
1. **Latency regression**
   - Mitigation: chunk-size tuning, binary transport, region alignment, baseline + target SLO
2. **Connection instability**
   - Mitigation: reconnect policy, heartbeat, graceful restart semantics
3. **Backend resource pressure**
   - Mitigation: max concurrent sessions, queue bounds, circuit-breaking
4. **Session security drift**
   - Mitigation: enforce JWT/session checks on every control/data path
5. **Feature parity gaps**
   - Mitigation: explicit parity checklist and acceptance tests before cleanup

## 10. Testing & Validation Strategy
### Backend validation
- Unit tests for service lifecycle and state manager
- Hub/controller tests for auth, validation, and contract responses
- Integration tests for start/stream/stop sequence

### Desktop validation
- Service-level tests for reconnect/state transitions
- UI-level checks for event continuity and mute semantics

### End-to-end validation
- Full duplex translation scenario tests
- Session expiry/invalid token tests
- Multi-session concurrency tests
- Performance targets:
  - P95 translation round-trip within agreed threshold
  - Connection success rate and reconnect recovery within SLA

## 11. Complexity & Effort Assessment
- **Complexity:** Medium-High
- **Primary complexity drivers:** real-time audio streaming, session state handling, parity retention
- **Estimated effort:** 18–27 engineering days

### Effort split
- Backend: ~55%
- Desktop: ~35%
- QA/rollout hardening: ~10%

## 12. Source Control Strategy
### Branching
- Start from current working branch
- Create dedicated branch: `feature/backend-translation-migration`

### Commit sequencing
1. Backend contracts + scaffolding
2. Backend runtime + transport
3. Backend tests/hardening
4. Desktop integration
5. Desktop cleanup/removal of direct Azure dependency
6. Rollout flags/docs

### Rollback
- Keep feature-flag guarded path until final cutover
- Revert-to-legacy mode should be config-controlled

## 13. Success Criteria
### Functional
- Backend can start/stop/status translation sessions for authenticated sessions
- Desktop translation works through backend with no major UX regressions
- Existing meeting/session lifecycle remains functional

### Security
- No Azure key/region required or persisted on desktop runtime for backend mode
- Backend is sole owner of Azure credential usage

### Operational
- Stable translation sessions under expected concurrent load
- Observability in place for connection failures, latency, and runtime errors
- Controlled rollout completed without critical incidents

---

**Plan Version:** 1.0  
**Scope:** Backend-first migration followed by desktop integration  
**Project Targets:** `.NET 8` (`Vaani.API`, `Vaani`)
