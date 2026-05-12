# SPC Star

Internal SPC platform for manufacturing inspection, drift detection, lock overrides, material traceability, job handoff notes, setup management, and QA exports.

## Current milestone

This repository currently contains a working local browser/tablet-first SPC application:

- Core domain models for users, roles, permissions, setup data, jobs, inspection measurements, job notes, alerts, overrides, material traceability, and exports.
- SQLite-oriented schema and seed scripts.
- CSV setup import with validation and upsert behavior.
- Manual setup screens for parts, operations, measured variables, accept/reject variables, sample size, frequency, and COA-required variables.
- User management screens for operators, line techs, QA, admins, and GOD access.
- Browser/tablet inspection console served by the API.
- Job, machine, part, and inspection phase selection before entry.
- Multi-variable measurement entry with sample-size based input rows.
- Accept/Reject inspection support.
- Running mean summary for every active variable.
- Trend chart rendering with chart type selection.
- Drift detection rule selection with a global default and part-level override.
- Western Electric, Nelson-style trend, CUSUM, EWMA, moving average trend, linear trend/slope, custom default, spec-limit-only, and no-automatic-rule options.
- Drift alert creation and lock enforcement.
- Authorized override workflow with credential validation, cause/action notes, line tech/admin/QA/GOD support, and GOD-mode bypass reason validation.
- Material lot change logging for job/resource traceability.
- Timestamped job notes for operator handoff and issue history.
- QA summary views and CSV export for one or more jobs.
- Raw inspection, alert, material, and job history CSV exports.
- Offline-oriented setup snapshot and retry-safe sync contracts.
- Unit tests for the high-risk rules and calculations.
- Dependency-free smoke tests that can run even when NuGet package restore is unavailable.
- Minimal API shell exposing the current app workflows.

## Run tests

With the .NET 8 SDK available, run:

```powershell
dotnet test SPCStar.sln
```

If NuGet is unavailable, run the dependency-free smoke tests:

```powershell
dotnet run --project tests/SPCStar.SmokeTests/SPCStar.SmokeTests.csproj
```

## Run API locally

```powershell
dotnet run --project src/SPCStar.Api/SPCStar.Api.csproj --urls "http://0.0.0.0:5000;http://0.0.0.0:5088"
```

Then check:

```powershell
http://localhost:5000/
http://localhost:5000/health
```

Example requests are in `docs/api-examples.http`.

Initial endpoints include:

- `GET /health`
- `POST /auth/login`
- `GET /auth/me`
- `POST /setup/import-csv`
- `GET /setup/users`
- `GET /setup/roles`
- `GET /setup/settings`
- `POST /setup/settings`
- `POST /setup/users`
- `POST /setup/inspection-plans`
- `GET /setup/parts`
- `GET /setup/inspection-plans`
- `GET /sync/setup-snapshot`
- `POST /inspections/measurements`
- `POST /material-changes`
- `GET /jobs/{jobNum}/notes`
- `POST /jobs/{jobNum}/notes`
- `POST /alerts/{alertId}/override`
- `POST /inspection-frequency/evaluate`
- `GET /work-context`
- `POST /charts/data`
- `POST /qa/summary`
- `POST /qa/summary.csv`
- `POST /exports/inspection-data.csv`
- `GET /exports/jobs/{jobNum}/inspection-history.csv`
- `POST /exports/drift-alerts.csv`
- `POST /exports/material-changes.csv`
- `POST /sync/offline-changes`
- `GET /alerts/active`

The API also serves the browser/tablet inspection UI at `/`.

The API seeds demo security users and one sample inspection plan:

- Users `operator1`, `linetech1`, `qa1`, `admin1`, and `god1`
- Demo passwords match the usernames
- Part `P100`
- Process `MOLD`
- Operation `10`
- Characteristics `Diameter`, `Length`, and `Weight`
- Diameter spec limits `4.5` to `5.5` mm
- Diameter control limits `4.0` to `6.0`
- Time frequency every `30` minutes

## Current gaps / next work

- Authentication provider integration.
- EF Core DbContext/migrations.
- XLSX import/export.
- Production database deployment and user/session hardening.
- Full offline queue UI with conflict handling.
- Custom drift-rule editor for admin-defined thresholds and warning behavior.
- Box-level traceability once the required production count/source logic is defined.
- Broader reporting/search screens for historical job notes, machine issues, drift events, and material events.
