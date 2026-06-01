# SPC Star Architecture Notes

## Current shape

SPC Star currently has three projects:

- `SPCStar.Core`: domain entities and business services.
- `SPCStar.Api`: minimal HTTP API over the core services.
- `SPCStar.Api/wwwroot`: browser/tablet inspection UI served by the API.
- `SPCStar.SmokeTests`: dependency-free executable tests for sandbox-safe verification.

The xUnit project remains in place for normal development and CI.

## Current storage

The app uses `ISpcRepository` with an `InMemorySpcRepository` implementation and a `FileBackedSpcRepository` for local JSON persistence. This keeps business rules isolated and testable before adding EF Core/SQLite persistence.

SQL schema scripts live in `database/` and mirror the initial relational model.

## Implemented services

- `SetupImportService`: row-type CSV setup validation and upsert for job data, material requirements, measured variables, and accept/reject attributes.
- `SetupManagementService`: users, global settings, manual part/operation/inspection setup, and part-specific job data field setup.
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
- `JobReviewService`: part/job review data, editable inspection entries, and limit-status flags for review highlighting.
- `HistoryExportService`: raw inspection, job history, drift alert, and material change CSV exports.
- `OfflineSyncService`: first batch upload contract for retry-safe tablet/offline writes.

## Inspection UI behavior

Inspection entry is organized around top-level job data, part-specific job tags, material lot entries, measured variables, and accept/reject attributes. The supported inspection phases are Startup, Setup, In Process, and Spool.

The browser UI supports keyboard-style USB measurement devices by focusing the target sample field, cleaning device strings down to numeric values, and advancing to the next field when Enter is received. Devices that require direct serial or HID communication should be added through a dedicated Web Serial/WebHID profile layer so the inspection workflow does not need to change.

## Next architecture step

Move from local JSON persistence to the production database layer, then harden auth/session behavior and offline queue handling. Keep the service APIs stable so tests continue to protect the manufacturing rules while storage changes underneath.

