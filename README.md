# SPC Star

Internal SPC platform for manufacturing inspection, drift detection, lock overrides, material traceability, job handoff notes, setup management, and QA exports.

## Current milestone

This repository currently contains a working local browser/tablet-first SPC application:

- Core domain models for users, roles, permissions, setup data, jobs, inspection measurements, job notes, alerts, overrides, material traceability, and exports.
- SQLite-backed local server database with JSON fallback/import for existing development data.
- Standard Excel/CSV setup import with row types for job data, materials, measured variables, and accept/reject attributes.
- Manual setup screens for parts, operations, part-specific job data fields, measured variables, accept/reject attributes, sample size, frequency, and COA-required variables.
- User management screens for operators, line techs, QA, admins, and GOD access, including add/edit/delete with last-admin/GOD protection.
- Browser/tablet inspection console served by the API.
- Job, machine, part, and inspection phase selection before entry. Current phases are Startup, Setup, In Process, and Spool.
- Persistent job tag storage for part-specific context fields that will be driven by inspection setup.
- Part-specific material requirements from setup/import, with lot entry on the inspection screen.
- Ordered inspection-item entry for measured variables and accept/reject attributes, with inactive phase items removed from the operator view.
- Accept/Reject inspection support for comparator/template checks.
- Live row-based min, max, mean, standard deviation, Cp, Cpk, Pp, and Ppk summary for every active measured variable.
- Cp, Cpk, Pp, and Ppk calculations with shared red/yellow/green visual status cues.
- Trend chart rendering with chart type selection.
- Drift detection rule selection with a global default, part-level override, and editable system capability thresholds.
- Western Electric, Nelson-style trend, CUSUM, EWMA, moving average trend, linear trend/slope, custom default, spec-limit-only, and no-automatic-rule options.
- Drift alert creation and lock enforcement.
- Authorized override workflow with credential validation, cause/action notes, line tech/admin/QA/GOD support, and GOD-mode bypass reason validation.
- Material lot change logging for job/resource traceability.
- Timestamped job notes for operator handoff and issue history.
- History tab combining ledger review, charts, and job-data export. It carries part/job filters across Ledger, Charts, and Export.
- History ledger for part capability across all jobs, part/job review, measurement history, notes, locks, material history, and editable inspection entries.
- History measurement highlighting: red for out-of-spec values and yellow for out-of-control values.
- QA summary views and CSV export for one or more jobs, including mean, min, max, standard deviation, Cp, Cpk, Pp, and Ppk.
- Raw inspection, alert, material, and job history CSV exports.
- USB keyboard-style measurement capture support for gauges/scales/calipers that enter values into focused fields, including value cleanup and Enter-to-next-field behavior.
- Offline-oriented setup snapshot and retry-safe sync contracts.
- Unit tests for the high-risk rules and calculations.
- Dependency-free smoke tests that can run even when NuGet package restore is unavailable.
- Minimal API shell exposing the current app workflows.

## Run tests

With the .NET 8 SDK available, run:

```powershell
dotnet test SPCStar.sln
```

## Database

SPC-Star now defaults to a local SQLite database at `.appdata/spcstar.db`. If the database is empty and the older `.appdata/spcstar-data.json` file exists, the app imports that JSON data into SQLite on startup.

Useful environment variables:

- `SPCSTAR_DATABASE_PATH`: choose a different SQLite database file.
- `SPCSTAR_STORAGE_PROVIDER=json`: temporarily run against the older JSON file.
- `SPCSTAR_DATA_PATH`: choose a different JSON file path when using JSON storage.

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

## Standard Setup Import

The setup import is row-type based so one file can define the complete inspection plan for a part.

The in-app `Load Blank Template` button inserts headers only, with no sample part data. The import also supports the standardized Excel workbook sheet named `SPC-Star Import`.

The blank template uses readable columns grouped for manual entry. There is no `Section` column; SPC Star infers the row type from the column you fill in.

Primary readable columns:

`Part Number, Part Description, Product Group, Inspection Phase, Operation, Job Data Field, Material Name, Material Part Number, Material Description, Variable Name, Attribute Name, Required, Sort Order, Unit, Location, Inspection Method, Target, Lower Spec, Upper Spec, Lower Control, Upper Control, Drift Rule, COA Required, COA Statistic`

- Job data rows use `Job Data Field`, `Required`, and `Sort Order`.
- Material rows use `Material Name`, `Material Part Number`, `Material Description`, `Required`, and `Sort Order`.
- Variable rows use `Variable Name`, `Operation`, `Unit`, `Location`, `Inspection Method`, `Target`, `Lower Spec`, `Upper Spec`, optional control limits, sample/frequency columns, drift rule, and optional COA columns.
- Attribute rows use `Attribute Name`, `Operation`, `Location`, `Inspection Method`, sample/frequency columns, drift rule, and optional COA columns.
- Universal inspection rows can use phase-specific columns such as `Startup Required`, `Startup Sample Size`, `Setup Required`, `Setup Sample Size`, `In Process Required`, `In Process Sample Size`, `CoilChange Required`, `CoilChange Sample Size`, `Spool Required`, and `Spool Sample Size`.
- `Attribute/Variable`, `Tool Used`, `ParameterSeq`, and other standardized inspection-sheet conversion headers are accepted for bulk import workflows.

The importer also accepts the older technical column names for compatibility.

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
- `DELETE /setup/users/{userName}`
- `POST /setup/inspection-plans`
- `POST /setup/job-data-fields`
- `POST /setup/material-fields`
- `GET /setup/parts`
- `GET /setup/inspection-plans`
- `GET /sync/setup-snapshot`
- `POST /inspections/measurements`
- `POST /material-changes`
- `GET /jobs/{jobNum}/tags`
- `POST /jobs/{jobNum}/tags`
- `GET /jobs/{jobNum}/notes`
- `GET /jobs/{jobNum}/history`
- `POST /jobs/{jobNum}/notes`
- `POST /alerts/{alertId}/override`
- `POST /inspection-frequency/evaluate`
- `GET /work-context`
- `POST /charts/data`
- `POST /qa/summary`
- `POST /qa/summary.csv`
- `GET /qa/jobs/{jobNum}/variable-means`
- `GET /qa/jobs/{jobNum}/variable-means.csv`
- `GET /qa/job-variable-means`
- `GET /qa/job-variable-means.csv`
- `GET /review/part`
- `GET /review/job`
- `PATCH /review/measurements/{measurementId}`
- `POST /exports/inspection-data.csv`
- `GET /exports/jobs/{jobNum}/inspection-history.csv`
- `POST /exports/drift-alerts.csv`
- `POST /exports/material-changes.csv`
- `POST /sync/offline-changes`
- `GET /alerts/active`

The API also serves the browser/tablet inspection UI at `/`.

## Deploy On Local Network

For the fastest internal testing rollout, install SPC-Star on one Windows server and let operators open it through a browser from shop-floor computers.

Server install from the project folder:

```powershell
.\deploy\install-server.ps1
```

Server update after pulling newer code:

```powershell
.\deploy\update-server.ps1
```

Backup current local data:

```powershell
.\deploy\backup-data.ps1
```

The default server URL is:

```text
http://SERVER-NAME:5000/
```

The scripts publish the app to `C:\SPCStar\app`, store data at `C:\SPCStar\data\spcstar.db`, keep backups in `C:\SPCStar\backups`, and create a Windows Scheduled Task named `SPC-Star Server`.

See `deploy/README.md` for the deployment workflow.

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

## Current Data / Import Status

- Excel setup import is now supported. Upload workbooks with a sheet named `SPC-Star Import`; CSV import remains available as a fallback.
- Current development data has included Schneider, Ethicon Cutting Edge - Needles, Ethicon Cutting Edge - Drilled, Ethicon Taperpoint - Needles, Ethicon Taperpoint - Drilled, Everpoint, and Ethalloy/Cardio inspection-family imports.
- Product group names have been standardized around customer/family names such as Schneider, Ethicon Cutting Edge - Needles, Ethicon Cutting Edge - Drilled, Ethicon Taperpoint - Needles, and Ethicon Taperpoint - Drilled.

## Current Gaps / Next Work

- Continue validating loaded inspection plans against source sheets before production-floor pilot use.
- Prepare pilot rollout checklist: server install, backups, user permissions, operator sign-in/password reset, product group access, and test jobs.
- Authentication provider integration.
- Production database backup/restore drill and user/session hardening.
- Fully relational EF Core/SQL Server storage if the pilot requires a separate database engine.
- Full offline queue UI with conflict handling.
- Custom drift-rule editor for admin-defined thresholds and warning behavior.
- Box-level traceability once the required production count/source logic is defined.
- Native Web Serial/WebHID device profiles for gauges that do not behave like keyboard input.
- Broader History search/refinement for historical job notes, machine issues, drift events, and material events.

