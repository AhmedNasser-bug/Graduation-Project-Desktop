# Changelog

All notable changes to the NephroEdge Clinic Workstation Desktop (`Graduation-Project-Desktop`) are documented in this file.

## [0.2.0.0] - 2026-11-13

### Added
- Launch the clinical workstation as an Avalonia UI (`.NET 9`) desktop app (`NephroEdge.Desktop`) bound directly in-process via Scoped DI to `Services_Unit` and `Persistence_Unit`, complete with a standalone `NephroEdge.Desktop.slnx` solution and `.gitignore`.
- Keep patient context anchored across all four tabs with the single-row IPSG.1 `StickyPatientBanner` (avatar initials, dual patient identifiers, mutual-exclusion `HBV+` isolation badge, dry weight, access, bed, and `PatientSwitcherComboBox`).
- Triage active beds in `ShiftGridView` with clickable semantic filter chips (`All Beds`, `High Alarms`, `Warnings`, `Pending Ack`, `HBV+`), inline patient search, shift selector (`1–4`), IEC 60601-1-8 alarm color/shape badges, and whole-card tap navigation.
- Track longitudinal lab and adequacy trends in `AnalyticsView` with interactive hover tooltips, Potassium/Hemoglobin panic indicators, `5`/`10`/`20`-session toggles, and collapsible drawers for manual lab entry, logarithmic Daugirdas II `spKt/V` calculation, and dry-weight logs.
- Author dry-weight changes and dialysis prescriptions above the fold on `1366x768` and `1366x820` displays in `SafetyRxWorkbenchView` with ECRI zero-autopopulate safety defaults, opt-in standard templates, expandable ADE medication checks, FDA Criterion 4 explainability, and HL7 FHIR R4 bundle preview/export.
- Review and resolve bedside `409` order conflicts in `SyncMonitorView` with mandatory clinician rationale guardrails while keeping `:5050` USB ADB / Clinic Wi-Fi LAN diagnostics and outbox logs collapsed inside `TechnicalSyncExpander`.
- Automatically detect and reseed stale or duplicate-patient SQLite caches (`%#%` suffix rows, fewer than 12 distinct shift patients, or legacy schema errors) on startup via `ClinicWorkspaceBootstrap.NeedsFreshMendeleySeed`.
- Verify all four workstation screens, clinical safety invariants, interactive tooltips, and cross-screen geometry across `1024x700`, `1366x768`, `1366x820`, and `1920x1080` with `NephroEdge.Desktop.HeadlessTests` (`41/41` checks passing and configurable `NEPHROEDGE_SCREENSHOT_DIR` capture output).
