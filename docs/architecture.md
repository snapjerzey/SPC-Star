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
- `SetupManagementService`: permission-controlled users, machines, rules, manual inspection setup, job data fields and materials. Capability has no global acceptance thresholds.
- `SetupQueryService`: tablet setup snapshot, part lookup, job data field lookup, material field lookup, inspection plan lookup, and setup review data.
- `AuthSessionService`: credential-bound sessions with persistent token hashes in a separate SQLite session store.
- `WorkContextService`: one-call inspection screen context for tablet entry, including live capability metrics.
- `InspectionMeasurementService`: operator measurement entry, operator shift stamping, active lock enforcement, alert creation, inspection phase capture, and global/part-level drift rule resolution.
- `WesternElectricRuleService`: Western Electric drift rules used directly and as part of Nelson-style detection.
- `AlertOverrideService`: permission-based lock override and audit creation.
- `MaterialChangeLogService`: material lot traceability logging.
- `JobTagService`: persistent job context tags for part-specific inspection fields.
- `JobNoteService`: server-assigned note authors/timestamps, authenticated job access and append-only QA responses.
- `InspectionFrequencyService` and `MachineCounterSchedule`: time/event requirements and stacked quantity frequencies anchored to the actual current run's starting counter; failed inspections remain due for immediate repeat.
- `ChartDataService`: chart-ready measurement points with moving range, limits, specs, and violations.
- `QaSummaryExportService`: job and part summary calculations with CSV export.
- `JobReviewService`: part/job review data, editable inspection entries, and limit-status flags for History highlighting.
- `HistoryExportService`: raw inspection, job history, drift alert, and material change CSV exports, including operator shift on new inspection and alert rows.
- `HistoryIssueSummaryService`: Top Issues summaries for out-of-spec and rejected-attribute events, excluding custom WATCH and User Error corrections.
- `ChangeHistoryService`: read-only history of saved setup/configuration changes and authorized corrections.
- `PartQualityService`: finalized inspection totals and variable-specific failure review.
- `OfflineSyncService`: first batch upload contract for retry-safe tablet/offline writes.

## Inspection UI behavior

Inspection entry is organized around top-level job data, part-specific job tags, material lot entries, and ordered inspection items. Inspection items can be measured variables or accept/reject attributes. The supported operator inspection phases are Startup, Setup, In Process, Coil Change, Spool, and End of Spool; material lot details are still captured through material/job data fields when the inspection plan requires them.

Setup contains Parts & Inspections, Users, Machines and Rules. Data contains Ledger, Part Quality, Charts, Top Issues and certificate functions. The reporting views share nine filters. Admin contains Notes, Change History and restricted Archive. Operators and Line Techs have inspection-side Job History, not the Data reporting views. QA can export and maintain setup but cannot import data/configuration. QA - Read only can view/export Data and generate certificates, without editing records. System Managers retain restricted database maintenance and import permissions.

The browser UI supports keyboard-style USB measurement devices by focusing the target sample field, cleaning device strings down to numeric values, and advancing to the next field when Enter is received. It also supports a Web Serial text-gauge profile for USB devices that expose serial readings instead of acting like a keyboard. Web Serial requires a supporting desktop browser such as Chrome or Edge and a secure browser context, so shop-floor serial-gauge use should be served over HTTPS rather than plain `http://SERVER-NAME:5000`. WebHID/custom binary profiles should be added once the actual gauge models and report formats are known.

## Next Architecture Step

Keep SQLite as the pilot database while validating production workflows. The next hardening work is backup/restore practice, internal user/session hardening, offline queue conflict handling, cross-job note/material/machine/shift trend refinement, MES/OEE correlation exports, and saved QA report views. If the pilot requires a separate database engine, add an EF Core/SQL Server provider behind the existing repository boundary.

