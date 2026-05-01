# SPC Star

Foundation-first internal SPC platform for manufacturing inspection, drift detection, lock overrides, material traceability, and QA exports.

## Current milestone

This repository contains the first backend-focused slice:

- Core domain models for users, roles, permissions, setup data, jobs, inspection measurements, alerts, overrides, and exports.
- SQLite-oriented schema and seed scripts.
- One-shot CSV setup import with validation and upsert behavior.
- Inspection measurement entry service.
- Western Electric rule detection service.
- Drift alert creation and lock enforcement.
- Authorized override workflow with GOD-mode bypass reason validation.
- Material lot change logging for job/resource traceability.
- QA summary CSV export.
- Unit tests for the high-risk rules and calculations.

## Run tests

The local session used to create this foundation did not have the .NET SDK on PATH. Once .NET 8 SDK is installed or available, run:

```powershell
dotnet test SPCStar.sln
```

## What is intentionally not built yet

- UI screens.
- Authentication provider integration.
- EF Core DbContext/migrations.
- XLSX import/export.
- Chart rendering.
- Full inspection frequency engine behavior beyond stored plan fields.
