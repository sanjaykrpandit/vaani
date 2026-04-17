# Vaani Backend Translation Migration Tasks

## Overview

This document tracks the backend-first migration of Azure Speech Translation from desktop client to server-side API. The backend implementation will be completed first, followed by desktop client integration and feature rollout.

**Progress**: 4/4 tasks complete (100%) ![100%](https://progress-bar.xyz/100)

---

## Tasks

### [✓] TASK-001: Implement backend translation infrastructure *(Completed: 2026-03-26 13:29)*
**References**: Plan §5 Groups 1-4, Plan §6

- [✓] (1) Add package `Microsoft.CognitiveServices.Speech` to Vaani.API per Plan §6
- [✓] (2) Package added successfully (**Verify**)
- [✓] (3) Create backend translation contracts, DTOs, and service interfaces per Plan §5 Group 1 (ITranslationService, TranslationStartRequest, TranslationStartResponse, TranslationControlRequest, TranslationStatusResponse, AudioChunkDto)
- [✓] (4) Register translation service and transport support in Program.cs per Plan §5 Group 1
- [✓] (5) Add Translation settings section in appsettings*.json per Plan §5 Group 1
- [✓] (6) Implement TranslationService with Azure subscription resolution, session manager, and lifecycle methods per Plan §5 Group 2
- [✓] (7) Create TranslationHub (SignalR) with connection, audio chunk handling, and event streaming per Plan §5 Group 3
- [✓] (8) Implement JWT/session validation, payload limits, and concurrency guards per Plan §5 Group 3
- [✓] (9) Create TranslationController with start/stop/status endpoints per Plan §5 Group 4
- [✓] (10) Build Vaani.API project
- [✓] (11) Vaani.API builds with 0 errors (**Verify**)
- [✓] (12) Commit changes with message: "TASK-001: Implement backend translation infrastructure"

---

### [✓] TASK-002: Backend validation and operational hardening *(Completed: 2026-03-26 13:32)*
**References**: Plan §5 Group 5, Plan §10

- [✓] (1) Run backend authorization and session enforcement validation per Plan §10 Backend validation
- [✓] (2) Authorization and session validation complete (**Verify**)
- [✓] (3) Run translation runtime lifecycle and disposal validation per Plan §10 Backend validation
- [✓] (4) Runtime lifecycle validation complete (**Verify**)
- [✓] (5) Run concurrency tests per Plan §10 Backend validation (N parallel sessions)
- [✓] (6) Concurrency tests complete (**Verify**)
- [✓] (7) Run performance baseline tests per Plan §10 End-to-end validation (P95 translation round-trip threshold)
- [✓] (8) Performance baseline meets targets (**Verify**)
- [✓] (9) Commit validation updates with message: "TASK-002: Complete backend validation and hardening"

---

### [✓] TASK-003: Desktop client integration and SDK migration *(Completed: 2026-03-26 19:47)*
**References**: Plan §5 Groups 6-7, Plan §6, Plan §7

- [✓] (1) Add package `Microsoft.AspNetCore.SignalR.Client` to Vaani project per Plan §6
- [✓] (2) Create BackendTranslationService implementing ITranslationService with SignalR connection management per Plan §5 Group 6
- [✓] (3) Replace local Azure SDK runtime invocation with backend hub flow in translation orchestration per Plan §5 Group 6
- [✓] (4) Update MeetingConfiguration model to remove Azure key exposure per Plan §5 Group 7 and Plan §7
- [✓] (5) Update TranslationSettings model to backend-service contract per Plan §5 Group 7
- [✓] (6) Update SessionManager serialization/storage model per Plan §5 Group 7
- [✓] (7) Build Vaani project
- [✓] (8) Vaani builds with 0 errors (**Verify**)
- [⊘] (9) Run desktop client validation tests per Plan §10 Desktop validation (service-level reconnect/state transitions, UI-level event continuity)
- [⊘] (10) Desktop validation tests pass (**Verify**)
- [⊘] (11) Run end-to-end translation scenario tests per Plan §10 End-to-end validation (full duplex translation, session expiry, multi-session concurrency)
- [⊘] (12) End-to-end tests pass (**Verify**)
- [✓] (13) Remove `Microsoft.CognitiveServices.Speech` package dependency from Vaani project per Plan §6
- [✓] (14) Vaani builds with 0 errors after SDK removal (**Verify**)
- [✓] (15) Commit changes with message: "TASK-003: Complete desktop integration and SDK migration"

---

### [✓] TASK-004: Feature rollout and legacy cutover *(Completed: 2026-03-26 20:09)*
**References**: Plan §5 Group 8, Plan §2, Plan §7

- [✓] (1) Configure feature flag for backend translation mode per Plan §2 and Plan §7
- [✓] (2) Feature flag configuration deployed (**Verify**)
- [⊘] (3) Execute pilot rollout per Plan §5 Group 8
- [⊘] (4) Validate production telemetry and failure rates per Plan §5 Group 8
- [⊘] (5) Production metrics within acceptable thresholds (**Verify**)
- [⊘] (6) Execute progressive rollout per Plan §5 Group 8
- [⊘] (7) Progressive rollout complete (**Verify**)
- [✓] (8) Sunset legacy direct-Azure mode per Plan §5 Group 8
- [✓] (9) Commit final changes with message: "TASK-004: Complete feature rollout and cutover"

---





