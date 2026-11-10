# NephroEdge — Clinic Workstation Desktop App (`Graduation-Project-Desktop`)

> **Master Documentation & Quick Dev Setup Hub:**  
> 👉 **[xByteA/Graduation-Project-Documentation](https://github.com/xByteA/Graduation-Project-Documentation)** *(Contains `API_CONTRACTS_V1.md`, `OUT_OF_SCOPE_BACKLOG.md`, and the 5-repo architecture diagrams)*

---

## 1. Prototype Scope & Post-Headstart Direct C# Transition

* **Target Prototype:** A running desktop workstation application displaying the live **12-Bed Active Shift Session Board (`vw_ActiveShiftSessionBoard`)** seeded and battle-tested with the real 12,404-session Mendeley clinical hemodialysis dataset (`7kmtsmsgfw-1/dataset_11.csv.zip`), integrated with the open-source medical catalogs (ICD-10, LOINC, RxNorm, SNOMED) and synchronized with the Mobile Bedside app.
* **Headstart vs. Post-Headstart Data Binding:**
  * **During Headstart Prototype:** Communicates with `Graduation-Project-Backend` via the essential local endpoints (`http://127.0.0.1:5050/api/v1/shift/active`, `/api/v1/troubleshooter/*`, `/api/v1/patients/{id}/summary`).
  * **Post-Headstart Transition (`OUT_OF_SCOPE_BACKLOG.md` #1.3):** Transitions to **direct programmatic in-process C# invocation** of `Services_Unit` and `Persistence_Unit` inside the native `Photino.NET` desktop host.
