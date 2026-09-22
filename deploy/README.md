# SPC-Star Server Deployment

This folder contains the local-network deployment scripts for SPC-Star.

For a detailed IT handoff covering scheduled tasks, backups, restore, archive folders, and health checks, see `deploy/IT-SERVER-REFERENCE.md`.

Recommended server layout:

- `C:\Program Files\SPCstar\app` - published SPC-Star application files
- `C:\Program Files\SPCstar\data` - SPC-Star database/storage file
- `C:\Program Files\SPCstar\data\archives` - archived historical record files
- `C:\Program Files\SPCstar\backups` - local backup copies
- `C:\Program Files\SPCstar\logs` - server log output

SPC-Star uses a local SQLite database file by default. The scripts keep that file outside the app folder so updates can replace the application without wiping data.

The current pilot standard is a background Windows Scheduled Task named `SPC-Star Server`. SPC-Star should keep running after the Administrator PowerShell window is closed and after the server user logs out. IIS is not required for the normal pilot install.

## First Install

Run from the project folder on the server:

```powershell
.\deploy\install-server.ps1
```

The install creates two Windows Scheduled Tasks:

- `SPC-Star Server`: starts SPC-Star automatically when the server starts.
- `SPC-Star Daily Backup`: creates a daily SPC-Star database backup at `2:00 AM` by default.

To choose a different daily backup time:

```powershell
.\deploy\install-server.ps1 -BackupTime "03:00"
```

Default local network URL:

```text
http://SERVER-NAME:5000/
```

Use the server's Windows computer name or IP address from shop-floor computers.

For serial gauge workstations, the preferred operator-facing URL is `https://spcstar.bihler.com/` with a trusted internal certificate. Plain `http://spcstar.bihler.com:5000` can load SPC-Star, but browsers usually block Web Serial access from that non-secure network URL. See `deploy/IT-SERVER-REFERENCE.md`.

## Update Existing Server

After pulling the latest SPC-Star code onto the server:

```powershell
.\deploy\update-server.ps1
```

This stops the scheduled task, creates a database backup, publishes the newest app files, and restarts the scheduled task.
It also installs or refreshes the `SPC-Star Daily Backup` scheduled task.

For thumb-drive handoff patches, IT should run the update script from the root of the handoff package. The patch must preserve `C:\Program Files\SPCstar\data`, refresh the app files, restart the `SPC-Star Server` scheduled task, and verify `/health`.

## Backup Only

```powershell
.\deploy\backup-data.ps1
```

Backups are stored in `C:\Program Files\SPCstar\backups` using the naming format `MMDDYY Backup HHMM.db`, for example `081226 Backup 1430.db`. Backups do not overwrite existing backup files.

The daily scheduled backup task runs this same backup script automatically. This gives SPC-Star its own local database backups in addition to the server's normal daily backup process.

When SPC-Star is running, the backup script asks the local SPC-Star server to create an online SQLite backup. Operators can stay logged in and continue submitting inspections while the backup is created. The backup captures a consistent snapshot of all data saved before the backup finishes; newer submissions continue into the live database and will be included in the next backup.

If SPC-Star is stopped or unavailable, the script falls back to a direct file copy for offline recovery use.

Archon/System Manager users can also create a manual backup inside SPC-Star from `Setup > Archive > Database Backup`. Manual backups use the same `C:\Program Files\SPCstar\backups` folder through `SPCSTAR_BACKUP_PATH`.

For in-app restore testing, use `Setup > Archive > Database Test / Restore`. `Clear History Data` clears jobs, measurements, notes, locks, overrides, materials, tags, and other historical records while keeping users, machines, parts, inspection plans, rules, specs, and control limits. `Restore Latest Backup` restores the newest `.db` file from `C:\Program Files\SPCstar\backups`.

Before clearing history data or restoring, SPC-Star copies the current database into `C:\Program Files\SPCstar\quarantine` using the naming format `MMDDYY Quarantine HHMM.db`. The server start script sets this folder through `SPCSTAR_QUARANTINE_PATH`.

If SPC-Star cannot run and IT must restore manually, stop the `SPC-Star Server` scheduled task, copy the current suspect database to `C:\Program Files\SPCstar\quarantine`, restore the selected known-good backup to `C:\Program Files\SPCstar\data\spcstar.db`, then restart the scheduled task and verify `/health`.

## Archiving Old Records

Use `Setup > Archive` inside SPC-Star when old historical records need to be removed from the live database for space management while keeping the seven-year record hold.

Only Archon/System Manager access can create an archive. The workflow previews record counts for a selected cutoff date, requires System Manager credentials, requires typing `ARCHIVE`, writes a JSON archive file, and only then removes matching history records from the live database. Archive does not delete parts, inspection plans, users, machines, rules, specifications, or control limits.

Archive files are written to `C:\Program Files\SPCstar\data\archives` by the server start script through `SPCSTAR_ARCHIVE_PATH`. They should also be copied into the company's normal local retention location after creation.

## Notes

- The scripts use a Windows Scheduled Task named `SPC-Star Server` so the app can start automatically.
- The server must allow inbound traffic on the configured port, default `5000`.
- Operators do not install SPC-Star locally. They open the server URL in a browser.
- Keep the data folder and backup folder out of the app publish folder.
- Full lockouts are for out-of-spec measured values and rejected attributes. Process drift is shown beside the inspection variable as a warning and is still recorded for review.

