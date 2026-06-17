# SPC Star

Internal SPC platform for manufacturing inspection, drift detection, lock overrides, material traceability, job handoff notes, setup management, and QA exports.

## Current milestone

This repository currently contains a working local browser/tablet-first SPC application:

- Core domain models for users, roles, permissions, setup data, jobs, inspection measurements, job notes, alerts, overrides, material traceability, and exports.
- SQLite-backed local server database with JSON fallback/import for existing development data.
- Standard CSV setup import with row types for job data, materials, measured variables, and accept/reject attributes.
- Manual setup screens for parts, operations, part-specific job data fields, measured variables, accept/reject attributes, sample size, frequency, and COA-required variables.
- User management screens for operators, line techs, QA, admins, and GOD access, including add/edit/delete with last-admin/GOD protection.
- Browser/tablet inspection console served by the API.
- Job, machine, part, and inspection phase selection before entry. Current phases are Startup, Setup, In Process, and Spool.
- Persistent job tag storage for part-specific context fields that will be driven by inspection setup.
- Part-specific material requirements from setup/import, with lot entry on the inspection screen.
- Multi-variable measurement entry with measured variables separated from accept/reject attribute checks.
- Accept/Reject inspection support for comparator/template checks.
- Live row-based min, max, mean, standard deviation, Cp, Cpk, Pp, and Ppk summary for every active measured variable.
- Cp, Cpk, Pp, and Ppk calculations with shared red/yellow/green visual status cues.
- Trend chart rendering with chart type selection.
- Drift detection rule selection with a global default and part-level override.
- Western Electric, Nelson-style trend, CUSUM, EWMA, moving average trend, linear trend/slope, custom default, spec-limit-only, and no-automatic-rule options.
- Drift alert creation and lock enforcement.
- Authorized override workflow with credential validation, cause/action notes, line tech/admin/QA/GOD support, and GOD-mode bypass reason validation.
- Material lot change logging for job/resource traceability.
- Timestamped job notes for operator handoff and issue history.
- Review tab for part capability across all jobs, part/job job review, measurement history, notes, locks, material history, and editable inspection entries.
- Review measurement highlighting: red for out-of-spec values and yellow for out-of-control values.
- QA summary views and CSV export for one or more jobs.
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

## Standard setup import

The setup import is row-type based so one file can define the complete inspection plan for a part.

The in-app `Load Blank Template` button inserts headers only, with no sample part data.

The blank template uses readable columns grouped for manual entry. There is no `Section` column; SPC Star infers the row type from the column you fill in.

`Part Number, Part Description, Product Group, Inspection Phase, Operation, Job Data Field, Material Name, Material Part Number, Material Description, Variable Name, Attribute Name, Required, Sort Order, Unit, Target, Lower Spec, Upper Spec, Lower Control, Upper Control, Sample Size, Frequency Type, Frequency, Frequency Unit, Drift Rule, COA Required, COA Statistic`

- Job data rows use `Job Data Field`, `Required`, and `Sort Order`.
- Material rows use `Material Name`, `Material Part Number`, `Material Description`, `Required`, and `Sort Order`.
- Variable rows use `Variable Name`, `Operation`, `Unit`, `Target`, `Lower Spec`, `Upper Spec`, optional control limits, sample/frequency columns, drift rule, and COA columns.
- Attribute rows use `Attribute Name`, `Operation`, `Unit=Accept/Reject`, sample/frequency columns, drift rule, and COA columns.

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

## Deploy on local network

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

## Current gaps / next work

- Excel setup import is now supported. Upload workbooks with a sheet named `SPC-Star Import`; CSV import remains available as a fallback.
- Cutting Edge inspection families have been prepared and loaded into the local test data set: Needlemaker, Test Polish, Strip Polish, and Drilled.
- Next inspection-family import work: Taperpoint.
- Authentication provider integration.
- Production database backup/restore workflow and user/session hardening.
- Fully relational EF Core/SQL Server storage if the pilot requires a separate database engine.
- Full offline queue UI with conflict handling.
- Custom drift-rule editor for admin-defined thresholds and warning behavior.
- Box-level traceability once the required production count/source logic is defined.
- Native Web Serial/WebHID device profiles for gauges that do not behave like keyboard input.
- Broader reporting/search screens for historical job notes, machine issues, drift events, and material events.

