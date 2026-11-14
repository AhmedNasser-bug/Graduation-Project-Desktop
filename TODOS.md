# TODOS

## Desktop Infrastructure

### Migrate private remote from `AhmedNasser-bug` to `xByteA` organization when org repo is provisioned

**What:** Mirror or transfer `AhmedNasser-bug/Graduation-Project-Desktop` to `xByteA/Graduation-Project-Desktop` and update `git remote origin`.

**Why:** Aligns repository ownership across all five `xByteA` graduation project repositories for team onboarding.

**Context:** Current `origin` is hosted publicly at `https://github.com/AhmedNasser-bug/Graduation-Project-Desktop.git` because `xByteA/Graduation-Project-Desktop` had not been created during the `v0.2.0.0` ship window.

**Effort:** S
**Priority:** P3
**Depends on:** None

### Wire live Bedside Mobile tablet polling and SignalR `ShiftHub` updates

**What:** Rewire `Nurse interface/mobile-prototype` from isolated `sql.js` / `:5175` to the Desktop `:5050` `LocalSyncHttpHost` bridge and add sub-second SignalR directive notifications (`M5`).

**Why:** Delivers live bidirectional doctor-to-nurse bedside handshakes beyond local loopback simulation tests.

**Context:** `LocalSyncHttpHost` and `SyncMonitorViewModel` already expose `/api/v1/shift/active`, `/telemetry`, `/incidents`, `/handshakes/{id}/ack`, and `CONFLICT_REQUIRES_CLINICIAN_ACK` resolution; mobile client wiring and SignalR `ShiftHub` remain deferred per ADR-007/ADR-008.

**Effort:** L
**Priority:** P1
**Depends on:** Graduation-Project-Mobile `:5050` client rewire

### Add 4-Step Patient Registration Wizard (`M2-REG`) and Staff PIN Gate (`M2-AUTH`)

**What:** Build the Avalonia patient onboarding view (14-digit Egyptian National ID parser + viral hepatitis isolation step) and initial staff PIN lock screen.

**Why:** Enables new patient intake and role-gated sign-off inside the clinic workstation.

**Context:** Currently blocked on `Services_Unit` `IPatientService.RegisterPatientAsync` and `Middleware_Unit` `IdentityService` (`OUT_OF_SCOPE_BACKLOG.md` sections 1.1–1.2).

**Effort:** L
**Priority:** P2
**Depends on:** `Graduation-Project-Backend` M2-REG and M2-AUTH

### Configure Self-Contained Desktop Packaging, Local WAL Backups & Silent Updates (`M7`)

**What:** Set up release packaging (`PublishTrimmed=false` to preserve Dapper/LiteDB/Firely reflection), daily SQLite WAL backup rotation, and Velopack delta updates.

**Why:** Prepares `NephroEdge.Desktop` for clinic workstation installation without manual `.NET 9` SDK setup.

**Context:** ADR-007 explicitly documents `PublishTrimmed=false` as a hard requirement for `Dapper`, `LiteDB`, and `Hl7.Fhir.R4`.

**Effort:** M
**Priority:** P3
**Depends on:** None

## Completed

### Build Progressive Disclosure Workstation UI & 41-Iteration Headless UX Geometry Suite

**What:** Implemented `ShiftGridView`, `AnalyticsView`, `SafetyRxWorkbenchView`, `SyncMonitorView`, `StickyPatientBanner`, `ClinicWorkspaceBootstrap` auto-heal, and 41 headless UX/safety/geometry assertions.

**Why:** Delivered an above-the-fold `1366x768`/`1366x820` clinical workstation compliant with IPSG.1, IEC 60601-1-8, ISMP, and ECRI zero-autopopulate safety rules.

**Context:** Verified with `NephroEdge.Desktop.HeadlessTests` (`41/41` pass, 19 visual audit captures) and Win32 `--smoke-exit-ms 2500` exit `0`.

**Effort:** XL
**Priority:** P0
**Depends on:** None
**Completed:** v0.2.0.0 (2026-11-13)
