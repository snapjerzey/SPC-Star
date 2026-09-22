# SPC-Star Windows Service Setup

This document is retained only as a legacy reference. The current pilot standard is not a Windows Service.

For the pilot server, SPC-Star runs through the Windows Scheduled Task named `SPC-Star Server`, installed and refreshed by the deployment scripts in `deploy`. That scheduled task is the supported way to keep SPC-Star running after PowerShell closes and after the server user logs out.

Use these documents for current server work:

- `deploy/README.md`
- `deploy/IT-SERVER-REFERENCE.md`

## Current Pilot Runtime

- Install root: `C:\Program Files\SPCstar`
- App files: `C:\Program Files\SPCstar\app`
- Live database: `C:\Program Files\SPCstar\data\spcstar.db`
- Backups: `C:\Program Files\SPCstar\backups`
- Logs: `C:\Program Files\SPCstar\logs\spcstar.log`
- Runtime owner: Windows Scheduled Task `SPC-Star Server`
- Daily backup task: Windows Scheduled Task `SPC-Star Daily Backup`
- Default port: `5000`

## Standard Install

Run PowerShell as Administrator from the current SPC-Star project or IT handoff package:

```powershell
.\deploy\install-server.ps1
```

## Standard Update

Run PowerShell as Administrator from the current SPC-Star project or IT handoff package:

```powershell
.\deploy\update-server.ps1
```

The update path preserves `C:\Program Files\SPCstar\data`, creates a backup, replaces the application files, restarts the scheduled task, and verifies `/health`.

## Verify

On the server:

```powershell
Invoke-WebRequest http://localhost:5000/health
```

From another computer on the network:

```text
http://SERVER-NAME:5000/
```

Replace `SERVER-NAME` with the actual server name or IP address.

## Legacy Service Note

Do not install a separate Windows Service for the current pilot unless the deployment standard is formally changed. Running both a service and the scheduled task can create port conflicts or make it unclear which process owns the app.
