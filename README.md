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
- Authorized override workflow with credential validation and GOD-mode bypass reason validation.
- Material lot change logging for job/resource traceability.
- QA summary CSV export.
- Unit tests for the high-risk rules and calculations.
- Dependency-free smoke tests that can run even when NuGet package restore is unavailable.
- Minimal API shell exposing the first backend workflows.

## Run tests

The local session used to create this foundation did not have the .NET SDK on PATH. Once .NET 8 SDK is installed or available, run:

```powershell
dotnet test SPCStar.sln
```

If NuGet is unavailable, run the dependency-free smoke tests:

```powershell
dotnet run --project tests/SPCStar.SmokeTests/SPCStar.SmokeTests.csproj
```

## Run API locally

```powershell
dotnet run --project src/SPCStar.Api/SPCStar.Api.csproj
```

Then check:

```powershell
http://localhost:5000/health
```

Example requests are in `docs/api-examples.http`.

Initial endpoints include:

- `GET /health`
- `POST /setup/import-csv`
- `GET /setup/parts`
- `GET /setup/inspection-plans`
- `POST /inspections/measurements`
- `POST /material-changes`
- `POST /alerts/{alertId}/override`
- `POST /inspection-frequency/evaluate`
- `POST /charts/data`
- `POST /qa/summary`
- `POST /qa/summary.csv`
- `POST /exports/inspection-data.csv`
- `GET /exports/jobs/{jobNum}/inspection-history.csv`
- `POST /exports/drift-alerts.csv`
- `POST /exports/material-changes.csv`
- `GET /alerts/active`

The API seeds demo security users and one sample inspection plan:

- Part `P100`
- Process `MOLD`
- Operation `10`
- Characteristic `Diameter`
- Spec limits `4.5` to `5.5` mm
- Control limits `4.0` to `6.0`
- Time frequency every `30` minutes

## What is intentionally not built yet

- UI screens.
- Authentication provider integration.
- EF Core DbContext/migrations.
- XLSX import/export.
- Chart rendering.
- Full inspection frequency engine behavior beyond stored plan fields.
