# SPC-Star Patch Verification - 2026-09-25

This record documents the final verified state of the 2026-09-25 pilot-server thumb-drive patch.

## Final Runtime Decision

SPC-Star must run as the `SPC-Star` Windows Service on the pilot server.

The patch must not depend on:

- IIS
- an open Administrator PowerShell window
- a logged-in server user session
- the accidental `SPC-Star Server` scheduled-task runtime path

The only scheduled task expected in the standard deployment is `SPC-Star Daily Backup`.

## Prior Failure Cause

The patch drifted away from the intended Windows Service runtime path and used a scheduled-task launch path. That was incorrect for the pilot server.

The patch was corrected so it:

- removes the accidental legacy `SPC-Star Server` scheduled task if present
- removes old service names if present
- recreates the `SPC-Star` Windows Service
- starts SPC-Star through the service
- verifies `http://localhost:5000/health`
- imports the corrected setup workbook only after the service is healthy

## Thumb-Drive Package

The thumb drive root contains only:

- `D:\SPCstar`
- Windows-created hidden `System Volume Information`

The patch folder contains only:

- `D:\SPCstar\app`
- `D:\SPCstar\imports`
- `D:\SPCstar\UPDATE-SPCSTAR.ps1`

There is exactly one PowerShell script on the thumb drive:

- `D:\SPCstar\UPDATE-SPCSTAR.ps1`

IT command:

```powershell
cd D:\SPCstar
.\UPDATE-SPCSTAR.ps1
```

## Update Script Behavior

`UPDATE-SPCSTAR.ps1` performs the update in this order:

1. verifies the app package and workbook exist
2. creates required install/data/backup/log/archive/quarantine folders
3. stops and removes old `SPC-Star`, `SPC-Star Server`, or duplicate service names if present
4. removes the accidental legacy `SPC-Star Server` scheduled task if present
5. stops any running `SPCStar.Api` process
6. backs up current data files from `C:\Program Files\SPCstar\data`
7. mirrors `D:\SPCstar\app` to `C:\Program Files\SPCstar\app`
8. creates/updates inbound firewall rule `SPC-Star TCP 5000`
9. creates the `SPC-Star` Windows Service with startup type `Automatic`
10. configures service restart recovery
11. starts the `SPC-Star` Windows Service
12. waits for `http://localhost:5000/health`
13. imports `D:\SPCstar\imports\01 - SPC-Star Parts Inspections Materials Import - LATEST.xlsx` with `replaceSetup=true`
14. writes `C:\Program Files\SPCstar\UPDATE-COMPLETE.txt`

## Preserved Server Data

The patch preserves:

- users
- roles
- machines
- jobs
- measurements
- completed inspections
- failed inspections
- lock history
- lock overrides
- job notes
- material changes
- audit records
- `C:\Program Files\SPCstar\data`
- `C:\Program Files\SPCstar\backups`
- `C:\Program Files\SPCstar\quarantine`
- `C:\Program Files\SPCstar\data\archives`

## Refreshed Setup Data

The workbook import refreshes setup/master data:

- parts
- operations
- inspection variables and attributes
- spec limits
- inspection phases
- job data fields
- material fields
- control limits

## Verified Package State

Verified on 2026-09-25:

- `D:\SPCstar\UPDATE-SPCSTAR.ps1` PowerShell parse: OK
- exactly one `.ps1` file on the thumb drive
- app package is self-contained
- `Microsoft.NETCore.App` included framework: `8.0.26`
- `Microsoft.AspNetCore.App` included framework: `8.0.26`
- thumb-drive app command-line startup path works
- health endpoint responds
- workbook import succeeds against a temporary database
- unit tests pass: `214/214`

Workbook hash:

```text
SHA256 21C93A442EE07E091231B704713E8EBB071F7CF9B07581CA81E23F3FAA4E53A5
```

The thumb-drive workbook hash matched:

```text
C:\Users\snapj\OneDrive\Desktop\SPC-Star Load\01 - SPC-Star Parts Inspections Materials Import - LATEST.xlsx
D:\SPCstar\imports\01 - SPC-Star Parts Inspections Materials Import - LATEST.xlsx
```

## Workbook Audit

Needle End of Spool audit passed with `Issues: 0`.

Expected End of Spool counts:

- cutting-edge needle parts: 19 rows, order 1-19
- taperpoint needle parts: 10 rows, order 1-10

## GitHub State

Deployment documentation and repo scripts were corrected and pushed to GitHub.

Latest pushed commit at time of verification:

```text
af21945 Restore Windows service deployment path
```

## Important Notes

Do not add extra files to the thumb drive root or patch folder for handoff.

Do not reintroduce the scheduled-task runtime path for SPC-Star itself. The application runtime owner is the `SPC-Star` Windows Service. The daily backup may remain a scheduled task.
