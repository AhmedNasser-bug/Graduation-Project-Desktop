# NephroGuard — Clinic Workstation Desktop App (`Graduation-Project-Desktop`)

> **Platform Hub & Landing Portal:** [`AhmedNasser-bug/Grad_project_landing`](https://github.com/AhmedNasser-bug/Grad_project_landing)  
> **Related Repositories:**  
> - **Bedside Mobile App:** [`AhmedNasser-bug/Graduation-Project-Mobile`](https://github.com/AhmedNasser-bug/Graduation-Project-Mobile)  
> - **Backend Engine (`.NET 9`):** [`xByteA/Graduation-Project-Backend`](https://github.com/xByteA/Graduation-Project-Backend)  
> - **Master Documentation Hub:** [`xByteA/Graduation-Project-Documentation`](https://github.com/xByteA/Graduation-Project-Documentation)

Cross-platform **.NET 9 + Avalonia UI 11** clinical workstation running in-process Scoped DI against `Services_Unit` and `Persistence_Unit` (SQLite WAL + LiteDB + Mendeley 12,404-session cohort).

---

## Option 1 — One-Command Native Setup (`.NET 9 SDK`)

**Prerequisites:** `Git`, `.NET 9 SDK`, and read access to [`xByteA/Graduation-Project-Backend`](https://github.com/xByteA/Graduation-Project-Backend).

Clone next to `backend/` (if `../backend` is missing or lacks the desktop contracts, `setup.ps1` / `setup.sh` automatically clones `xByteA/Graduation-Project-Backend` and checks out branch `feat/analytics-sync-refactor` until Backend PR #1 merges):

**Windows (PowerShell):**

```powershell
git clone https://github.com/AhmedNasser-bug/Graduation-Project-Desktop.git desktop
cd desktop
.\setup.ps1
```

To verify the build + all `41` headless tests without opening the GUI window (or skip tests when launching quickly):

```powershell
.\setup.ps1 -VerifyOnly
.\setup.ps1 -SkipTests
```

Override the sibling backend path if needed with `.\setup.ps1 -BackendDir <path>`.

**macOS / Linux:**

```bash
git clone https://github.com/AhmedNasser-bug/Graduation-Project-Desktop.git desktop
cd desktop
chmod +x setup.sh
./setup.sh
```

Run `./setup.sh --verify-only` to run build + headless tests in CI/headless mode, or set `NEPHROEDGE_BACKEND_DIR=<path>` to point at an existing backend checkout.

---

## Option 2 — Reproducible Docker Verification Suite (`.NET 9 + Skia Headless`)

Run the 1-command Docker verification (stages only the needed backend/desktop sources and runs the full Avalonia Headless UI suite inside `mcr.microsoft.com/dotnet/sdk:9.0`):

**Windows:**

```powershell
.\setup.ps1 -Docker
```

**macOS / Linux:**

```bash
./setup.sh --docker
```

*(If Docker Compose plugin is installed on your machine, `docker compose run --rm desktop-tests` from inside `desktop/` also works directly.)*

---

## Architecture Highlights

1. **Shift Risk Grid (`ShiftGridView`):** 12-bed active triage board with semantic filter chips (`All Beds`, `High Alarms`, `Warnings`, `Pending Ack`, `HBV+`), IEC 60601-1-8 alarm badges, and whole-card selection.
2. **Patient Analytics (`AnalyticsView`):** Longitudinal Potassium/Hemoglobin panic thresholds, `5`/`10`/`20`-session trend switcher, manual lab logging, Daugirdas II `spKt/V` calculator, and dry-weight log.
3. **Safety & Rx Workbench (`SafetyRxWorkbenchView`):** ECRI zero-autopopulate safety defaults, ADE / FDA Criterion 4 explainability, and HL7 FHIR R4 bundle preview/export.
4. **Sync & Conflicts (`SyncMonitorView`):** Bedside `409` conflict resolution with mandatory clinician rationale + collapsed USB ADB / LAN (`:5050`) diagnostics.

---

## Architecture & Engineering Docs

Detailed specs live in [`xByteA/Graduation-Project-Documentation`](https://github.com/xByteA/Graduation-Project-Documentation):

- **System Architecture & Diagrams:** `architecture/system-architecture.md`, `architecture/dependency-graph.md`
- **Decision Log (`ADR-007` & `ADR-008`):** `architecture/BACKEND_DESIGN_LOG.md`
- **API & Sync Contracts (`:5050`):** `api/API_CONTRACTS_V1.md`
- **Deferred Scope (`M2-REG`, `M2-AUTH`, `M5` SignalR, `M7` Packaging):** [`TODOS.md`](./TODOS.md) and `milestones-and-backlog/OUT_OF_SCOPE_BACKLOG.md`
