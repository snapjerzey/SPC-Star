# SPC-Star Windows Service Setup

The current pilot standard is the `SPC-Star` Windows Service.

SPC-Star must keep running after the Administrator PowerShell window is closed and after the server user logs out. IIS is not part of the normal pilot runtime path.

Use these documents for current server work:

- `deploy/README.md`
- `deploy/IT-SERVER-REFERENCE.md`

## Current Pilot Runtime

- Install root: `C:\Program Files\SPCstar`
- App files: `C:\Program Files\SPCstar\app`
- Live database: `C:\Program Files\SPCstar\data\spcstar.db`
- Backups: `C:\Program Files\SPCstar\backups`
- Archives: `C:\Program Files\SPCstar\data\archives`
- Quarantine: `C:\Program Files\SPCstar\quarantine`
- Runtime owner: Windows Service `SPC-Star`
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

The update path preserves `C:\Program Files\SPCstar\data`, creates a backup, replaces the application files, restarts the Windows Service, and verifies `/health`.

## Verify

On the server:

```powershell
Get-Service -Name "SPC-Star"
Invoke-WebRequest http://localhost:5000/health
```

From another computer on the network:

```text
http://SERVER-NAME:5000/
```

Replace `SERVER-NAME` with the actual server name or IP address.
