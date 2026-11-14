# NephroGuard — Clinic Workstation Desktop App (`Graduation-Project-Desktop`)

> **Platform Hub:** [`NephroGuard.vercel.app`](https://nephroguard.vercel.app)  
> **Related Repositories:**  
> - **Bedside Mobile App:** [`AhmedNasser-bug/Graduation-Project-Mobile`](https://github.com/AhmedNasser-bug/Graduation-Project-Mobile)  
> - **Backend Engine (`.NET 9`):** [`xByteA/Graduation-Project-Backend`](https://github.com/xByteA/Graduation-Project-Backend)  
> - **Master Documentation Hub:** [`xByteA/Graduation-Project-Documentation`](https://github.com/xByteA/Graduation-Project-Documentation)

Cross-platform **.NET 9 + Avalonia UI 11** clinical workstation running in-process Scoped DI against `Services_Unit` and `Persistence_Unit` (SQLite WAL + LiteDB + Mendeley 12,404-session cohort).

---

## Option 1 — One-Command Native Setup (`.NET 9 SDK`)

Clone next to `backend/` (the setup script automatically clones/checks out the compatible backend branch if `../backend` is missing):

**Windows (PowerShell):**

```powershell
git clone https://github.com/AhmedNasser-bug/Graduation-Project-Desktop.git desktop
cd desktop
.\setup.ps1
```

To verify build + all `41` headless tests without opening the GUI window:

```powershell
.\setup.ps1 -VerifyOnly
```

**macOS / Linux:**

```bash
git clone https://github.com/AhmedNasser-bug/Graduation-Project-Desktop.git desktop
cd desktop
chmod +x setup.sh
./setup.sh
```

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
