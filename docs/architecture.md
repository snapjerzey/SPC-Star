# SPC Star Architecture Notes

## Current shape

SPC Star currently has these main projects:

- `SPCStar.Core`: domain entities and business services.
- `SPCStar.Api`: minimal HTTP API over the core services.
- `SPCStar.Api/wwwroot`: browser/tablet inspection UI served by the API.
- `SPCStar.SmokeTests`: dependency-free executable tests for sandbox-safe verification.

The xUnit project remains in place for normal development and CI.

## Current storage

The app uses `ISpcRepository` with an `InMemorySpcRepository` implementation for tests and a `SqliteBackedSpcRepository` for normal local/server operation. SQLite stores the current repository snapshot in `.appdata/spcstar.db` by default, with WAL journaling enabled. `FileBackedSpcRepository` remains as a JSON fallback and one-time import source for older development data.

This is intentionally still behind the repository/service boundary. Business rules stay isolated from storage so a future EF Core/SQL Server provider can be added without rewriting the inspection, drift, import, and review services.

## Implemented services

- `SetupImportService`: row-type Excel/CSV setup validation and upsert for job data, material requirements, measured variables, accept/reject attributes, phase-specific requirements, sample sizes, frequency, and display order.
- `SetupManagementService`: users, global settings, capability thresholds, manual part/operation/inspection setup, part-specific job data fields, and material setup.
- `SetupQueryService`: tablet setup snapshot, part lookup, job data field lookup, material field lookup, inspection plan lookup, and setup review data.
- `AuthSessionService`: development login/session contract for role-aware UI flows.
- `WorkContextService`: one-call inspection screen context for tablet entry, including live capability metrics.
- `InspectionMeasurementService`: operator measurement entry, active lock enforcement, alert creation, inspection phase capture, and global/part-level drift rule resolution.
- `WesternElectricRuleService`: Western Electric drift rules used directly and as part of Nelson-style detection.
- `AlertOverrideService`: permission-based lock override and audit creation.
- `MaterialChangeLogService`: material lot traceability logging.
- `JobTagService`: persistent job context tags for part-specific inspection fields.
- `JobNoteService`: timestamped operator/job notes for handoff and issue history.
- `InspectionFrequencyService`: time, quantity, and event frequency evaluation.
- `ChartDataService`: chart-ready measurement points with moving range, limits, specs, and violations.
- `QaSummaryExportService`: COA-style summary calculations and CSV export.
- `JobReviewService`: part/job review data, editable inspection entries, and limit-status flags for History highlighting.
- `HistoryExportService`: raw inspection, job history, drift alert, and material change CSV exports.
- `OfflineSyncService`: first batch upload contract for retry-safe tablet/offline writes.

## Inspection UI behavior

Inspection entry is organized around top-level job data, part-specific job tags, material lot entries, and ordered inspection items. Inspection items can be measured variables or accept/reject attributes. The supported operator inspection phases are Startup, Setup, In Process, and Spool; coil/material changes are captured through material/job data rather than as a standalone operator phase.

The setup/admin UI includes Parts & Inspections, Users, Rules, Import, and History. History combines the previous review/report/job-data functions into Ledger, Charts, and Export views with shared job/part filters.

The browser UI supports keyboard-style USB measurement devices by focusing the target sample field, cleaning device strings down to numeric values, and advancing to the next field when Enter is received. Devices that require direct serial or HID communication should be added through a dedicated Web Serial/WebHID profile layer so the inspection workflow does not need to change.

## Next Architecture Step

Keep SQLite as the pilot database while validating production workflows. The next hardening work is backup/restore practice, authentication/session hardening, offline queue conflict handling, and broader History filtering. If the pilot requires a separate database engine, add an EF Core/SQL Server provider behind the existing repository boundary.

