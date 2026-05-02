# SPC Star Architecture Notes

## Current shape

SPC Star currently has three projects:

- `SPCStar.Core`: domain entities and business services.
- `SPCStar.Api`: minimal HTTP API over the core services.
- `SPCStar.SmokeTests`: dependency-free executable tests for sandbox-safe verification.

The xUnit project remains in place for normal development and CI.

## Current storage

The app intentionally uses `InMemorySpcRepository` for the first foundation slice. This keeps business rules isolated and testable before adding EF Core/SQLite persistence.

SQL schema scripts live in `database/` and mirror the initial relational model.

## Implemented services

- `SetupImportService`: CSV setup validation and upsert.
- `InspectionMeasurementService`: operator measurement entry, active lock enforcement, alert creation.
- `WesternElectricRuleService`: first Western Electric drift rules.
- `AlertOverrideService`: permission-based lock override and audit creation.
- `MaterialChangeLogService`: material lot traceability logging.
- `InspectionFrequencyService`: time, quantity, and event frequency evaluation.
- `ChartDataService`: chart-ready measurement points with moving range, limits, specs, and violations.
- `QaSummaryExportService`: COA-style summary calculations and CSV export.
- `HistoryExportService`: raw inspection, job history, drift alert, and material change CSV exports.

## Next architecture step

Move persistence behind an interface and add an EF Core SQLite implementation. Keep the service APIs stable so tests continue to protect the manufacturing rules while storage changes underneath.
